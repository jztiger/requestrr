using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    // Stand-in for the real settings-backed provider that Task 9 wires up. A later task maps
    // real settings (Redownload.RecentItemsCount / HistoryFetchCount / AnimeRootFolder in
    // settings.json) onto this options object.
    public class RedownloadResolverOptions
    {
        public int RecentItemsCount { get; set; } = 5;
        public int HistoryFetchCount { get; set; } = 25;
        public string AnimeRootFolder { get; set; } = "/data/plexmedia/Anime";
    }

    public class RecentList
    {
        public IReadOnlyList<WatchedItem> Items { get; set; }
        public int AnimeSkipped { get; set; }
    }

    public class ResolveResult
    {
        public RedownloadTarget Target { get; set; }
        public string Error { get; set; } // Error == null → ok
    }

    public class RedownloadResolver
    {
        private const string NotManagedError = "not managed by Sonarr/Radarr";
        private const string NoFileError = "no file on record";
        private const string AmbiguousError = "couldn't be matched confidently";

        private readonly TautulliClient _tautulli;
        private readonly ISonarrRedownloadLookup _sonarr;
        private readonly IRadarrRedownloadLookup _radarr;
        private readonly RedownloadResolverOptions _options;

        public RedownloadResolver(TautulliClient tautulli, ISonarrRedownloadLookup sonarr, IRadarrRedownloadLookup radarr,
                                   RedownloadResolverOptions options)
        {
            _tautulli = tautulli;
            _sonarr = sonarr;
            _radarr = radarr;
            _options = options;
        }

        // fetch HistoryFetchCount, dedupe, anime-filter, keep RecentItemsCount
        public async Task<RecentList> BuildRecentListAsync()
        {
            var history = await FetchHistoryAsync(_options.HistoryFetchCount);
            var deduped = Dedupe(history);

            var animeCache = new Dictionary<string, bool>();
            var kept = new List<WatchedItem>();
            var animeSkipped = 0;

            foreach (var item in deduped)
            {
                if (kept.Count >= _options.RecentItemsCount)
                {
                    break;
                }

                if (item.IsMovie)
                {
                    kept.Add(item);
                    continue;
                }

                if (!animeCache.TryGetValue(item.GrandparentRatingKey, out var isAnime))
                {
                    isAnime = await IsAnimeSeriesAsync(item.GrandparentRatingKey);
                    animeCache[item.GrandparentRatingKey] = isAnime;
                }

                if (isAnime)
                {
                    animeSkipped++;
                    continue;
                }

                kept.Add(item);
            }

            return new RecentList { Items = kept, AnimeSkipped = animeSkipped };
        }

        // A series NOT found in Sonarr is NOT anime-filtered — it stays listed and fails later
        // at pick time via ResolveTargetAsync's "not managed by Sonarr/Radarr" error.
        private async Task<bool> IsAnimeSeriesAsync(string grandparentRatingKey)
        {
            var guids = await FetchGuidsAsync(grandparentRatingKey);
            var tvdbId = ExtractId(guids, "tvdb");

            if (!tvdbId.HasValue)
            {
                return false;
            }

            var series = await _sonarr.FindSeriesByTvdbIdAsync(tvdbId.Value);

            return series != null && IsAnime(series, _options.AnimeRootFolder);
        }

        // full resolution of ONE picked item
        public async Task<ResolveResult> ResolveTargetAsync(WatchedItem item)
        {
            return item.IsMovie
                ? await ResolveMovieAsync(item)
                : await ResolveEpisodeAsync(item);
        }

        private async Task<ResolveResult> ResolveMovieAsync(WatchedItem item)
        {
            var guids = await FetchGuidsAsync(item.RatingKey);
            var tmdbId = ExtractId(guids, "tmdb");
            var movie = tmdbId.HasValue ? await _radarr.FindMovieByTmdbIdAsync(tmdbId.Value) : null;

            if (movie == null)
            {
                return new ResolveResult { Error = NotManagedError };
            }

            if (movie.MovieFileId == 0)
            {
                return new ResolveResult { Error = NoFileError };
            }

            var file = await _radarr.GetMovieFileAsync(movie.MovieFileId);

            return new ResolveResult
            {
                Target = new RedownloadTarget
                {
                    IsMovie = true,
                    ArrId = movie.Id,
                    SeriesId = 0,
                    FileId = movie.MovieFileId,
                    Monitored = movie.Monitored,
                    DisplayTitle = item.Display,
                    FilePath = file?.Path,
                    Quality = file?.Quality,
                    SizeBytes = file?.SizeBytes ?? 0,
                    ReleaseGroup = file?.ReleaseGroup,
                }
            };
        }

        private async Task<ResolveResult> ResolveEpisodeAsync(WatchedItem item)
        {
            var guids = await FetchGuidsAsync(item.GrandparentRatingKey);
            var tvdbId = ExtractId(guids, "tvdb");
            var series = tvdbId.HasValue ? await _sonarr.FindSeriesByTvdbIdAsync(tvdbId.Value) : null;

            if (series == null)
            {
                return new ResolveResult { Error = NotManagedError };
            }

            var episode = await _sonarr.FindEpisodeAsync(series.Id, item.SeasonNumber, item.EpisodeNumber);

            if (episode == null)
            {
                return new ResolveResult { Error = AmbiguousError };
            }

            if (episode.EpisodeFileId == 0)
            {
                return new ResolveResult { Error = NoFileError };
            }

            var file = await _sonarr.GetEpisodeFileAsync(episode.EpisodeFileId);

            return new ResolveResult
            {
                Target = new RedownloadTarget
                {
                    IsMovie = false,
                    ArrId = episode.Id,
                    SeriesId = series.Id,
                    FileId = episode.EpisodeFileId,
                    Monitored = episode.Monitored,
                    DisplayTitle = item.Display,
                    FilePath = file?.Path,
                    Quality = file?.Quality,
                    SizeBytes = file?.SizeBytes ?? 0,
                    ReleaseGroup = file?.ReleaseGroup,
                }
            };
        }

        // Seams for testability: TautulliClient is a concrete class with no interface. Unit
        // tests subclass RedownloadResolver and override these two methods with in-memory
        // fakes instead of hitting HTTP. The public API above is exactly the brief's shape.
        protected virtual Task<IReadOnlyList<WatchedItem>> FetchHistoryAsync(int count)
        {
            return _tautulli.GetRecentHistoryAsync(count);
        }

        protected virtual Task<IReadOnlyList<string>> FetchGuidsAsync(string ratingKey)
        {
            return _tautulli.GetGuidsAsync(ratingKey);
        }

        // by RatingKey, keep first (= newest)
        public static IReadOnlyList<WatchedItem> Dedupe(IReadOnlyList<WatchedItem> history)
        {
            var seen = new HashSet<string>();
            var result = new List<WatchedItem>();

            foreach (var item in history)
            {
                if (seen.Add(item.RatingKey))
                {
                    result.Add(item);
                }
            }

            return result;
        }

        // the OR rule: seriesType == "anime" OR path is under animeRootFolder
        public static bool IsAnime(SonarrSeriesInfo series, string animeRootFolder)
        {
            if (series == null)
            {
                return false;
            }

            var byType = string.Equals(series.SeriesType, "anime", StringComparison.OrdinalIgnoreCase);
            var byPath = !string.IsNullOrEmpty(series.Path)
                         && !string.IsNullOrEmpty(animeRootFolder)
                         && series.Path.StartsWith(animeRootFolder.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase);

            return byType || byPath;
        }

        // "tvdb"/"tmdb" → int
        public static int? ExtractId(IReadOnlyList<string> guids, string scheme)
        {
            if (guids == null)
            {
                return null;
            }

            var prefix = scheme + "://";

            foreach (var guid in guids)
            {
                if (guid != null && guid.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(guid.Substring(prefix.Length), out var id))
                {
                    return id;
                }
            }

            return null;
        }
    }
}
