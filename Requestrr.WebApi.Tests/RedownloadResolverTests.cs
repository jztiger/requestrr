using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class RedownloadResolverTests
    {
        // ---- fakes -------------------------------------------------------

        private class FakeSonarrLookup : ISonarrRedownloadLookup
        {
            public Func<int, SonarrSeriesInfo> FindSeriesByTvdbId = _ => null;
            public Func<int, int, int, SonarrEpisodeInfo> FindEpisode = (seriesId, season, episode) => null;
            public Func<int, MediaFileInfo> GetEpisodeFile = _ => null;
            public Func<string, IReadOnlyList<SonarrSeriesInfo>> FindSeriesByTitle = _ => Array.Empty<SonarrSeriesInfo>();
            public int FindSeriesByTvdbIdCallCount;

            public Task<SonarrSeriesInfo> FindSeriesByTvdbIdAsync(int tvdbId)
            {
                FindSeriesByTvdbIdCallCount++;
                return Task.FromResult(FindSeriesByTvdbId(tvdbId));
            }
            public Task<SonarrEpisodeInfo> FindEpisodeAsync(int seriesId, int season, int episode) => Task.FromResult(FindEpisode(seriesId, season, episode));
            public Task<MediaFileInfo> GetEpisodeFileAsync(int episodeFileId) => Task.FromResult(GetEpisodeFile(episodeFileId));
            public Task<IReadOnlyList<SonarrSeriesInfo>> FindSeriesByTitleAsync(string title) => Task.FromResult(FindSeriesByTitle(title));
        }

        private class FakeRadarrLookup : IRadarrRedownloadLookup
        {
            public Func<int, RadarrMovieInfo> FindMovieByTmdbId = _ => null;
            public Func<int, MediaFileInfo> GetMovieFile = _ => null;
            public Func<string, int, IReadOnlyList<RadarrMovieInfo>> FindMoviesByTitleYear = (_, __) => Array.Empty<RadarrMovieInfo>();

            public Task<RadarrMovieInfo> FindMovieByTmdbIdAsync(int tmdbId) => Task.FromResult(FindMovieByTmdbId(tmdbId));
            public Task<MediaFileInfo> GetMovieFileAsync(int movieFileId) => Task.FromResult(GetMovieFile(movieFileId));
            public Task<IReadOnlyList<RadarrMovieInfo>> FindMoviesByTitleYearAsync(string title, int year) => Task.FromResult(FindMoviesByTitleYear(title, year));
        }

        // TautulliClient is a concrete class with an HTTP-only constructor path; the resolver's
        // two fetch methods are protected virtual seams so tests never need a real instance.
        private class TestResolver : RedownloadResolver
        {
            public IReadOnlyList<WatchedItem> History;
            public Dictionary<string, IReadOnlyList<string>> GuidsByRatingKey = new Dictionary<string, IReadOnlyList<string>>();
            public int? RequestedHistoryCount;
            public Dictionary<string, int> GuidLookupCallsByRatingKey = new Dictionary<string, int>();

            public TestResolver(ISonarrRedownloadLookup sonarr, IRadarrRedownloadLookup radarr, RedownloadResolverOptions options)
                : base(null, sonarr, radarr, options)
            {
            }

            protected override Task<IReadOnlyList<WatchedItem>> FetchHistoryAsync(int count)
            {
                RequestedHistoryCount = count;
                return Task.FromResult(History);
            }

            protected override Task<IReadOnlyList<string>> FetchGuidsAsync(string ratingKey)
            {
                GuidLookupCallsByRatingKey.TryGetValue(ratingKey, out var calls);
                GuidLookupCallsByRatingKey[ratingKey] = calls + 1;

                return Task.FromResult(GuidsByRatingKey.TryGetValue(ratingKey, out var guids) ? guids : Array.Empty<string>());
            }
        }

        // ---- IsAnime -------------------------------------------------------

        [Fact]
        public void IsAnime_TrueByType()
        {
            var series = new SonarrSeriesInfo { SeriesType = "anime", Path = "/data/plexmedia/TV/X" };

            Assert.True(RedownloadResolver.IsAnime(series, "/data/plexmedia/Anime"));
        }

        [Fact]
        public void IsAnime_TrueByPath()
        {
            var series = new SonarrSeriesInfo { SeriesType = "standard", Path = "/data/plexmedia/Anime/SpongeBob SquarePants" };

            Assert.True(RedownloadResolver.IsAnime(series, "/data/plexmedia/Anime"));
        }

        [Fact]
        public void IsAnime_HeMan()
        {
            var series = new SonarrSeriesInfo { SeriesType = "standard", Path = "/data/plexmedia/Anime/He-Man and the Masters of the Universe" };

            Assert.True(RedownloadResolver.IsAnime(series, "/data/plexmedia/Anime"));
        }

        [Fact]
        public void IsAnime_FalseForNormal()
        {
            var series = new SonarrSeriesInfo { SeriesType = "standard", Path = "/data/plexmedia/TV/Lioness" };

            Assert.False(RedownloadResolver.IsAnime(series, "/data/plexmedia/Anime"));
        }

        // ---- ExtractId -------------------------------------------------------

        [Fact]
        public void ExtractId_ParsesTvdbAndTmdb()
        {
            var guids = new[] { "imdb://tt123", "tmdb://456", "tvdb://789" };

            Assert.Equal(789, RedownloadResolver.ExtractId(guids, "tvdb"));
            Assert.Equal(456, RedownloadResolver.ExtractId(guids, "tmdb"));
            Assert.Null(RedownloadResolver.ExtractId(guids, "imdb-alt")); // scheme not present at all
            Assert.Null(RedownloadResolver.ExtractId(guids, "imdb"));    // present but non-numeric id ("tt123")
        }

        // ---- Dedupe -------------------------------------------------------

        [Fact]
        public void Dedupe_KeepsNewestPerRatingKey()
        {
            var history = new List<WatchedItem>
            {
                new WatchedItem { RatingKey = "1", Title = "Newest" },
                new WatchedItem { RatingKey = "2", Title = "Other" },
                new WatchedItem { RatingKey = "1", Title = "Older Duplicate" },
            };

            var result = RedownloadResolver.Dedupe(history);

            Assert.Equal(2, result.Count);
            Assert.Equal("Newest", result.Single(x => x.RatingKey == "1").Title);
        }

        // ---- BuildRecentListAsync -------------------------------------------------------

        [Fact]
        public async Task BuildRecentList_FiltersAnime_CountsSkips_Backfills()
        {
            var animeSeries = new SonarrSeriesInfo { Id = 1, SeriesType = "anime", Path = "/data/plexmedia/Anime/Whatever" };
            var normalSeries = new SonarrSeriesInfo { Id = 2, SeriesType = "standard", Path = "/data/plexmedia/TV/Whatever" };

            var sonarr = new FakeSonarrLookup
            {
                FindSeriesByTvdbId = tvdbId => tvdbId == 999 ? animeSeries : (tvdbId == 111 ? normalSeries : null),
            };

            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25 };
            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), options);

            var history = new List<WatchedItem>();

            for (var i = 1; i <= 25; i++)
            {
                var isAnimeRow = i <= 2;
                var gpKey = "gp" + i;

                history.Add(new WatchedItem
                {
                    RatingKey = "rk" + i,
                    GrandparentRatingKey = gpKey,
                    IsMovie = false,
                    Title = "Ep" + i,
                    GrandparentTitle = "Show" + i,
                    SeasonNumber = 1,
                    EpisodeNumber = i,
                });

                resolver.GuidsByRatingKey[gpKey] = new[] { isAnimeRow ? "tvdb://999" : "tvdb://111" };
            }

            resolver.History = history;

            var result = await resolver.BuildRecentListAsync();

            Assert.Equal(5, result.Items.Count);
            Assert.Equal(2, result.AnimeSkipped);
            Assert.Equal(new[] { "rk3", "rk4", "rk5", "rk6", "rk7" }, result.Items.Select(x => x.RatingKey));
            Assert.Equal(25, resolver.RequestedHistoryCount); // HistoryFetchCount actually reaches the fetch call
        }

        [Fact]
        public async Task BuildRecentList_AnimeLookupIsCachedPerDistinctSeries()
        {
            var animeSeries = new SonarrSeriesInfo { Id = 1, SeriesType = "anime", Path = "/data/plexmedia/Anime/Whatever" };
            var normalSeries = new SonarrSeriesInfo { Id = 2, SeriesType = "standard", Path = "/data/plexmedia/TV/Whatever" };

            var sonarr = new FakeSonarrLookup
            {
                FindSeriesByTvdbId = tvdbId => tvdbId == 999 ? animeSeries : (tvdbId == 111 ? normalSeries : null),
            };

            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25 };
            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), options);

            // 3 rows on one anime series (gp1), then 2 rows on one normal series (gp2) — a
            // realistic binge, not 1 row per series like the backfill test above.
            resolver.History = new List<WatchedItem>
            {
                new WatchedItem { RatingKey = "rk1", GrandparentRatingKey = "gp1", IsMovie = false, Title = "E1", GrandparentTitle = "Anime Show", SeasonNumber = 1, EpisodeNumber = 1 },
                new WatchedItem { RatingKey = "rk2", GrandparentRatingKey = "gp1", IsMovie = false, Title = "E2", GrandparentTitle = "Anime Show", SeasonNumber = 1, EpisodeNumber = 2 },
                new WatchedItem { RatingKey = "rk3", GrandparentRatingKey = "gp1", IsMovie = false, Title = "E3", GrandparentTitle = "Anime Show", SeasonNumber = 1, EpisodeNumber = 3 },
                new WatchedItem { RatingKey = "rk4", GrandparentRatingKey = "gp2", IsMovie = false, Title = "E1", GrandparentTitle = "Normal Show", SeasonNumber = 1, EpisodeNumber = 1 },
                new WatchedItem { RatingKey = "rk5", GrandparentRatingKey = "gp2", IsMovie = false, Title = "E2", GrandparentTitle = "Normal Show", SeasonNumber = 1, EpisodeNumber = 2 },
            };
            resolver.GuidsByRatingKey["gp1"] = new[] { "tvdb://999" };
            resolver.GuidsByRatingKey["gp2"] = new[] { "tvdb://111" };

            var result = await resolver.BuildRecentListAsync();

            Assert.Equal(new[] { "rk4", "rk5" }, result.Items.Select(x => x.RatingKey));
            Assert.Equal(3, result.AnimeSkipped); // AnimeSkipped counts items, not distinct series
            Assert.Equal(1, resolver.GuidLookupCallsByRatingKey["gp1"]); // 3 rows, same series → 1 lookup, cached
            Assert.Equal(1, resolver.GuidLookupCallsByRatingKey["gp2"]);
            Assert.Equal(2, sonarr.FindSeriesByTvdbIdCallCount); // one Sonarr call per distinct series, not per row
        }

        [Fact]
        public async Task BuildRecentList_AllAnime_YieldsEmptyListWithCount()
        {
            var animeSeries = new SonarrSeriesInfo { Id = 1, SeriesType = "anime", Path = "/data/plexmedia/Anime/Whatever" };
            var sonarr = new FakeSonarrLookup { FindSeriesByTvdbId = _ => animeSeries };

            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25 };
            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), options);

            var history = new List<WatchedItem>();

            for (var i = 1; i <= 6; i++)
            {
                var gpKey = "gp" + i;

                history.Add(new WatchedItem
                {
                    RatingKey = "rk" + i,
                    GrandparentRatingKey = gpKey,
                    IsMovie = false,
                    Title = "Ep" + i,
                    GrandparentTitle = "Show" + i,
                    SeasonNumber = 1,
                    EpisodeNumber = i,
                });

                resolver.GuidsByRatingKey[gpKey] = new[] { "tvdb://999" };
            }

            resolver.History = history;

            var result = await resolver.BuildRecentListAsync();

            Assert.Empty(result.Items);
            Assert.Equal(6, result.AnimeSkipped);
        }

        // ---- ResolveTargetAsync -------------------------------------------------------

        [Fact]
        public async Task ResolveTarget_NotInArr_ReturnsError()
        {
            var sonarr = new FakeSonarrLookup(); // FindSeriesByTvdbId defaults to null
            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["gp1"] = new[] { "tvdb://555" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("not managed", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_NoFile_ReturnsError()
        {
            var series = new SonarrSeriesInfo { Id = 10, SeriesType = "standard", Path = "/data/plexmedia/TV/Show" };
            var sonarr = new FakeSonarrLookup
            {
                FindSeriesByTvdbId = _ => series,
                FindEpisode = (seriesId, season, episode) => new SonarrEpisodeInfo { Id = 55, EpisodeFileId = 0, Monitored = true },
            };

            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["gp1"] = new[] { "tvdb://555" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("no file on record", result.Error);
        }

        // ---- title/year fallback (no usable GUID) -------------------------------------------------------

        [Fact]
        public async Task ResolveTarget_Episode_NoGuid_OneTitleMatch_Resolves()
        {
            var series = new SonarrSeriesInfo { Id = 10, SeriesType = "standard", Path = "/data/plexmedia/TV/Show" };
            var sonarr = new FakeSonarrLookup
            {
                FindSeriesByTitle = title => title == "Show" ? new[] { series } : Array.Empty<SonarrSeriesInfo>(),
                FindEpisode = (seriesId, season, episode) => new SonarrEpisodeInfo { Id = 55, EpisodeFileId = 99, Monitored = true },
                GetEpisodeFile = _ => new MediaFileInfo { Id = 99, Path = "/data/plexmedia/TV/Show/ep.mkv", Quality = "WEBDL-1080p", SizeBytes = 1000, ReleaseGroup = "GRP" },
            };

            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            // No tvdb guid at all — old metadata agent scenario.
            resolver.GuidsByRatingKey["gp1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Error);
            Assert.NotNull(result.Target);
            Assert.Equal(55, result.Target.ArrId);
            Assert.Equal(10, result.Target.SeriesId);
        }

        [Fact]
        public async Task ResolveTarget_Episode_NoGuid_TwoTitleMatches_ReturnsAmbiguousError()
        {
            var seriesA = new SonarrSeriesInfo { Id = 10, SeriesType = "standard", Path = "/data/plexmedia/TV/Show" };
            var seriesB = new SonarrSeriesInfo { Id = 11, SeriesType = "standard", Path = "/data/plexmedia/TV/Show (2019)" };
            var sonarr = new FakeSonarrLookup { FindSeriesByTitle = _ => new[] { seriesA, seriesB } };

            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["gp1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("couldn't be matched confidently", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_Episode_NoGuid_ZeroTitleMatches_ReturnsNotManagedError()
        {
            var sonarr = new FakeSonarrLookup(); // FindSeriesByTitle defaults to empty list
            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["gp1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("not managed", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_Movie_NoGuid_OneTitleMatch_Resolves()
        {
            var movie = new RadarrMovieInfo { Id = 42, MovieFileId = 77, Monitored = true, Title = "Sweet Home Alabama" };
            var file = new MediaFileInfo { Id = 77, Path = "/data/plexmedia/Movies/Sweet Home Alabama/movie.mkv", Quality = "WEBDL-2160p", SizeBytes = 3_000_000_000L, ReleaseGroup = "EDITH" };

            var radarr = new FakeRadarrLookup
            {
                FindMoviesByTitleYear = (title, year) => title == "Sweet Home Alabama" && year == 2002 ? new[] { movie } : Array.Empty<RadarrMovieInfo>(),
                GetMovieFile = _ => file,
            };

            var resolver = new TestResolver(new FakeSonarrLookup(), radarr, new RedownloadResolverOptions());
            // No tmdb guid at all — old metadata agent scenario.
            resolver.GuidsByRatingKey["rk1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem { RatingKey = "rk1", IsMovie = true, Title = "Sweet Home Alabama", Year = 2002 };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Error);
            Assert.NotNull(result.Target);
            Assert.Equal(42, result.Target.ArrId);
            Assert.Equal(77, result.Target.FileId);
        }

        [Fact]
        public async Task ResolveTarget_Movie_NoGuid_TwoTitleMatches_ReturnsAmbiguousError()
        {
            var movieA = new RadarrMovieInfo { Id = 42, MovieFileId = 77, Title = "Sweet Home Alabama" };
            var movieB = new RadarrMovieInfo { Id = 43, MovieFileId = 78, Title = "Sweet Home Alabama" };
            var radarr = new FakeRadarrLookup { FindMoviesByTitleYear = (_, __) => new[] { movieA, movieB } };

            var resolver = new TestResolver(new FakeSonarrLookup(), radarr, new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["rk1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem { RatingKey = "rk1", IsMovie = true, Title = "Sweet Home Alabama", Year = 2002 };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("couldn't be matched confidently", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_Movie_NoGuid_ZeroTitleMatches_ReturnsNotManagedError()
        {
            var radarr = new FakeRadarrLookup(); // FindMoviesByTitleYear defaults to empty list
            var resolver = new TestResolver(new FakeSonarrLookup(), radarr, new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["rk1"] = new[] { "imdb://tt123" };

            var item = new WatchedItem { RatingKey = "rk1", IsMovie = true, Title = "Sweet Home Alabama", Year = 2002 };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("not managed", result.Error);
        }

        // ---- null file result (nonzero file id, but the *arr file lookup misses) -------------------------------------------------------

        [Fact]
        public async Task ResolveTarget_Episode_NullFileResult_ReturnsNoFileError()
        {
            var series = new SonarrSeriesInfo { Id = 10, SeriesType = "standard", Path = "/data/plexmedia/TV/Show" };
            var sonarr = new FakeSonarrLookup
            {
                FindSeriesByTvdbId = _ => series,
                FindEpisode = (seriesId, season, episode) => new SonarrEpisodeInfo { Id = 55, EpisodeFileId = 99, Monitored = true },
                GetEpisodeFile = _ => null, // nonzero EpisodeFileId, but the file record is missing
            };

            var resolver = new TestResolver(sonarr, new FakeRadarrLookup(), new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["gp1"] = new[] { "tvdb://555" };

            var item = new WatchedItem
            {
                RatingKey = "rk1",
                GrandparentRatingKey = "gp1",
                IsMovie = false,
                Title = "Ep",
                GrandparentTitle = "Show",
                SeasonNumber = 1,
                EpisodeNumber = 2,
            };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("no file on record", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_Movie_NullFileResult_ReturnsNoFileError()
        {
            var movie = new RadarrMovieInfo { Id = 42, MovieFileId = 77, Monitored = true, Title = "Sweet Home Alabama" };
            var radarr = new FakeRadarrLookup
            {
                FindMovieByTmdbId = _ => movie,
                GetMovieFile = _ => null, // nonzero MovieFileId, but the file record is missing
            };

            var resolver = new TestResolver(new FakeSonarrLookup(), radarr, new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["rk1"] = new[] { "tmdb://456" };

            var item = new WatchedItem { RatingKey = "rk1", IsMovie = true, Title = "Sweet Home Alabama", Year = 2002 };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Target);
            Assert.Contains("no file on record", result.Error);
        }

        [Fact]
        public async Task ResolveTarget_Movie_HappyPath()
        {
            var movie = new RadarrMovieInfo { Id = 42, MovieFileId = 77, Monitored = true, Title = "Sweet Home Alabama" };
            var file = new MediaFileInfo
            {
                Id = 77,
                Path = "/data/plexmedia/Movies/Sweet Home Alabama/movie.mkv",
                Quality = "WEBDL-2160p",
                SizeBytes = 3_000_000_000L,
                ReleaseGroup = "EDITH",
            };

            var radarr = new FakeRadarrLookup
            {
                FindMovieByTmdbId = _ => movie,
                GetMovieFile = _ => file,
            };

            var resolver = new TestResolver(new FakeSonarrLookup(), radarr, new RedownloadResolverOptions());
            resolver.GuidsByRatingKey["rk1"] = new[] { "tmdb://456" };

            var item = new WatchedItem { RatingKey = "rk1", IsMovie = true, Title = "Sweet Home Alabama", Year = 2002 };

            var result = await resolver.ResolveTargetAsync(item);

            Assert.Null(result.Error);
            Assert.NotNull(result.Target);
            Assert.True(result.Target.IsMovie);
            Assert.Equal(42, result.Target.ArrId);
            Assert.Equal(0, result.Target.SeriesId);
            Assert.Equal(77, result.Target.FileId);
            Assert.True(result.Target.Monitored);
            Assert.Equal("Sweet Home Alabama (2002)", result.Target.DisplayTitle);
            Assert.Equal(file.Path, result.Target.FilePath);
            Assert.Equal("WEBDL-2160p", result.Target.Quality);
            Assert.Equal(3_000_000_000L, result.Target.SizeBytes);
            Assert.Equal("EDITH", result.Target.ReleaseGroup);
        }
    }
}
