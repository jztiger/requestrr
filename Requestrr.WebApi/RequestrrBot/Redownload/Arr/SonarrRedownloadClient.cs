using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Sonarr;

namespace Requestrr.WebApi.RequestrrBot.Redownload.Arr
{
    // Self-contained Sonarr HTTP client for the /redownload flow. Deliberately does NOT touch
    // SonarrClientV4 — keeps upstream merge surface zero. Own HttpGet/Post/Put/Delete copied
    // from the SonarrClientV4 pattern.
    public class SonarrRedownloadClient : IRedownloadMediaClient, ISonarrRedownloadLookup
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<SonarrRedownloadClient> _logger;
        private readonly SonarrSettingsProvider _sonarrSettingsProvider;
        private SonarrSettings SonarrSettings => _sonarrSettingsProvider.Provide();
        private string BaseURL => GetBaseURL(SonarrSettings);

        public SonarrRedownloadClient(IHttpClientFactory httpClientFactory, ILogger<SonarrRedownloadClient> logger, SonarrSettingsProvider sonarrSettingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _sonarrSettingsProvider = sonarrSettingsProvider;
        }

        // ---- ISonarrRedownloadLookup -------------------------------------------------------

        public async Task<SonarrSeriesInfo> FindSeriesByTvdbIdAsync(int tvdbId)
        {
            var response = await HttpGetAsync($"{BaseURL}/series?tvdbId={tvdbId}");
            await EnsureSuccessAsync(response);

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());
            var match = json.FirstOrDefault();

            return match == null ? null : ParseSeries((JObject)match);
        }

        public async Task<SonarrEpisodeInfo> FindEpisodeAsync(int seriesId, int season, int episode)
        {
            var response = await HttpGetAsync($"{BaseURL}/episode?seriesId={seriesId}&seasonNumber={season}");
            await EnsureSuccessAsync(response);

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());
            var match = json.FirstOrDefault(x => (int)x["episodeNumber"] == episode);

            return match == null ? null : ParseEpisode((JObject)match);
        }

        public async Task<MediaFileInfo> GetEpisodeFileAsync(int episodeFileId)
        {
            var response = await HttpGetAsync($"{BaseURL}/episodefile/{episodeFileId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            await EnsureSuccessAsync(response);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return ParseEpisodeFile(json);
        }

        public async Task<IReadOnlyList<SonarrSeriesInfo>> FindSeriesByTitleAsync(string title)
        {
            var response = await HttpGetAsync($"{BaseURL}/series");
            await EnsureSuccessAsync(response);

            var json = JArray.Parse(await response.Content.ReadAsStringAsync());

            return json
                .Where(x => string.Equals((string)x["title"], title, StringComparison.OrdinalIgnoreCase))
                .Select(x => ParseSeries((JObject)x))
                .ToArray();
        }

        // ---- IRedownloadMediaClient -------------------------------------------------------

        public async Task<string> GetRecycleBinPathAsync()
        {
            var response = await HttpGetAsync($"{BaseURL}/config/mediamanagement");
            await EnsureSuccessAsync(response);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return (string)json["recycleBin"] ?? "";
        }

        public async Task<MediaFileInfo> GetCurrentFileAsync(RedownloadTarget t)
        {
            var episodeResponse = await HttpGetAsync($"{BaseURL}/episode/{t.ArrId}");
            await EnsureSuccessAsync(episodeResponse);

            var episodeJson = JObject.Parse(await episodeResponse.Content.ReadAsStringAsync());
            var episodeFileId = (int)episodeJson["episodeFileId"];

            if (episodeFileId == 0)
            {
                return null;
            }

            return await GetEpisodeFileAsync(episodeFileId);
        }

        public async Task EnsureMonitoredAsync(RedownloadTarget t)
        {
            var response = await HttpGetAsync($"{BaseURL}/episode/{t.ArrId}");
            await EnsureSuccessAsync(response);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            if ((bool)json["monitored"] == false)
            {
                json["monitored"] = true;
                var putResponse = await HttpPutAsync($"{BaseURL}/episode/{t.ArrId}", json.ToString());
                await EnsureSuccessAsync(putResponse);
            }
        }

        public async Task<long?> GetLastGrabHistoryIdAsync(RedownloadTarget t)
        {
            // Live-verified against this Sonarr instance 2026-08-08: the /history eventType query
            // param is bound as a numeric enum value, NOT the lowercase string ("grabbed") that
            // appears in the JSON response body — eventType=grabbed 400s with "value is not valid",
            // eventType=1 works and returns records with eventType:"grabbed". 1 = HistoryEventType.Grabbed.
            var response = await HttpGetAsync($"{BaseURL}/history?episodeId={t.ArrId}&eventType=1&pageSize=1&sortKey=date&sortDirection=descending");
            await EnsureSuccessAsync(response);

            var json = JObject.Parse(await response.Content.ReadAsStringAsync());

            return ParseNewestGrabId(json);
        }

        public async Task DeleteFileAsync(int fileId)
        {
            var response = await HttpDeleteAsync($"{BaseURL}/episodefile/{fileId}");
            await EnsureSuccessAsync(response);
        }

        public async Task MarkHistoryFailedAsync(long historyId)
        {
            var response = await HttpPostAsync($"{BaseURL}/history/failed/{historyId}", "");
            await EnsureSuccessAsync(response);
        }

        public async Task TriggerSearchAsync(RedownloadTarget t)
        {
            var payload = new JObject
            {
                ["name"] = "EpisodeSearch",
                ["episodeIds"] = new JArray(t.ArrId),
            };

            var response = await HttpPostAsync($"{BaseURL}/command", payload.ToString());
            await EnsureSuccessAsync(response);
        }

        public async Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName)
        {
            var recycleBin = await GetRecycleBinPathAsync();

            if (string.IsNullOrEmpty(recycleBin))
            {
                return false;
            }

            var response = await HttpGetAsync($"{BaseURL}/manualimport?folder={Uri.EscapeDataString(recycleBin)}&filterExistingFiles=false");
            await EnsureSuccessAsync(response);

            var candidates = JArray.Parse(await response.Content.ReadAsStringAsync());
            var candidate = FindRecycleBinCandidate(candidates, originalFileName) as JObject;

            if (candidate == null)
            {
                _logger.LogWarning($"RestoreFromRecycleBinAsync: no manualimport candidate matched basename \"{originalFileName}\" under recycle bin \"{recycleBin}\"");
                return false;
            }

            var payload = BuildManualImportPayload(candidate, t);

            var importResponse = await HttpPostAsync($"{BaseURL}/command", payload.ToString());
            await EnsureSuccessAsync(importResponse);

            return true;
        }

        // ---- pure parsers (unit-tested) -------------------------------------------------------

        public static MediaFileInfo ParseEpisodeFile(JObject j)
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

        public static long? ParseNewestGrabId(JObject historyPage)
        {
            var records = historyPage["records"] as JArray;

            if (records == null || records.Count == 0)
            {
                return null;
            }

            return (long)records[0]["id"];
        }

        // Builds the ManualImport command payload for a matched recycle-bin candidate. Prefers the
        // candidate's own auto-matched series/episode ids; falls back to the target's when the
        // candidate is missing them. releaseGroup can be null on the candidate → send "".
        public static JObject BuildManualImportPayload(JObject candidate, RedownloadTarget t)
        {
            var seriesId = (int?)candidate["series"]?["id"] ?? t.SeriesId;
            var episodeIds = (candidate["episodes"] as JArray)?.Select(x => (int)x["id"]).ToArray();

            if (episodeIds == null || episodeIds.Length == 0)
            {
                episodeIds = new[] { t.ArrId };
            }

            var file = new JObject
            {
                ["path"] = (string)candidate["path"],
                ["seriesId"] = seriesId,
                ["episodeIds"] = new JArray(episodeIds),
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

        public static JToken FindRecycleBinCandidate(JArray manualImportItems, string originalFileName)
        {
            if (manualImportItems == null)
            {
                return null;
            }

            foreach (var item in manualImportItems)
            {
                var path = (string)item["path"];

                if (path != null && string.Equals(Path.GetFileName(path), originalFileName, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return null;
        }

        // ---- private mapping helpers -------------------------------------------------------

        private static SonarrSeriesInfo ParseSeries(JObject j)
        {
            return new SonarrSeriesInfo
            {
                Id = (int)j["id"],
                SeriesType = (string)j["seriesType"],
                Path = (string)j["path"],
                Title = (string)j["title"],
            };
        }

        private static SonarrEpisodeInfo ParseEpisode(JObject j)
        {
            return new SonarrEpisodeInfo
            {
                Id = (int)j["id"],
                EpisodeFileId = (int)j["episodeFileId"],
                Monitored = (bool)j["monitored"],
            };
        }

        // ---- error handling (S-2) -------------------------------------------------------

        // Replaces bare EnsureSuccessStatusCode() at every call site in this file. A destructive
        // flow (delete-then-search) failing silently with just "500 Internal Server Error" and no
        // body makes on-call debugging a guessing game; this reads and includes the response body
        // (truncated) plus the request path so the arr's actual error message reaches the log line
        // that already wraps every call site (RedownloadEngine/RedownloadWorkflow already log the
        // exception + its Message).
        //
        // NOT included: the API key. It is only ever sent via the X-Api-Key header (see
        // HttpGetAsync/HttpPostAsync/HttpPutAsync/HttpDeleteAsync below) -- never appended to the
        // URL's query string -- so RequestUri.PathAndQuery below can never contain it.
        private static async Task EnsureSuccessAsync(HttpResponseMessage response)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                body = $"<failed to read response body: {ex.Message}>";
            }

            if (body != null && body.Length > 500)
            {
                body = body.Substring(0, 500) + "...(truncated)";
            }

            var urlPath = response.RequestMessage?.RequestUri?.PathAndQuery ?? "<unknown url>";

            throw new HttpRequestException($"Sonarr API call failed: {(int)response.StatusCode} {response.StatusCode} at {urlPath} -- {body}");
        }

        // ---- HTTP plumbing (copied from the SonarrClientV4 pattern) -------------------------------------------------------

        private static string GetBaseURL(SonarrSettings settings)
        {
            var protocol = settings.UseSSL ? "https" : "http";
            return $"{protocol}://{settings.Hostname}:{settings.Port}{settings.BaseUrl}/api/v3";
        }

        private async Task<HttpResponseMessage> HttpGetAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Api-Key", SonarrSettings.ApiKey);

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
            postRequest.Headers.Add("X-Api-Key", SonarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.PostAsync(url, postRequest);
        }

        private async Task<HttpResponseMessage> HttpPutAsync(string url, string content)
        {
            var putRequest = new StringContent(content);
            putRequest.Headers.Clear();
            putRequest.Headers.Add("Content-Type", "application/json");
            putRequest.Headers.Add("X-Api-Key", SonarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.PutAsync(url, putRequest);
        }

        private async Task<HttpResponseMessage> HttpDeleteAsync(string url)
        {
            var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Api-Key", SonarrSettings.ApiKey);

            var client = _httpClientFactory.CreateClient();
            return await client.SendAsync(request);
        }
    }
}
