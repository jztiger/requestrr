using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Redownload;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli
{
    public class TautulliClient
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TautulliClient> _logger;
        private readonly TautulliSettingsProvider _tautulliSettingsProvider;
        private TautulliSettings TautulliSettings => _tautulliSettingsProvider.Provide();

        public TautulliClient(IHttpClientFactory httpClientFactory, ILogger<TautulliClient> logger, TautulliSettingsProvider tautulliSettingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _tautulliSettingsProvider = tautulliSettingsProvider;
        }

        public async Task<IReadOnlyList<WatchedItem>> GetRecentHistoryAsync(int count)
        {
            try
            {
                var response = await HttpGetAsync(_httpClientFactory.CreateClient(), TautulliSettings, $"cmd=get_history&length={count}&media_type=movie,episode");
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var apiResponse = JObject.Parse(jsonResponse);

                return ParseHistory(apiResponse);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting recent history from Tautulli: " + ex.Message);
            }

            throw new System.Exception("An error occurred while getting recent history from Tautulli");
        }

        public async Task<IReadOnlyList<string>> GetGuidsAsync(string ratingKey)
        {
            try
            {
                var response = await HttpGetAsync(_httpClientFactory.CreateClient(), TautulliSettings, $"cmd=get_metadata&rating_key={ratingKey}");
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var apiResponse = JObject.Parse(jsonResponse);

                return ParseGuids(apiResponse);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"An error occurred while getting guids for rating key \"{ratingKey}\" from Tautulli: " + ex.Message);
            }

            throw new System.Exception("An error occurred while getting guids from Tautulli");
        }

        public static IReadOnlyList<WatchedItem> ParseHistory(JObject apiResponse)
        {
            var rows = apiResponse["response"]?["data"]?["data"] as JArray ?? new JArray();
            var items = new List<WatchedItem>();

            foreach (var row in rows)
            {
                var mediaType = (string)row["media_type"];

                if (mediaType != "movie" && mediaType != "episode")
                {
                    continue;
                }

                var isMovie = mediaType == "movie";

                items.Add(new WatchedItem
                {
                    RatingKey = (string)row["rating_key"],
                    GrandparentRatingKey = isMovie ? null : (string)row["grandparent_rating_key"],
                    IsMovie = isMovie,
                    Title = (string)row["title"],
                    GrandparentTitle = isMovie ? null : (string)row["grandparent_title"],
                    Year = isMovie ? ParseIntOrZero(row["year"]) : 0,
                    SeasonNumber = isMovie ? 0 : ParseIntOrZero(row["parent_media_index"]),
                    EpisodeNumber = isMovie ? 0 : ParseIntOrZero(row["media_index"]),
                });
            }

            return items;
        }

        // Tautulli returns not-applicable numeric fields as "" rather than null/absent
        // (e.g. media_index on a movie row), which throws if cast directly to (int?).
        private static int ParseIntOrZero(JToken token)
        {
            return token != null && int.TryParse(token.ToString(), out var value) ? value : 0;
        }

        public static IReadOnlyList<string> ParseGuids(JObject apiResponse)
        {
            var guids = apiResponse["response"]?["data"]?["guids"] as JArray;

            return guids?.Select(x => (string)x).ToArray() ?? Array.Empty<string>();
        }

        public static async Task TestConnectionAsync(HttpClient httpClient, ILogger logger, TautulliSettings settings)
        {
            try
            {
                var response = await HttpGetAsync(httpClient, settings, "cmd=get_server_info");

                if (!response.IsSuccessStatusCode)
                {
                    throw new System.Exception("Invalid host, port, and/or api key");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                var apiResponse = JObject.Parse(jsonResponse);
                var result = (string)apiResponse["response"]?["result"];

                if (!string.Equals(result, "success", StringComparison.InvariantCultureIgnoreCase))
                {
                    throw new System.Exception("Invalid host, port, and/or api key");
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Error while testing Tautulli connection: " + ex.Message);
                throw new System.Exception("Invalid host and/or port");
            }
            catch (System.Exception ex)
            {
                logger.LogWarning(ex, "Error while testing Tautulli connection: " + ex.Message);

                if (ex.GetType() == typeof(System.Exception))
                {
                    throw;
                }
                else
                {
                    throw new System.Exception("Invalid host and/or port");
                }
            }
        }

        private static string GetBaseURL(TautulliSettings settings)
        {
            var protocol = settings.UseSSL ? "https" : "http";
            return $"{protocol}://{settings.Hostname}:{settings.Port}/api/v2";
        }

        private static async Task<HttpResponseMessage> HttpGetAsync(HttpClient client, TautulliSettings settings, string queryString)
        {
            client.Timeout = TimeSpan.FromSeconds(15);

            var url = $"{GetBaseURL(settings)}?apikey={settings.ApiKey}&{queryString}";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Add("Accept", "application/json");

            return await client.SendAsync(request);
        }
    }
}
