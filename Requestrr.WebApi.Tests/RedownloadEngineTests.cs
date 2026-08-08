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
                return Task.CompletedTask;
            }

            public Task MarkHistoryFailedAsync(long historyId)
            {
                Calls.Add("MarkHistoryFailed");
                BlocklistedHistoryIds.Add(historyId);
                if (MarkHistoryFailedThrows != null)
                    throw MarkHistoryFailedThrows;
                return Task.CompletedTask;
            }

            public Task TriggerSearchAsync(RedownloadTarget t)
            {
                Calls.Add("TriggerSearch");
                SearchTriggeredFor.Add(t);
                if (TriggerSearchThrows != null)
                    throw TriggerSearchThrows;
                return Task.CompletedTask;
            }

            public Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName)
            {
                Calls.Add("Restore");
                RestoreCallCount++;
                RestoreCalledWithTarget = t;
                RestoreCalledWithFileName = originalFileName;
                if (RestoreThrows != null)
                    throw RestoreThrows;
                return Task.FromResult(RestoreResult);
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

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // the deleted file must never be silently orphaned: the job is persisted anyway,
            // and the search still runs despite the blocklist failure.
            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
            Assert.Contains(100, client.DeletedFileIds);
            Assert.Contains("TriggerSearch", client.Calls);
            Assert.NotEmpty(logger.Errors);
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

            var job = await engine.BeginAsync(target, 111UL, "someuser", 222UL, 30);

            // the deleted file must never be silently orphaned: the job is persisted anyway
            Assert.Equal(RedownloadJobState.Searching, job.State);
            Assert.True(repo.HasActiveFor(target.IsMovie, target.ArrId));
            Assert.Contains(100, client.DeletedFileIds);
            Assert.NotEmpty(logger.Errors);
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

        // ---- Start / StopAsync -------------------------------------------------------

        [Fact]
        public async Task Start_ResumesPersistedSearchingJobs()
        {
            var job = new RedownloadJob
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
                DeadlineUtc = DateTime.UtcNow.AddMinutes(30),
                State = RedownloadJobState.Searching
            };

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
    }
}
