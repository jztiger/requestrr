using System.Linq;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Requestrr.WebApi.RequestrrBot.Redownload.Arr;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class SonarrRedownloadClientTests
    {
        // Shape per Sonarr v3 /episodefile/{id}: id, path, size, quality.quality.name, releaseGroup.
        private const string EpisodeFileFixture = @"
        {
            ""id"": 32974,
            ""path"": ""/data/plexmedia/TV/Show/Season 01/Show.S01E01.WEBDL-1080p.NTb.mkv"",
            ""size"": 1234567890,
            ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
            ""releaseGroup"": ""NTb""
        }";

        // releaseGroup can be null when Sonarr couldn't parse a group from the release name.
        private const string EpisodeFileFixtureNullReleaseGroup = @"
        {
            ""id"": 32975,
            ""path"": ""/data/plexmedia/TV/Show/Season 01/Show.S01E02.WEBDL-1080p.mkv"",
            ""size"": 987654321,
            ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
            ""releaseGroup"": null
        }";

        private const string HistoryFixtureWithGrab = @"
        {
            ""records"": [
                { ""id"": 9001, ""eventType"": ""grabbed"", ""date"": ""2026-08-01T00:00:00Z"" }
            ]
        }";

        private const string HistoryFixtureEmpty = @"
        {
            ""records"": []
        }";

        // Two manualimport candidates from the same recycle-bin folder scan; only the first's
        // basename matches the requested original file name.
        private const string ManualImportFixture = @"
        [
            {
                ""path"": ""/data/plexmedia/_quarantine/sonarr/Show/Season 01/Show.S01E01.WEBDL-1080p.NTb.mkv"",
                ""series"": { ""id"": 42, ""title"": ""Show"" },
                ""episodes"": [ { ""id"": 501 } ],
                ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": ""NTb""
            },
            {
                ""path"": ""/data/plexmedia/_quarantine/sonarr/Other/Season 02/Other.S02E02.HDTV-720p.mkv"",
                ""series"": { ""id"": 43, ""title"": ""Other"" },
                ""episodes"": [ { ""id"": 600 } ],
                ""quality"": { ""quality"": { ""name"": ""HDTV-720p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": ""XYZ""
            }
        ]";

        // ---- ParseEpisodeFile -------------------------------------------------------

        [Fact]
        public void ParseEpisodeFile_ParsesAllFields()
        {
            var json = JObject.Parse(EpisodeFileFixture);

            var result = SonarrRedownloadClient.ParseEpisodeFile(json);

            Assert.Equal(32974, result.Id);
            Assert.Equal("/data/plexmedia/TV/Show/Season 01/Show.S01E01.WEBDL-1080p.NTb.mkv", result.Path);
            Assert.Equal("WEBDL-1080p", result.Quality);
            Assert.Equal(1234567890L, result.SizeBytes);
            Assert.Equal("NTb", result.ReleaseGroup);
        }

        [Fact]
        public void ParseEpisodeFile_NullReleaseGroup_MapsToNull()
        {
            var json = JObject.Parse(EpisodeFileFixtureNullReleaseGroup);

            var result = SonarrRedownloadClient.ParseEpisodeFile(json);

            Assert.Null(result.ReleaseGroup);
        }

        // ---- ParseNewestGrabId -------------------------------------------------------

        [Fact]
        public void ParseNewestGrabId_ReturnsFirstRecordId()
        {
            var json = JObject.Parse(HistoryFixtureWithGrab);

            var result = SonarrRedownloadClient.ParseNewestGrabId(json);

            Assert.Equal(9001L, result);
        }

        [Fact]
        public void ParseNewestGrabId_EmptyRecords_ReturnsNull()
        {
            var json = JObject.Parse(HistoryFixtureEmpty);

            var result = SonarrRedownloadClient.ParseNewestGrabId(json);

            Assert.Null(result);
        }

        // ---- FindRecycleBinCandidate -------------------------------------------------------

        [Fact]
        public void FindRecycleBinCandidate_MatchesByBasenameCaseInsensitive()
        {
            var items = (JArray)JArray.Parse(ManualImportFixture);

            var result = SonarrRedownloadClient.FindRecycleBinCandidate(items, "SHOW.S01E01.WEBDL-1080P.NTB.MKV");

            Assert.NotNull(result);
            Assert.Equal(42, (int)result["series"]["id"]);
        }

        [Fact]
        public void FindRecycleBinCandidate_NoMatch_ReturnsNull()
        {
            var items = (JArray)JArray.Parse(ManualImportFixture);

            var result = SonarrRedownloadClient.FindRecycleBinCandidate(items, "Nonexistent.File.mkv");

            Assert.Null(result);
        }

        // ---- BuildManualImportPayload -------------------------------------------------------

        [Fact]
        public void BuildManualImportPayload_UsesCandidateSeriesAndEpisodeIds()
        {
            var items = (JArray)JArray.Parse(ManualImportFixture);
            var candidate = (JObject)SonarrRedownloadClient.FindRecycleBinCandidate(items, "Show.S01E01.WEBDL-1080p.NTb.mkv");
            var target = new RedownloadTarget { ArrId = 999, SeriesId = 111 };

            var payload = SonarrRedownloadClient.BuildManualImportPayload(candidate, target);

            Assert.Equal("ManualImport", (string)payload["name"]);
            Assert.Equal("move", (string)payload["importMode"]);
            var file = (JObject)payload["files"][0];
            Assert.Equal(42, (int)file["seriesId"]);
            Assert.Equal(new[] { 501 }, ((JArray)file["episodeIds"]).Select(x => (int)x));
            Assert.Equal("NTb", (string)file["releaseGroup"]);
            Assert.Equal("WEBDL-1080p", (string)file["quality"]["quality"]["name"]);
        }

        [Fact]
        public void BuildManualImportPayload_FallsBackToTargetWhenCandidateMissingIds()
        {
            var candidate = JObject.Parse(@"{
                ""path"": ""/data/plexmedia/_quarantine/sonarr/Show/Season 01/Show.S01E01.mkv"",
                ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": null
            }");
            var target = new RedownloadTarget { ArrId = 999, SeriesId = 111 };

            var payload = SonarrRedownloadClient.BuildManualImportPayload(candidate, target);

            var file = (JObject)payload["files"][0];
            Assert.Equal(111, (int)file["seriesId"]);
            Assert.Equal(new[] { 999 }, ((JArray)file["episodeIds"]).Select(x => (int)x));
            Assert.Equal("", (string)file["releaseGroup"]);
        }
    }
}
