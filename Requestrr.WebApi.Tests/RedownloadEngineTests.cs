using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class RedownloadEngineTests : IDisposable
    {
        private readonly string _tempPath;

        public RedownloadEngineTests()
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
            // Shared with FakeRedownloadMessenger (via SharedCallLog) so ordering tests can
            // assert the public post lands in the exact right slot relative to delete/blocklist/
            // search, not just relative to other client calls.
            public List<string> Calls = new List<string>();

            public string RecycleBinPath = "/bin";
            public Func<RedownloadTarget, MediaFileInfo> GetCurrentFile = _ => null;
            public long? LastGrabHistoryId = 555;
            public bool RestoreResult = true;
            public Exception RestoreThrows;
            public Exception TriggerSearchThrows;
            public Exception MarkHistoryFailedThrows;
            public Exception DeleteFileThrows;

            // Invoked synchronously, right as the corresponding call happens, so a test can
            // inspect repository state exactly "during" that call (all fakes here are
            // synchronous, so this reflects real happens-before ordering relative to whatever
            // BeginAsync already did, e.g. the pre-delete persist).
            public Action BeforeDeleteFile;
            public Action BeforeMarkHistoryFailed;
            public Action BeforeTriggerSearch;

            // When set, RestoreFromRecycleBinAsync suspends on this until it's completed --
            // used to force two CheckJobAsync calls to genuinely race past the state-gate before
            // either has persisted a state change, exercising the _inFlightRestores claim itself.
            public TaskCompletionSource<bool> RestoreGate;

            public readonly List<int> DeletedFileIds = new List<int>();
            public readonly List<long> BlocklistedHistoryIds = new List<long>();
            public readonly List<RedownloadTarget> SearchTriggeredFor = new List<RedownloadTarget>();
            public int RestoreCallCount;
            public RedownloadTarget RestoreCalledWithTarget;
            public string RestoreCalledWithFileName;

            public Task<string> GetRecycleBinPathAsync()
            {
                Calls.Add("GetRecycleBinPath");
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
                DeletedFileIds.Add(fileId);
                BeforeDeleteFile?.Invoke();
                if (DeleteFileThrows != null)
                    throw DeleteFileThrows;
                return Task.CompletedTask;
            }

            public Task MarkHistoryFailedAsync(long historyId)
            {
                Calls.Add("MarkHistoryFailed");
                BlocklistedHistoryIds.Add(historyId);
                BeforeMarkHistoryFailed?.Invoke();
                if (MarkHistoryFailedThrows != null)
                    throw MarkHistoryFailedThrows;
                return Task.CompletedTask;
            }

            public Task TriggerSearchAsync(RedownloadTarget t)
            {
                Calls.Add("TriggerSearch");
                SearchTriggeredFor.Add(t);
                BeforeTriggerSearch?.Invoke();
                if (TriggerSearchThrows != null)
                    throw TriggerSearchThrows;
                return Task.CompletedTask;
            }

            public async Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName)
            {
                Calls.Add("Restore");
                RestoreCallCount++;
                RestoreCalledWithTarget = t;
                RestoreCalledWithFileName = originalFileName;

                if (RestoreGate != null)
                    await RestoreGate.Task;

                if (RestoreThrows != null)
                    throw RestoreThrows;

                return RestoreResult;
            }
        }

        private class FakeRedownloadMessenger : IRedownloadMessenger
        {
            public readonly List<(ulong channelId, string content)> Posts = new List<(ulong, string)>();
            public readonly List<(ulong channelId, ulong messageId, string content)> Edits = new List<(ulong, ulong, string)>();
            public ulong NextMessageId = 1;

            // Set to a client's Calls list to interleave "PostPublic" into the same ordered
            // log as the client's delete/blocklist/search calls.
            public List<string> SharedCallLog;

            public Task<(ulong channelId, ulong messageId)> PostPublicAsync(ulong channelId, string content)
            {
                SharedCallLog?.Add("PostPublic");
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

        private class FakeLogger : ILogger
        {
            public readonly List<string> Errors = new List<string>();

            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
            {
                if (logLevel >= LogLevel.Error)
                    Errors.Add(formatter(state, exception));
            }
        }

        // ---- helpers -------------------------------------------------------

        private static RedownloadTarget MakeTarget(bool isMovie = false)
        {
            return new RedownloadTarget
            {
                IsMovie = isMovie,
                ArrId = 10,
                SeriesId = isMovie ? 0 : 5,
                FileId = 100,
                Monitored = true,
                DisplayTitle = "Some Show - S01E01",
                FilePath = "/data/plexmedia/TV/Some Show/Season 01/Some.Show.S01E01.mkv",
                Quality = "WEBDL-1080p",
                SizeBytes = 1_500_000_000L,
                ReleaseGroup = "GROUP"
            };
        }

        private static MediaFileInfo MakeFile(int id, string path = "/data/plexmedia/TV/Some Show/Season 01/Some.Show.S01E01.mkv",
                                               string quality = "WEBDL-1080p", long sizeBytes = 1_500_000_000L, string releaseGroup = "GROUP")
        {
            return new MediaFileInfo { Id = id, Path = path, Quality = quality, SizeBytes = sizeBytes, ReleaseGroup = releaseGroup };
        }

        private RedownloadEngine MakeEngine(FakeRedownloadMediaClient client, RedownloadJobsRepository repo, FakeRedownloadMessenger messenger,
                                             FakeLogger logger, TimeSpan? pollInterval = null, Func<DateTime> utcNow = null)
        {
            return new RedownloadEngine(_ => client, repo, messenger, logger,
                pollInterval ?? TimeSpan.FromMilliseconds(50), utcNow ?? (() => DateTime.UtcNow));
        }

        private static RedownloadJob MakeSeedJob(DateTime deadlineUtc)
        {
            return new RedownloadJob
            {
                Id = Guid.NewGuid(),
                IsMovie = false,
                ArrId = 10,
                SeriesId = 5,
                OldFileId = 100,
                DisplayTitle = "Some Show - S01E01",
                OldFilePath = "/data/plexmedia/TV/Some Show/Season 01/Some.Show.S01E01.mkv",
                OldFileName = "Some.Show.S01E01.mkv",
                OldQuality = "WEBDL-1080p",
                OldSizeBytes = 1_500_000_000L,
                OldReleaseGroup = "GROUP",
                BlocklistedHistoryId = 777,
                ChannelId = 222UL,
                MessageId = 333UL,
                RequestedByUserId = 111UL,
                RequestedByUsername = "someuser",
                CreatedUtc = DateTime.UtcNow,
                DeadlineUtc = deadlineUtc,
                State = RedownloadJobState.Searching
            };
        }

        // ---- BeginAsync -------------------------------------------------------

        [Fact]
        public async Task BeginAsync_CallsInOrder_DeleteBeforeBlocklistBeforeSearch()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger { SharedCallLog = client.Calls };
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // Full order, spec Section 3, BINDING: capture current file -> ensure monitored ->
            // find grab history id -> post public message -> DELETE -> blocklist -> search.
            Assert.Equal(
                new[] { "GetCurrentFile", "EnsureMonitored", "GetLastGrabHistoryId", "PostPublic", "DeleteFile", "MarkHistoryFailed", "TriggerSearch" },
                client.Calls);

            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
        }

        [Fact]
        public async Task BeginAsync_HasGrabHistory_PostsExactMessage_WithBlocklistedFlag()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            Assert.Single(messenger.Posts);
            Assert.Equal(
                "🔁 **someuser** is replacing **Some Show - S01E01**\n" +
                "deleted WEBDL-1080p · 1.5 GB · GROUP — blocklisted — searching…",
                messenger.Posts[0].content);
        }

        [Fact]
        public async Task BeginAsync_NoGrabHistory_SkipsBlocklist_AndSaysSo()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = null };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            Assert.DoesNotContain("MarkHistoryFailed", client.Calls);
            Assert.Single(messenger.Posts);
            Assert.Equal(
                "🔁 **someuser** is replacing **Some Show - S01E01**\n" +
                "deleted WEBDL-1080p · 1.5 GB · GROUP — ⚠️ couldn't blocklist (no grab history) — the same release may return — searching…",
                messenger.Posts[0].content);
        }

        [Fact]
        public async Task BeginAsync_BlocklistThrows_JobStillPersisted()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            client.MarkHistoryFailedThrows = new InvalidOperationException("blocklist POST failed");
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            RedownloadJob persistedDuringFailure = null;
            client.BeforeMarkHistoryFailed = () => persistedDuringFailure = repo.GetActive().SingleOrDefault();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // Persisted BEFORE the delete (and therefore already there the instant the
            // blocklist call fails) -- proven by inspecting the repo from inside the throwing
            // call itself, not just after BeginAsync returns.
            Assert.NotNull(persistedDuringFailure);
            Assert.Equal(RedownloadJobState.Searching, persistedDuringFailure.State);

            // the deleted file must never be silently orphaned: the job is persisted anyway,
            // and the search still runs despite the blocklist failure.
            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
            Assert.Contains(100, client.DeletedFileIds);
            Assert.Contains("TriggerSearch", client.Calls);
            Assert.NotEmpty(logger.Errors);

            // The message must stop claiming "blocklisted" once the blocklist call actually
            // failed -- BlocklistedHistoryId is corrected to null so every later rewrite tells
            // the truth instead of repeating a claim that never happened.
            Assert.Null(job.BlocklistedHistoryId);

            client.GetCurrentFile = _ => MakeFile(200, quality: "WEBDL-2160p", sizeBytes: 5_000_000_000L, releaseGroup: "NEWGROUP");
            await engine.CheckJobAsync(job);

            Assert.Single(messenger.Edits);
            Assert.Contains("couldn't blocklist", messenger.Edits[0].content);
        }

        [Fact]
        public async Task BeginAsync_SearchThrows_JobStillPersisted()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            client.TriggerSearchThrows = new InvalidOperationException("search POST failed");
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            RedownloadJob persistedDuringFailure = null;
            client.BeforeTriggerSearch = () => persistedDuringFailure = repo.GetActive().SingleOrDefault();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // Persisted BEFORE the delete -- already there the instant the search call fails.
            Assert.NotNull(persistedDuringFailure);
            Assert.Equal(RedownloadJobState.Searching, persistedDuringFailure.State);

            // the deleted file must never be silently orphaned: the job is persisted anyway
            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
            Assert.Contains(100, client.DeletedFileIds);
            Assert.NotEmpty(logger.Errors);
        }

        [Fact]
        public async Task BeginAsync_DeleteThrows_JobStillPersisted()
        {
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            client.DeleteFileThrows = new InvalidOperationException("delete timed out");
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // A delete-timeout that throws must not stop the blocklist/search steps from
            // running, nor stop the job from persisting -- worst case the deadline eventually
            // passes and a restore attempt finds nothing to import, landing on RestoreFailed
            // rather than losing the job's record entirely.
            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
            Assert.Contains("MarkHistoryFailed", client.Calls);
            Assert.Contains("TriggerSearch", client.Calls);
            Assert.NotEmpty(logger.Errors);
        }

        [Fact]
        public async Task BeginAsync_PersistsJobBeforeDelete_WithCorrectDeadline()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            RedownloadJob persistedDuringDelete = null;
            client.BeforeDeleteFile = () =>
            {
                Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
                persistedDuringDelete = repo.GetActive().Single();
            };

            await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // The job -- including its deadline -- must exist in the repository BEFORE the
            // delete call runs, and DeadlineUtc must already be the real computed value (not a
            // default DateTime, which would make the poll loop's very first tick see this job
            // as already past-deadline and try to restore it mid-Begin).
            Assert.NotNull(persistedDuringDelete);
            Assert.Equal(RedownloadJobState.Searching, persistedDuringDelete.State);
            Assert.Equal(now.AddMinutes(30), persistedDuringDelete.DeadlineUtc);
        }

        // ---- CheckJobAsync -------------------------------------------------------

        [Fact]
        public async Task CheckJob_NewFileArrived_EditsSuccess_CompletesJob()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            client.GetCurrentFile = _ => MakeFile(200, quality: "WEBDL-2160p", sizeBytes: 5_000_000_000L, releaseGroup: "NEWGROUP");
            now = now.AddMinutes(17);

            await engine.CheckJobAsync(job);

            Assert.Equal(RedownloadJobState.Completed, job.State);
            Assert.Single(messenger.Edits);
            var content = messenger.Edits[0].content;
            Assert.Equal(
                "🔁 **someuser** is replacing **Some Show - S01E01**\n" +
                "deleted WEBDL-1080p · 1.5 GB · GROUP — blocklisted — searching…\n" +
                "✅ Replaced with WEBDL-2160p · 5.0 GB · NEWGROUP · 17m",
                content);

            var reloaded = new RedownloadJobsRepository(_tempPath);
            Assert.False(reloaded.HasActiveFor(target.IsMovie, target.ArrId));
        }

        [Fact]
        public async Task CheckJob_PastDeadline_RestoresExactlyOnce()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100); // no new file ever arrives
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            now = job.DeadlineUtc.AddMinutes(1); // advance the injected clock past the deadline

            await engine.CheckJobAsync(job);

            Assert.Equal(1, client.RestoreCallCount);
            Assert.Equal(RedownloadJobState.Restored, job.State);
            Assert.Single(messenger.Edits);
            Assert.Contains("↩️ Nothing better found — original restored", messenger.Edits[0].content);

            // A second call must not attempt a second restore, nor a second edit -- the
            // state-gate at the top of CheckJobAsync short-circuits on the already-Restored job.
            await engine.CheckJobAsync(job);

            Assert.Equal(1, client.RestoreCallCount);
            Assert.Single(messenger.Edits);
        }

        [Fact]
        public async Task CheckJob_RestoreFails_MarksRestoreFailed_WarnsAdmin()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777, RestoreResult = false };
            client.GetCurrentFile = _ => MakeFile(100);
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            now = job.DeadlineUtc.AddMinutes(1);

            await engine.CheckJobAsync(job);

            Assert.Equal(RedownloadJobState.RestoreFailed, job.State);
            Assert.Single(messenger.Edits);
            Assert.Contains("⚠️ No replacement found and restoring the original failed — an admin needs to check the recycle bin.", messenger.Edits[0].content);
        }

        [Fact]
        public async Task CheckJob_RestoreThrows_MarksRestoreFailed_WarnsAdmin()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100);
            client.RestoreThrows = new InvalidOperationException("recycle bin scan failed");
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            now = job.DeadlineUtc.AddMinutes(1);

            await engine.CheckJobAsync(job);

            Assert.Equal(RedownloadJobState.RestoreFailed, job.State);
            Assert.Single(messenger.Edits);
            Assert.Contains("⚠️ No replacement found and restoring the original failed — an admin needs to check the recycle bin.", messenger.Edits[0].content);
            Assert.NotEmpty(logger.Errors);

            // Restore must still run at most once even when it threw.
            await engine.CheckJobAsync(job);
            Assert.Equal(1, client.RestoreCallCount);
        }

        [Fact]
        public async Task CheckJob_ConcurrentCallsPastDeadline_RestoresExactlyOnce()
        {
            var now = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc);
            var client = new FakeRedownloadMediaClient { LastGrabHistoryId = 777 };
            client.GetCurrentFile = _ => MakeFile(100); // no new file ever arrives
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, utcNow: () => now);
            var target = MakeTarget();

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);
            now = job.DeadlineUtc.AddMinutes(1);

            client.RestoreGate = new TaskCompletionSource<bool>();

            // Neither call is awaited before starting the other: each fake call up through
            // GetCurrentFileAsync resolves synchronously, so both calls reach the deadline check
            // and the restore-claim before either has persisted a state change -- job.State is
            // STILL Searching for both at that point. The first to reach the lock claims
            // _inFlightRestores and suspends inside RestoreFromRecycleBinAsync (awaiting the
            // still-incomplete RestoreGate); the second finds the claim already taken and
            // returns immediately WITHOUT ever calling RestoreFromRecycleBinAsync. This proves
            // the HashSet guard itself is doing the work, not just the (racy) top-of-method
            // State check.
            var taskA = engine.CheckJobAsync(job);
            var taskB = engine.CheckJobAsync(job);

            Assert.Equal(1, client.RestoreCallCount);
            Assert.False(taskA.IsCompleted);
            Assert.True(taskB.IsCompleted);

            client.RestoreGate.SetResult(true);
            await Task.WhenAll(taskA, taskB);

            Assert.Equal(1, client.RestoreCallCount);
            Assert.Equal(RedownloadJobState.Restored, job.State);
            Assert.Single(messenger.Edits);
        }

        // ---- Start / StopAsync -------------------------------------------------------

        [Fact]
        public async Task Start_ResumesPersistedSearchingJobs()
        {
            var job = MakeSeedJob(DateTime.UtcNow.AddMinutes(30));

            var seedRepo = new RedownloadJobsRepository(_tempPath);
            seedRepo.Add(job);

            var client = new FakeRedownloadMediaClient();
            client.GetCurrentFile = _ => MakeFile(200, quality: "WEBDL-2160p", sizeBytes: 5_000_000_000L, releaseGroup: "NEWGROUP");
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, pollInterval: TimeSpan.FromMilliseconds(50));

            engine.Start();
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline && !messenger.Edits.Any())
                {
                    await Task.Delay(25);
                }
            }
            finally
            {
                await engine.StopAsync();
            }

            Assert.Single(messenger.Edits);
            Assert.Contains("✅ Replaced with", messenger.Edits[0].content);

            var reloaded = new RedownloadJobsRepository(_tempPath);
            Assert.False(reloaded.HasActiveFor(false, 10));
        }

        [Fact]
        public async Task StopAsync_StopsPollingLoop()
        {
            var client = new FakeRedownloadMediaClient();
            client.GetCurrentFile = _ => null;
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, pollInterval: TimeSpan.FromMilliseconds(20));

            engine.Start();
            await Task.Delay(100);
            await engine.StopAsync();

            var callCountAtStop = client.Calls.Count;
            await Task.Delay(150);

            Assert.Equal(callCountAtStop, client.Calls.Count);
        }

        [Fact]
        public async Task Start_CalledTwice_DoesNotRunASecondPollingLoop()
        {
            // A job that never resolves (current file always equals OldFileId, deadline far in
            // the future) so it stays Searching and gets re-checked on every poll tick for the
            // duration of the test -- letting us count ticks via GetCurrentFile call volume.
            var job = MakeSeedJob(DateTime.UtcNow.AddDays(1));
            var seedRepo = new RedownloadJobsRepository(_tempPath);
            seedRepo.Add(job);

            var client = new FakeRedownloadMediaClient();
            client.GetCurrentFile = _ => MakeFile(100); // same as OldFileId -> never "succeeds"
            var repo = new RedownloadJobsRepository(_tempPath);
            var messenger = new FakeRedownloadMessenger();
            var logger = new FakeLogger();
            var engine = MakeEngine(client, repo, messenger, logger, pollInterval: TimeSpan.FromMilliseconds(20));

            engine.Start();
            engine.Start(); // must be a no-op: a loop is already running

            await Task.Delay(220); // ~11 ticks for a single loop
            await engine.StopAsync();

            var getCurrentFileCalls = client.Calls.Count(c => c == "GetCurrentFile");

            // A single loop ticking every 20ms for ~220ms produces roughly 10-12 calls; a
            // doubled loop would produce roughly twice that. Generous bound to absorb scheduler
            // jitter while still clearly separating single- from double-loop behavior.
            Assert.InRange(getCurrentFileCalls, 1, 16);
        }
    }
}
