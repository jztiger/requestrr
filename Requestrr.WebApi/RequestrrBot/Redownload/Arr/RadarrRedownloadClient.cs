using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr;

namespace Requestrr.WebApi.RequestrrBot.Redownload.Arr
{
    // Self-contained Radarr HTTP client for the /redownload flow. Deliberately does NOT touch
    // RadarrClientV3 — keeps upstream merge surface zero. Own HttpGet/Post/Put/Delete copied
    // from the RadarrClientV3 pattern. Reuses SonarrRedownloadClient.FindRecycleBinCandidate,
    // which is arr-agnostic (matches on manualimport candidate basename only).
    public class RadarrRedownloadClient : IRedownloadMediaClient, IRadarrRedownloadLookup
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<RadarrRedownloadClient> _logger;
        private readonly RadarrSettingsProvider _radarrSettingsProvider;
        private RadarrSettings RadarrSettings => _radarrSettingsProvider.Provide();
        private string BaseURL => GetBaseURL(RadarrSettings);

        public RadarrRedownloadClient(IHttpClientFactory httpClientFactory, ILogger<RadarrRedownloadClient> logger, RadarrSettingsProvider radarrSettingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _radarrSettingsProvider = radarrSettingsProvider;
        }

        // ---- IRadarrRedownloadLookup -------------------------------------------------------

        public async Task<RadarrMovieInfo> FindMovieByTmdbIdAsync(int tmdbId)
        {
            var response = await HttpGetAsync($"{BaseURL}/movie?tmdbId={tmdbId}");
            response.EnsureSuccessStatusCode();

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());
            var match = json.FirstOrDefault();

            return match == null ? null : ParseMovie((JObject)match);
        }

        public async Task<MediaFileInfo> GetMovieFileAsync(int movieFileId)
        {
            var response = await HttpGetAsync($"{BaseURL}/moviefile/{movieFileId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return ParseMovieFile(json);
        }

        public async Task<IReadOnlyList<RadarrMovieInfo>> FindMoviesByTitleYearAsync(string title, int year)
        {
            var response = await HttpGetAsync($"{BaseURL}/movie");
            response.EnsureSuccessStatusCode();

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());

            return json
                .Where(x => string.Equals((string)x["title"], title, StringComparison.OrdinalIgnoreCase) && (int)x["year"] == year)
                .Select(x => ParseMovie((JObject)x))
                .ToArray();
        }

        // ---- IRedownloadMediaClient -------------------------------------------------------

        public async Task<string> GetRecycleBinPathAsync()
        {
            var response = await HttpGetAsync($"{BaseURL}/config/mediamanagement");
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return (string)json["recycleBin"] ?? "";
        }

        public async Task<MediaFileInfo> GetCurrentFileAsync(RedownloadTarget t)
        {
            var movieResponse = await HttpGetAsync($"{BaseURL}/movie/{t.ArrId}");
            movieResponse.EnsureSuccessStatusCode();

            var movieJson = JObject.Parse(await movieResponse.Content.ReadAsStringAsync());
            var movieFileId = (int)movieJson["movieFileId"];

            if (movieFileId == 0)
            {
                return null;
            }

            return await GetMovieFileAsync(movieFileId);
        }

        public async Task EnsureMonitoredAsync(RedownloadTarget t)
        {
            var response = await HttpGetAsync($"{BaseURL}/movie/{t.ArrId}");
            response.EnsureSuccessStatusCode();

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            if ((bool)json["monitored"] == false)
            {
                json["monitored"] = true;
                var putResponse = await HttpPutAsync($"{BaseURL}/movie/{t.ArrId}", json.ToString());
                putResponse.EnsureSuccessStatusCode();
            }
        }

        public async Task<long?> GetLastGrabHistoryIdAsync(RedownloadTarget t)
        {
            // Live-verified against this Radarr instance 2026-08-08: GET /history/movie?movieId=N
            // returns a plain JSON ARRAY (not Sonarr's paged {records:[]} object), newest first,
            // and mixes event types (downloadFolderImported, movieFileDeleted, grabbed, ...) rather
            // than reliably honoring an eventType filter — so fetch unfiltered and filter client-side.
            var response = await HttpGetAsync($"{BaseURL}/history/movie?movieId={t.ArrId}");
            response.EnsureSuccessStatusCode();

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());

            return ParseNewestGrabIdFromArray(json);
        }

        public async Task DeleteFileAsync(int fileId)
        {
            var response = await HttpDeleteAsync($"{BaseURL}/moviefile/{fileId}");
            response.EnsureSuccessStatusCode();
        }

        public async Task MarkHistoryFailedAsync(long historyId)
        {
            var response = await HttpPostAsync($"{BaseURL}/history/failed/{historyId}", "");
            response.EnsureSuccessStatusCode();
        }

        public async Task TriggerSearchAsync(RedownloadTarget t)
        {
            var payload = new JObject
            {
                ["name"] = "MoviesSearch",
                ["movieIds"] = new JArray(t.ArrId),
            };

            var response = await HttpPostAsync($"{BaseURL}/command", payload.ToString());
            response.EnsureSuccessStatusCode();
        }

        public async Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName)
        {
            var recycleBin = await GetRecycleBinPathAsync();

            if (string.IsNullOrEmpty(recycleBin))
            {
                return false;
            }

            var response = await HttpGetAsync($"{BaseURL}/manualimport?folder={Uri.EscapeDataString(recycleBin)}&filterExistingFiles=false");
            response.EnsureSuccessStatusCode();

            var candidates = JArray.Parse(await response.Content.ReadAsStringAsync());
            var candidate = SonarrRedownloadClient.FindRecycleBinCandidate(candidates, originalFileName) as JObject;

            if (candidate == null)
            {
                _logger.LogWarning($"RestoreFromRecycleBinAsync: no manualimport candidate matched basename \"{originalFileName}\" under recycle bin \"{recycleBin}\"");
                return false;
            }

            var payload = BuildManualImportPayload(candidate, t);

            var importResponse = await HttpPostAsync($"{BaseURL}/command", payload.ToString());
            importResponse.EnsureSuccessStatusCode();

            return true;
        }

        // ---- pure parsers (unit-tested) -------------------------------------------------------

        public static MediaFileInfo ParseMovieFile(JObject j)
        {
            return new MediaFileInfo
            {
                Id = (int)j["id"],
                Path = (string)j["path"],
                Quality = (string)j["quality"]?["quality"]?["name"],
                SizeBytes = (long?)j["size"] ?? 0,
                ReleaseGroup = (string)j["releaseGroup"],
            };
        }

        // History is a plain array (newest first). Filter client-side for the newest "grabbed"
        // event rather than relying on a server-side eventType filter (unreliable — see
        // GetLastGrabHistoryIdAsync). Null when no grabbed event is present.
        public static long? ParseNewestGrabIdFromArray(JArray history)
        {
            if (history == null)
            {
                return null;
            }

            var match = history.FirstOrDefault(x => string.Equals((string)x["eventType"], "grabbed", StringComparison.OrdinalIgnoreCase));

            return match == null ? (long?)null : (long)match["id"];
        }

        // Builds the ManualImport command payload for a matched recycle-bin candidate. Prefers the
        // candidate's own auto-matched movie id; falls back to the target's when the candidate is
        // missing it. releaseGroup can be null on the candidate → send "".
        public static JObject BuildManualImportPayload(JObject candidate, RedownloadTarget t)
        {
            var movieId = (int?)candidate["movie"]?["id"] ?? t.ArrId;

            var file = new JObject
            {
                ["path"] = (string)candidate["path"],
                ["movieId"] = movieId,
                ["quality"] = candidate["quality"],
                ["languages"] = candidate["languages"],
                ["releaseGroup"] = (string)candidate["releaseGroup"] ?? "",
            };

            return new JObject
            {
                ["name"] = "ManualImport",
                ["importMode"] = "move",
                ["files"] = new JArray(file),
            };
        }

        // ---- private mapping helpers -------------------------------------------------------

        private static RadarrMovieInfo ParseMovie(JObject j)
        {
            return new RadarrMovieInfo
            {
                Id = (int)j["id"],
                MovieFileId = (int?)j["movieFileId"] ?? 0,
                Monitored = (bool)j["monitored"],
                Title = (string)j["title"],
            };
        }

        // ---- HTTP plumbing (copied from the RadarrClientV3 pattern) -------------------------------------------------------

        private static string GetBaseURL(RadarrSettings settings)
        {
            var protocol = settings.UseSSL ? "https" : "http";
            return $"{protocol}://{settings.Hostname}:{settings.Port}{settings.BaseUrl}/api/v{settings.Version}";
        }

        private async Task<HttpResponseMessage> HttpGetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Api-Key", RadarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();

            using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
            {
                return await client.SendAsync(request, cts.Token);
            }
        }

        private async Task<HttpResponseMessage> HttpPostAsync(string url, string content)
        {
            var postRequest = new StringContent(content);
            postRequest.Headers.Clear();
            postRequest.Headers.Add("Content-Type", "application/json");
            postRequest.Headers.Add("X-Api-Key", RadarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.PostAsync(url, postRequest);
        }

        private async Task<HttpResponseMessage> HttpPutAsync(string url, string content)
        {
            var putRequest = new StringContent(content);
            putRequest.Headers.Clear();
            putRequest.Headers.Add("Content-Type", "application/json");
            putRequest.Headers.Add("X-Api-Key", RadarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.PutAsync(url, putRequest);
        }

        private async Task<HttpResponseMessage> HttpDeleteAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Api-Key", RadarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.SendAsync(request);
        }
    }
}
