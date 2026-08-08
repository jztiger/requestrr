using System;
using System.IO;
using System.Linq;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class RedownloadJobsRepositoryTests : IDisposable
    {
        private readonly string _tempPath;

        public RedownloadJobsRepositoryTests()
        {
            _tempPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        private static RedownloadJob MakeJob(Guid? id = null, bool isMovie = false, int arrId = 42, RedownloadJobState state = RedownloadJobState.Searching)
        {
            return new RedownloadJob
            {
                Id = id ?? Guid.NewGuid(),
                IsMovie = isMovie,
                ArrId = arrId,
                SeriesId = 7,
                OldFileId = 123,
                DisplayTitle = "Some Show - S01E01",
                OldFilePath = "/data/plexmedia/TV/Some Show/Season 01/Some.Show.S01E01.mkv",
                OldFileName = "Some.Show.S01E01.mkv",
                OldQuality = "WEBDL-1080p",
                OldSizeBytes = 1234567890L,
                OldReleaseGroup = "GROUP",
                BlocklistedHistoryId = 999,
                // Realistic Discord snowflake magnitudes (Newtonsoft roundtrips these fine;
                // see note in the roundtrip test below re: ulong.MaxValue).
                ChannelId = 945523629792309308UL,
                MessageId = 945523629792309309UL,
                RequestedByUserId = 945523629792309310UL,
                RequestedByUsername = "someuser",
                CreatedUtc = new DateTime(2026, 8, 8, 12, 0, 0, DateTimeKind.Utc),
                DeadlineUtc = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc),
                State = state
            };
        }

        [Fact]
        public void Add_ThenNewRepositoryInstance_ReloadsJobFromDisk()
        {
            var repo = new RedownloadJobsRepository(_tempPath);
            var job = MakeJob();

            repo.Add(job);

            var reloaded = new RedownloadJobsRepository(_tempPath);
            var active = reloaded.GetActive();

            Assert.Single(active);
            var roundtripped = active[0];
            Assert.Equal(job.Id, roundtripped.Id);
            Assert.Equal(job.IsMovie, roundtripped.IsMovie);
            Assert.Equal(job.ArrId, roundtripped.ArrId);
            Assert.Equal(job.SeriesId, roundtripped.SeriesId);
            Assert.Equal(job.OldFileId, roundtripped.OldFileId);
            Assert.Equal(job.DisplayTitle, roundtripped.DisplayTitle);
            Assert.Equal(job.OldFilePath, roundtripped.OldFilePath);
            Assert.Equal(job.OldFileName, roundtripped.OldFileName);
            Assert.Equal(job.OldQuality, roundtripped.OldQuality);
            Assert.Equal(job.OldSizeBytes, roundtripped.OldSizeBytes);
            Assert.Equal(job.OldReleaseGroup, roundtripped.OldReleaseGroup);
            Assert.Equal(job.BlocklistedHistoryId, roundtripped.BlocklistedHistoryId);
            Assert.Equal(job.ChannelId, roundtripped.ChannelId);
            Assert.Equal(job.MessageId, roundtripped.MessageId);
            Assert.Equal(job.RequestedByUserId, roundtripped.RequestedByUserId);
            Assert.Equal(job.RequestedByUsername, roundtripped.RequestedByUsername);
            Assert.Equal(job.CreatedUtc, roundtripped.CreatedUtc);
            Assert.Equal(job.DeadlineUtc, roundtripped.DeadlineUtc);
            Assert.Equal(DateTimeKind.Utc, roundtripped.DeadlineUtc.Kind);
            Assert.Equal(job.State, roundtripped.State);

            // State must be serialized as its string name (human-readable, order-change-safe),
            // not as the underlying int — Assert.Equal above would pass either way.
            var json = File.ReadAllText(_tempPath);
            Assert.Contains("\"Searching\"", json);
        }

        [Fact]
        public void Add_ThenNewRepositoryInstance_RoundtripsUlongMaxValue()
        {
            // Discord snowflakes exceed long.MaxValue in theory; verify Newtonsoft handles the
            // true ulong ceiling, not just realistic snowflake magnitudes.
            var repo = new RedownloadJobsRepository(_tempPath);
            var job = MakeJob();
            job.ChannelId = ulong.MaxValue;
            job.MessageId = ulong.MaxValue;
            job.RequestedByUserId = ulong.MaxValue;

            repo.Add(job);

            var reloaded = new RedownloadJobsRepository(_tempPath);
            var roundtripped = reloaded.GetActive().Single();

            Assert.Equal(ulong.MaxValue, roundtripped.ChannelId);
            Assert.Equal(ulong.MaxValue, roundtripped.MessageId);
            Assert.Equal(ulong.MaxValue, roundtripped.RequestedByUserId);
        }

        [Fact]
        public void HasActiveFor_TrueWhileSearching_FalseAfterUpdateToCompleted()
        {
            var repo = new RedownloadJobsRepository(_tempPath);
            var job = MakeJob(isMovie: false, arrId: 55, state: RedownloadJobState.Searching);
            repo.Add(job);

            Assert.True(repo.HasActiveFor(false, 55));

            job.State = RedownloadJobState.Completed;
            repo.Update(job);

            Assert.False(repo.HasActiveFor(false, 55));

            // Add stores the caller's reference, so mutating job.State above and asserting via
            // the same repo instance would pass even if Update() never persisted anything. Force
            // a fresh instance to reload from disk to prove Update actually wrote the file.
            var reloaded = new RedownloadJobsRepository(_tempPath);
            Assert.False(reloaded.HasActiveFor(false, 55));
        }

        [Fact]
        public void HasActiveFor_DistinguishesByIsMovieAndArrId()
        {
            var repo = new RedownloadJobsRepository(_tempPath);
            repo.Add(MakeJob(isMovie: false, arrId: 10, state: RedownloadJobState.Searching));

            Assert.True(repo.HasActiveFor(false, 10));
            Assert.False(repo.HasActiveFor(true, 10));
            Assert.False(repo.HasActiveFor(false, 11));
        }

        [Fact]
        public void Constructor_MissingFile_ReturnsEmptyList()
        {
            var missingPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
            Assert.False(File.Exists(missingPath));

            var repo = new RedownloadJobsRepository(missingPath);

            Assert.Empty(repo.GetActive());
        }

        [Fact]
        public void Constructor_EmptyFile_ReturnsEmptyList()
        {
            File.WriteAllText(_tempPath, string.Empty);

            var repo = new RedownloadJobsRepository(_tempPath);

            Assert.Empty(repo.GetActive());
        }

        [Fact]
        public void Constructor_CorruptFile_ReturnsEmptyListWithoutThrowing()
        {
            File.WriteAllText(_tempPath, "{not json");

            var repo = new RedownloadJobsRepository(_tempPath);

            Assert.Empty(repo.GetActive());
        }

        [Fact]
        public void Update_JobNotInList_Throws()
        {
            var repo = new RedownloadJobsRepository(_tempPath);
            var job = MakeJob();

            Assert.Throws<InvalidOperationException>(() => repo.Update(job));
        }

        [Fact]
        public void GetActive_OnlyReturnsSearchingJobs()
        {
            var repo = new RedownloadJobsRepository(_tempPath);
            repo.Add(MakeJob(state: RedownloadJobState.Searching));
            repo.Add(MakeJob(state: RedownloadJobState.Completed));
            repo.Add(MakeJob(state: RedownloadJobState.Restored));
            repo.Add(MakeJob(state: RedownloadJobState.RestoreFailed));

            var active = repo.GetActive();

            Assert.Single(active);
            Assert.Equal(RedownloadJobState.Searching, active[0].State);
        }

        [Fact]
        public void DefaultConstructor_DoesNotCreateFileIfMissing()
        {
            var originalFolder = Requestrr.WebApi.RequestrrBot.SettingsFile.SettingsFolder;
            var tempDir = Path.Combine(Path.GetTempPath(), "redownload-ctor-test-" + Guid.NewGuid());
            try
            {
                Requestrr.WebApi.RequestrrBot.SettingsFile.SettingsFolder = tempDir;
                var expectedPath = Path.Combine(tempDir, "redownload-jobs.json");

                var repo = new RedownloadJobsRepository();

                Assert.False(File.Exists(expectedPath));
                Assert.Empty(repo.GetActive());
            }
            finally
            {
                Requestrr.WebApi.RequestrrBot.SettingsFile.SettingsFolder = originalFolder;
            }
        }
    }
}
