using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Requestrr.WebApi.RequestrrBot.Redownload.Arr;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class RedownloadWorkflowTests : IDisposable
    {
        private readonly string _tempPath;

        public RedownloadWorkflowTests()
        {
            _tempPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        // ---- fakes -------------------------------------------------------

        private class FakeRedownloadMediaClient : IRedownloadMediaClient
        {
            public readonly List<string> Calls = new List<string>();
            public string RecycleBinPath = "/bin";
            public Func<RedownloadTarget, MediaFileInfo> GetCurrentFile = _ => null;
            public long? LastGrabHistoryId = null;

            // Shared with a resolver/history fake so ordering tests can prove the gate check
            // happens strictly before any Tautulli/history call, not just relative to itself.
            public List<string> SharedCallLog;

            public Task<string> GetRecycleBinPathAsync()
            {
                Calls.Add("GetRecycleBinPath");
                SharedCallLog?.Add("GetRecycleBinPath");
                return Task.FromResult(RecycleBinPath);
            }

            public Task<MediaFileInfo> GetCurrentFileAsync(RedownloadTarget t)
            {
                Calls.Add("GetCurrentFile");
                return Task.FromResult(GetCurrentFile(t));
            }

            public Task EnsureMonitoredAsync(RedownloadTarget t)
            {
                Calls.Add("EnsureMonitored");
                return Task.CompletedTask;
            }

            public Task<long?> GetLastGrabHistoryIdAsync(RedownloadTarget t)
            {
                Calls.Add("GetLastGrabHistoryId");
                return Task.FromResult(LastGrabHistoryId);
            }

            public Task DeleteFileAsync(int fileId)
            {
                Calls.Add("DeleteFile");
                return Task.CompletedTask;
            }

            public Task MarkHistoryFailedAsync(long historyId)
            {
                Calls.Add("MarkHistoryFailed");
                return Task.CompletedTask;
            }

            public Task TriggerSearchAsync(RedownloadTarget t)
            {
                Calls.Add("TriggerSearch");
                return Task.CompletedTask;
            }

            public Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName)
            {
                Calls.Add("Restore");
                return Task.FromResult(false);
            }
        }

        private class FakeRedownloadMessenger : IRedownloadMessenger
        {
            public readonly List<(ulong channelId, string content)> Posts = new List<(ulong, string)>();
            public readonly List<(ulong channelId, ulong messageId, string content)> Edits = new List<(ulong, ulong, string)>();
            public ulong NextMessageId = 1;

            public Task<(ulong channelId, ulong messageId)> PostPublicAsync(ulong channelId, string content)
            {
                Posts.Add((channelId, content));
                var messageId = NextMessageId++;
                return Task.FromResult((channelId, messageId));
            }

            public Task EditPublicAsync(ulong channelId, ulong messageId, string content)
            {
                Edits.Add((channelId, messageId, content));
                return Task.CompletedTask;
            }
        }

        private class FakeResponder : IRedownloadEphemeralResponder
        {
            public bool EmptyListShown;
            public IReadOnlyList<WatchedItem> ListItemsShown;
            public int? ListAnimeSkippedShown;
            public bool GateShown;
            public (string title, string error)? ResolveErrorShown;
            public string InFlightTitleShown;
            public RedownloadTarget ConfirmTargetShown;
            public bool StartedShown;
            public bool CancelledShown;
            public bool StaleCardShown;

            public Task ShowEmptyListAsync()
            {
                EmptyListShown = true;
                return Task.CompletedTask;
            }

            public Task ShowListAsync(IReadOnlyList<WatchedItem> items, int animeSkipped)
            {
                ListItemsShown = items;
                ListAnimeSkippedShown = animeSkipped;
                return Task.CompletedTask;
            }

            public Task ShowGateMessageAsync()
            {
                GateShown = true;
                return Task.CompletedTask;
            }

            public Task ShowResolveErrorAsync(string displayTitle, string error)
            {
                ResolveErrorShown = (displayTitle, error);
                return Task.CompletedTask;
            }

            public Task ShowInFlightAsync(string displayTitle)
            {
                InFlightTitleShown = displayTitle;
                return Task.CompletedTask;
            }

            public Task ShowConfirmAsync(RedownloadTarget target)
            {
                ConfirmTargetShown = target;
                return Task.CompletedTask;
            }

            public Task ShowStartedAsync()
            {
                StartedShown = true;
                return Task.CompletedTask;
            }

            public Task ShowCancelledAsync()
            {
                CancelledShown = true;
                return Task.CompletedTask;
            }

            public Task ShowStaleCardAsync()
            {
                StaleCardShown = true;
                return Task.CompletedTask;
            }
        }

        private class FakeSonarrLookup : ISonarrRedownloadLookup
        {
            public Func<int, SonarrSeriesInfo> FindSeriesByTvdbId = _ => null;

            public Task<SonarrSeriesInfo> FindSeriesByTvdbIdAsync(int tvdbId) => Task.FromResult(FindSeriesByTvdbId(tvdbId));
            public Task<SonarrEpisodeInfo> FindEpisodeAsync(int seriesId, int season, int episode) => Task.FromResult<SonarrEpisodeInfo>(null);
            public Task<MediaFileInfo> GetEpisodeFileAsync(int episodeFileId) => Task.FromResult<MediaFileInfo>(null);
            public Task<IReadOnlyList<SonarrSeriesInfo>> FindSeriesByTitleAsync(string title) => Task.FromResult<IReadOnlyList<SonarrSeriesInfo>>(Array.Empty<SonarrSeriesInfo>());
        }

        private class FakeRadarrLookup : IRadarrRedownloadLookup
        {
            public Func<int, RadarrMovieInfo> FindMovieByTmdbId = _ => null;
            public Func<int, MediaFileInfo> GetMovieFile = _ => null;

            public Task<RadarrMovieInfo> FindMovieByTmdbIdAsync(int tmdbId) => Task.FromResult(FindMovieByTmdbId(tmdbId));
            public Task<MediaFileInfo> GetMovieFileAsync(int movieFileId) => Task.FromResult(GetMovieFile(movieFileId));
            public Task<IReadOnlyList<RadarrMovieInfo>> FindMoviesByTitleYearAsync(string title, int year) => Task.FromResult<IReadOnlyList<RadarrMovieInfo>>(Array.Empty<RadarrMovieInfo>());
        }

        // Mirrors RedownloadResolverTests.TestResolver: TautulliClient's two seams are protected
        // virtual specifically so tests never need a real HTTP-backed instance.
        private class TestResolver : RedownloadResolver
        {
            public IReadOnlyList<WatchedItem> History = Array.Empty<WatchedItem>();
            public Dictionary<string, IReadOnlyList<string>> GuidsByRatingKey = new Dictionary<string, IReadOnlyList<string>>();
            public bool HistoryFetched;
            public bool GuidsFetched;
            public List<string> SharedCallLog;

            public TestResolver(ISonarrRedownloadLookup sonarr, IRadarrRedownloadLookup radarr, RedownloadResolverOptions options)
                : base(null, sonarr, radarr, options)
            {
            }

            protected override Task<IReadOnlyList<WatchedItem>> FetchHistoryAsync(int count)
            {
                HistoryFetched = true;
                SharedCallLog?.Add("FetchHistory");
                return Task.FromResult(History);
            }

            protected override Task<IReadOnlyList<string>> FetchGuidsAsync(string ratingKey)
            {
                GuidsFetched = true;
                return Task.FromResult(GuidsByRatingKey.TryGetValue(ratingKey, out var guids) ? guids : Array.Empty<string>());
            }
        }

        // ---- helpers -------------------------------------------------------

        private static RedownloadSettings MakeSettings(bool enabled = true, string animeSnarkUserId = "", string animeSkipMessage = "")
        {
            return new RedownloadSettings
            {
                Enabled = enabled,
                CommandName = "redownload",
                RecentItemsCount = 5,
                HistoryFetchCount = 25,
                TimeoutMinutes = 30,
                AnimeRootFolder = "/data/plexmedia/Anime",
                AnimeSnarkUserId = animeSnarkUserId,
                AnimeSkipMessage = animeSkipMessage,
            };
        }

        private RedownloadWorkflow MakeWorkflow(
            RedownloadSettings settings,
            FakeRedownloadMediaClient sonarrClient,
            FakeRedownloadMediaClient radarrClient,
            RedownloadResolver resolver,
            RedownloadJobsRepository repo,
            RedownloadEngine engine,
            FakeRedownloadMessenger messenger,
            FakeResponder responder,
            ConcurrentDictionary<ulong, PendingRedownload> pending,
            ulong userId = 111UL)
        {
            return new RedownloadWorkflow(userId, "someuser", 222UL, settings, sonarrClient, radarrClient,
                resolver, repo, engine, messenger, responder, pending, NullLogger.Instance);
        }

        // ---- 1: anime snark -------------------------------------------------------

        [Fact]
        public async Task ShowRecentList_SkipsPositive_SnarkUserIdSet_PostsExactPublicMessage()
        {
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr" };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/radarr" };
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var sonarrLookup = new FakeSonarrLookup { FindSeriesByTvdbId = _ => new SonarrSeriesInfo { SeriesType = "anime", Path = "/anime/Foo" } };
            var resolver = new TestResolver(sonarrLookup, new FakeRadarrLookup(), options)
            {
                History = new[]
                {
                    new WatchedItem { RatingKey = "m1", IsMovie = true, Title = "Movie One", Year = 2020 },
                    new WatchedItem { RatingKey = "e1", IsMovie = false, GrandparentRatingKey = "g1", GrandparentTitle = "Some Anime", SeasonNumber = 1, EpisodeNumber = 1 },
                },
                GuidsByRatingKey = new Dictionary<string, IReadOnlyList<string>> { ["g1"] = new[] { "tvdb://123" } },
            };
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var settings = MakeSettings(animeSnarkUserId: "999", animeSkipMessage: "Sorry {user}, skipped {count} anime title(s).");
            var workflow = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, null, messenger, responder, pending);

            await workflow.ShowRecentListAsync();

            Assert.Equal(1, responder.ListAnimeSkippedShown); // sanity: exactly one skip in this fixture
            Assert.Single(messenger.Posts);
            Assert.Equal(222UL, messenger.Posts[0].channelId);
            Assert.Equal("Sorry <@999>, skipped 1 anime title(s).", messenger.Posts[0].content);
        }

        [Fact]
        public async Task ShowRecentList_SkipsPositive_SnarkUserIdBlank_SuppressesPublicPost_ButFootnoteDataStillPresent()
        {
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr" };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/radarr" };
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var sonarrLookup = new FakeSonarrLookup { FindSeriesByTvdbId = _ => new SonarrSeriesInfo { SeriesType = "anime", Path = "/anime/Foo" } };
            var resolver = new TestResolver(sonarrLookup, new FakeRadarrLookup(), options)
            {
                History = new[]
                {
                    new WatchedItem { RatingKey = "m1", IsMovie = true, Title = "Movie One", Year = 2020 },
                    new WatchedItem { RatingKey = "e1", IsMovie = false, GrandparentRatingKey = "g1", GrandparentTitle = "Some Anime", SeasonNumber = 1, EpisodeNumber = 1 },
                },
                GuidsByRatingKey = new Dictionary<string, IReadOnlyList<string>> { ["g1"] = new[] { "tvdb://123" } },
            };
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var settings = MakeSettings(animeSnarkUserId: "", animeSkipMessage: "Sorry {user}, skipped {count} anime title(s).");
            var workflow = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, null, messenger, responder, pending);

            await workflow.ShowRecentListAsync();

            Assert.Empty(messenger.Posts);

            // The ephemeral list's own footnote data must still reflect the real skip count --
            // only the PUBLIC snark post is gated on the snark user id being configured.
            Assert.Equal(1, responder.ListAnimeSkippedShown);
            Assert.Single(responder.ListItemsShown);
        }

        // ---- 2: recycle-bin gate -------------------------------------------------------

        [Fact]
        public async Task ShowRecentList_RecycleBinGate_BlocksBeforeAnyHistoryCall()
        {
            var sharedLog = new List<string>();
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr", SharedCallLog = sharedLog };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "", SharedCallLog = sharedLog }; // unset -> gate
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var resolver = new TestResolver(new FakeSonarrLookup(), new FakeRadarrLookup(), options) { SharedCallLog = sharedLog };
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var settings = MakeSettings();
            var workflow = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, null, messenger, responder, pending);

            await workflow.ShowRecentListAsync();

            Assert.True(responder.GateShown);
            Assert.False(resolver.HistoryFetched);
            Assert.Empty(messenger.Posts);

            // Both adapters were checked (not short-circuited on the first empty result), and
            // neither triggered a history/Tautulli call.
            Assert.Equal(new[] { "GetRecycleBinPath", "GetRecycleBinPath" }, sharedLog);
        }

        // ---- 3: confirm / double-confirm -------------------------------------------------------

        [Fact]
        public async Task HandleConfirm_CallsBeginAsyncExactlyOnce_DoubleConfirmDoesNotCallAgain()
        {
            var client = new FakeRedownloadMediaClient();
            var repo = new RedownloadJobsRepository(_tempPath);
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => client, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));

            var settings = MakeSettings();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var target = new RedownloadTarget
            {
                IsMovie = false,
                ArrId = 42,
                SeriesId = 7,
                FileId = 100,
                DisplayTitle = "Some Show - S01E01",
                Quality = "WEBDL-1080p",
                SizeBytes = 1_000_000_000L,
                ReleaseGroup = "GRP",
            };
            pending[111UL] = new PendingRedownload { Target = target };

            var responder = new FakeResponder();
            var workflowMessenger = new FakeRedownloadMessenger();
            var workflow1 = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder, pending);

            await workflow1.HandleConfirmAsync(target.ArrId);

            Assert.Single(engineMessenger.Posts); // BeginAsync's own public post, exactly once
            Assert.True(responder.StartedShown);
            Assert.False(pending.ContainsKey(111UL));
            Assert.Single(repo.GetActive());

            // A fresh workflow instance for the same user (mirrors a new interaction/component
            // event routed through the factory) -- but Pending was already cleared by the first
            // confirm, so this must be a silent no-op, not a second BeginAsync.
            var responder2 = new FakeResponder();
            var workflow2 = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder2, pending);

            await workflow2.HandleConfirmAsync(target.ArrId);

            Assert.Single(engineMessenger.Posts); // still just once
            Assert.Single(repo.GetActive());
            Assert.False(responder2.StartedShown);
        }

        [Fact]
        public async Task HandleConfirm_NoPendingEntry_SilentNoOp_NoThrow()
        {
            var client = new FakeRedownloadMediaClient();
            var repo = new RedownloadJobsRepository(_tempPath);
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => client, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));

            var settings = MakeSettings();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>(); // nothing seeded for 111UL
            var responder = new FakeResponder();
            var workflowMessenger = new FakeRedownloadMessenger();
            var workflow = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder, pending);

            await workflow.HandleConfirmAsync(42);

            Assert.Empty(engineMessenger.Posts);
            Assert.False(responder.StartedShown);
            Assert.False(responder.StaleCardShown);
        }

        [Fact]
        public async Task HandleConfirm_ArrIdMismatch_StaleCard_DoesNotCallBeginAsync()
        {
            // Simulates: user opened a confirm card for item A (ArrId 42), then made a NEW
            // selection (item B, ArrId 99) which overwrote pending.Target -- then clicked the
            // OLD card for A. The button's own arrId (42) no longer matches pending.Target.ArrId
            // (99), so this must be treated as stale, not actioned against B.
            var client = new FakeRedownloadMediaClient();
            var repo = new RedownloadJobsRepository(_tempPath);
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => client, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));

            var settings = MakeSettings();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var currentTarget = new RedownloadTarget { IsMovie = false, ArrId = 99, DisplayTitle = "Item B" };
            pending[111UL] = new PendingRedownload { Target = currentTarget };

            var responder = new FakeResponder();
            var workflowMessenger = new FakeRedownloadMessenger();
            var workflow = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder, pending);

            await workflow.HandleConfirmAsync(42); // the OLD card's arrId, not the current target's

            Assert.True(responder.StaleCardShown);
            Assert.False(responder.StartedShown);
            Assert.Empty(engineMessenger.Posts);

            // The (still valid) pending target for B is untouched -- clicking the CURRENT card
            // should still work.
            Assert.True(pending.ContainsKey(111UL));
            Assert.Same(currentTarget, pending[111UL].Target);

            await workflow.HandleConfirmAsync(99); // the current card's arrId -- matches

            Assert.True(responder.StartedShown);
            Assert.Single(engineMessenger.Posts);
            Assert.False(pending.ContainsKey(111UL));
        }

        [Fact]
        public async Task HandleConfirm_Disabled_DoesNotCallBeginAsync()
        {
            var client = new FakeRedownloadMediaClient();
            var repo = new RedownloadJobsRepository(_tempPath);
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => client, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));

            var settings = MakeSettings(enabled: false);
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var target = new RedownloadTarget { IsMovie = false, ArrId = 42, DisplayTitle = "Some Show - S01E01" };
            pending[111UL] = new PendingRedownload { Target = target };

            var responder = new FakeResponder();
            var workflowMessenger = new FakeRedownloadMessenger();
            var workflow = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder, pending);

            await workflow.HandleConfirmAsync(42);

            Assert.Empty(engineMessenger.Posts); // BeginAsync never called: the kill switch disarms an open card
            Assert.False(responder.StartedShown);
            Assert.False(responder.StaleCardShown);

            // An admin flipping Enabled off mid-flow shouldn't destroy the user's pending
            // selection either -- just refuse to act on it until re-enabled.
            Assert.True(pending.ContainsKey(111UL));
        }

        [Fact]
        public async Task HandleSelection_Disabled_DoesNotResolveOrShowConfirm()
        {
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr" };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/radarr" };
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var resolver = new TestResolver(new FakeSonarrLookup(), new FakeRadarrLookup(), options);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            pending[111UL] = new PendingRedownload
            {
                Offered = new[] { new WatchedItem { RatingKey = "m1", IsMovie = true, Title = "Movie One", Year = 2020 } },
            };
            var settings = MakeSettings(enabled: false);
            var workflow = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, null, messenger, responder, pending);

            await workflow.HandleSelectionAsync(0);

            Assert.False(resolver.GuidsFetched); // ResolveTargetAsync never ran either
            Assert.Null(responder.ConfirmTargetShown);
            Assert.Null(responder.ResolveErrorShown);
        }

        [Fact]
        public async Task HandleConfirm_RaceAlreadyActive_ShowsInFlight_DoesNotCallBeginAsync_ClearsPending()
        {
            var target = new RedownloadTarget
            {
                IsMovie = false,
                ArrId = 42,
                SeriesId = 7,
                FileId = 100,
                DisplayTitle = "Some Show - S01E01",
                Quality = "WEBDL-1080p",
                SizeBytes = 1_000_000_000L,
                ReleaseGroup = "GRP",
            };

            var repo = new RedownloadJobsRepository(_tempPath);
            repo.Add(new RedownloadJob
            {
                Id = Guid.NewGuid(),
                IsMovie = false,
                ArrId = 42,
                State = RedownloadJobState.Searching,
                DisplayTitle = "Some Show - S01E01",
                CreatedUtc = DateTime.UtcNow,
                DeadlineUtc = DateTime.UtcNow.AddMinutes(30),
            });

            var client = new FakeRedownloadMediaClient();
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => client, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));

            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            pending[111UL] = new PendingRedownload { Target = target };

            var responder = new FakeResponder();
            var workflowMessenger = new FakeRedownloadMessenger();
            var settings = MakeSettings();
            var workflow = MakeWorkflow(settings, client, client, null, repo, engine, workflowMessenger, responder, pending);

            await workflow.HandleConfirmAsync(target.ArrId);

            Assert.Equal("Some Show - S01E01", responder.InFlightTitleShown);
            Assert.False(responder.StartedShown);
            Assert.Empty(engineMessenger.Posts); // BeginAsync (and its public post) never ran
            Assert.False(pending.ContainsKey(111UL));
        }

        // ---- Pending write/read handoff across separate workflow instances -------------------------------------------------------

        [Fact]
        public async Task FullChain_ThreeSeparateWorkflowInstances_ShareOnePendingDictionary()
        {
            // Mirrors production: ShowRecentListAsync, HandleSelectionAsync, and
            // HandleConfirmAsync are each invoked on a FRESH RedownloadWorkflow instance (one
            // per Discord interaction, built by the factory) -- they only agree on state because
            // the factory hands every one of them the SAME Pending dictionary and the SAME
            // RedownloadJobsRepository. A bug that keyed Pending on the wrong id, or that failed
            // to write the resolved Target back into the stored PendingRedownload, would pass
            // every other test in this file (they all reuse one workflow's state directly) but
            // silently no-op /redownload in production.
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr" };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/radarr" };
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var radarrLookup = new FakeRadarrLookup
            {
                FindMovieByTmdbId = id => id == 55 ? new RadarrMovieInfo { Id = 5, MovieFileId = 77, Monitored = true, Title = "Movie One" } : null,
                GetMovieFile = id => id == 77
                    ? new MediaFileInfo { Id = 77, Path = "/data/plexmedia/Movies/Movie One (2020)/Movie One.mkv", Quality = "WEBDL-1080p", SizeBytes = 2_000_000_000L, ReleaseGroup = "GRP" }
                    : null,
            };
            var resolver = new TestResolver(new FakeSonarrLookup(), radarrLookup, options)
            {
                History = new[] { new WatchedItem { RatingKey = "m1", IsMovie = true, Title = "Movie One", Year = 2020 } },
                GuidsByRatingKey = new Dictionary<string, IReadOnlyList<string>> { ["m1"] = new[] { "tmdb://55" } },
            };
            var repo = new RedownloadJobsRepository(_tempPath);
            var engineMessenger = new FakeRedownloadMessenger();
            var engine = new RedownloadEngine(_ => sonarrClient, repo, engineMessenger, NullLogger.Instance, TimeSpan.FromMinutes(5));
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var settings = MakeSettings();

            var responder1 = new FakeResponder();
            var workflow1 = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, engine, new FakeRedownloadMessenger(), responder1, pending);
            await workflow1.ShowRecentListAsync();

            Assert.Single(responder1.ListItemsShown);
            Assert.True(pending.ContainsKey(111UL));
            Assert.Single(pending[111UL].Offered);

            var responder2 = new FakeResponder();
            var workflow2 = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, engine, new FakeRedownloadMessenger(), responder2, pending);
            await workflow2.HandleSelectionAsync(0);

            Assert.NotNull(responder2.ConfirmTargetShown);
            Assert.Equal("Movie One (2020)", responder2.ConfirmTargetShown.DisplayTitle);
            Assert.NotNull(pending[111UL].Target); // written back into the SAME stored object

            var responder3 = new FakeResponder();
            var workflow3 = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, engine, new FakeRedownloadMessenger(), responder3, pending);
            await workflow3.HandleConfirmAsync(responder2.ConfirmTargetShown.ArrId);

            Assert.True(responder3.StartedShown);
            Assert.Single(engineMessenger.Posts);
            Assert.Contains("Movie One (2020)", engineMessenger.Posts[0].content);
            Assert.False(pending.ContainsKey(111UL));
        }

        // ---- 4: cancel -------------------------------------------------------

        [Fact]
        public async Task HandleCancel_ClearsPendingState()
        {
            var client = new FakeRedownloadMediaClient();
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            pending[111UL] = new PendingRedownload
            {
                Offered = new[] { new WatchedItem { RatingKey = "m1", IsMovie = true, Title = "Movie One", Year = 2020 } },
                Target = new RedownloadTarget { IsMovie = true, ArrId = 1, DisplayTitle = "Movie One (2020)" },
            };
            var settings = MakeSettings();
            var workflow = MakeWorkflow(settings, client, client, null, repo, null, messenger, responder, pending);

            await workflow.HandleCancelAsync();

            Assert.False(pending.ContainsKey(111UL));
        }

        // ---- 5: clientSelector -------------------------------------------------------

        [Fact]
        public void RedownloadWorkflowFactory_ClientSelector_ReturnsRadarrForMovie_SonarrForEpisode()
        {
            var sonarrClient = new SonarrRedownloadClient(null, NullLogger<SonarrRedownloadClient>.Instance, null);
            var radarrClient = new RadarrRedownloadClient(null, NullLogger<RadarrRedownloadClient>.Instance, null);
            var repo = new RedownloadJobsRepository(_tempPath);
            var factory = new RedownloadWorkflowFactory(null, null, sonarrClient, radarrClient, repo, NullLogger<RedownloadWorkflow>.Instance);

            Assert.Same(radarrClient, factory.ClientSelector(true));
            Assert.Same(sonarrClient, factory.ClientSelector(false));
        }

        // ---- 6: disabled -------------------------------------------------------

        [Fact]
        public async Task ShowRecentList_Disabled_DoesNothing_NoGateCall_NoHistoryCall()
        {
            var sonarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/sonarr" };
            var radarrClient = new FakeRedownloadMediaClient { RecycleBinPath = "/bin/radarr" };
            var options = new RedownloadResolverOptions { RecentItemsCount = 5, HistoryFetchCount = 25, AnimeRootFolder = "/anime" };
            var resolver = new TestResolver(new FakeSonarrLookup(), new FakeRadarrLookup(), options);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var responder = new FakeResponder();
            var pending = new ConcurrentDictionary<ulong, PendingRedownload>();
            var settings = MakeSettings(enabled: false);
            var workflow = MakeWorkflow(settings, sonarrClient, radarrClient, resolver, repo, null, messenger, responder, pending);

            await workflow.ShowRecentListAsync();

            Assert.Empty(sonarrClient.Calls);
            Assert.Empty(radarrClient.Calls);
            Assert.False(resolver.HistoryFetched);
            Assert.False(responder.GateShown);
            Assert.Null(responder.ListAnimeSkippedShown);
            Assert.False(responder.EmptyListShown);
            Assert.Empty(messenger.Posts);
        }
    }
}
