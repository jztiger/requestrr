using System.Linq;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Requestrr.WebApi.RequestrrBot.Redownload.Arr;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class RadarrRedownloadClientTests
    {
        // Shape per Radarr v3 /moviefile/{id}: id, path, size, quality.quality.name, releaseGroup.
        // Live-verified 2026-08-08: releaseGroup was "" (empty string) on the sampled instance, but
        // Radarr can also return null when it couldn't parse a group — both are exercised below.
        private const string MovieFileFixture = @"
        {
            ""id"": 16487,
            ""path"": ""/data/plexmedia/Movies/Movie (2011)/Movie (2011) {tmdb-76122} [Bluray-1080p][AC3 5.1][x265].mkv"",
            ""size"": 44880732,
            ""quality"": { ""quality"": { ""name"": ""Bluray-1080p"" } },
            ""releaseGroup"": ""NTb""
        }";

        private const string MovieFileFixtureNullReleaseGroup = @"
        {
            ""id"": 16488,
            ""path"": ""/data/plexmedia/Movies/Movie (2012)/Movie (2012).mkv"",
            ""size"": 987654321,
            ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
            ""releaseGroup"": null
        }";

        // Live-verified 2026-08-08 against GET /history/movie?movieId=1211: plain JSON array,
        // newest first, mixed event types — the grabbed event is NOT necessarily first.
        private const string HistoryArrayWithGrabNotFirst = @"
        [
            { ""id"": 46478, ""eventType"": ""downloadFolderImported"", ""date"": ""2026-08-08T04:35:57Z"" },
            { ""id"": 46477, ""eventType"": ""movieFileDeleted"", ""date"": ""2026-08-08T04:35:24Z"" },
            { ""id"": 1542, ""eventType"": ""downloadFolderImported"", ""date"": ""2022-02-06T04:59:27Z"" },
            { ""id"": 1528, ""eventType"": ""grabbed"", ""date"": ""2022-02-06T04:58:51Z"" }
        ]";

        private const string HistoryArrayEmpty = @"[]";

        private const string HistoryArrayNoGrabbedEvents = @"
        [
            { ""id"": 46478, ""eventType"": ""downloadFolderImported"", ""date"": ""2026-08-08T04:35:57Z"" },
            { ""id"": 46477, ""eventType"": ""movieFileDeleted"", ""date"": ""2026-08-08T04:35:24Z"" }
        ]";

        // Two manualimport candidates from the same recycle-bin folder scan; only the first's
        // basename matches the requested original file name. movieId lives under "movie", not
        // "series"/"episodes" — Radarr's ManualImport candidates carry movieId directly.
        private const string ManualImportFixture = @"
        [
            {
                ""path"": ""/data/plexmedia/_quarantine/radarr/Movie (2011)/Movie.2011.Bluray-1080p.NTb.mkv"",
                ""movie"": { ""id"": 1211, ""title"": ""Movie"" },
                ""quality"": { ""quality"": { ""name"": ""Bluray-1080p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": ""NTb""
            },
            {
                ""path"": ""/data/plexmedia/_quarantine/radarr/Other (2012)/Other.2012.WEBDL-1080p.mkv"",
                ""movie"": { ""id"": 1212, ""title"": ""Other"" },
                ""quality"": { ""quality"": { ""name"": ""WEBDL-1080p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": ""XYZ""
            }
        ]";

        // ---- ParseMovieFile -------------------------------------------------------

        [Fact]
        public void ParseMovieFile_ParsesAllFields()
        {
            var json = JObject.Parse(MovieFileFixture);

            var result = RadarrRedownloadClient.ParseMovieFile(json);

            Assert.Equal(16487, result.Id);
            Assert.Equal("/data/plexmedia/Movies/Movie (2011)/Movie (2011) {tmdb-76122} [Bluray-1080p][AC3 5.1][x265].mkv", result.Path);
            Assert.Equal("Bluray-1080p", result.Quality);
            Assert.Equal(44880732L, result.SizeBytes);
            Assert.Equal("NTb", result.ReleaseGroup);
        }

        [Fact]
        public void ParseMovieFile_NullReleaseGroup_MapsToNull()
        {
            var json = JObject.Parse(MovieFileFixtureNullReleaseGroup);

            var result = RadarrRedownloadClient.ParseMovieFile(json);

            Assert.Null(result.ReleaseGroup);
        }

        // ---- ParseNewestGrabIdFromArray -------------------------------------------------------

        [Fact]
        public void ParseNewestGrabIdFromArray_ReturnsFirstGrabbedEvent_EvenWhenNotArrayHead()
        {
            var json = (JArray)JArray.Parse(HistoryArrayWithGrabNotFirst);

            var result = RadarrRedownloadClient.ParseNewestGrabIdFromArray(json);

            Assert.Equal(1528L, result);
        }

        [Fact]
        public void ParseNewestGrabIdFromArray_EmptyArray_ReturnsNull()
        {
            var json = (JArray)JArray.Parse(HistoryArrayEmpty);

            var result = RadarrRedownloadClient.ParseNewestGrabIdFromArray(json);

            Assert.Null(result);
        }

        [Fact]
        public void ParseNewestGrabIdFromArray_NoGrabbedEvents_ReturnsNull()
        {
            var json = (JArray)JArray.Parse(HistoryArrayNoGrabbedEvents);

            var result = RadarrRedownloadClient.ParseNewestGrabIdFromArray(json);

            Assert.Null(result);
        }

        // ---- FindRecycleBinCandidate (reused from SonarrRedownloadClient; arr-agnostic) -------------------------------------------------------

        [Fact]
        public void FindRecycleBinCandidate_MatchesByBasenameCaseInsensitive()
        {
            var items = (JArray)JArray.Parse(ManualImportFixture);

            var result = SonarrRedownloadClient.FindRecycleBinCandidate(items, "MOVIE.2011.BLURAY-1080P.NTB.MKV");

            Assert.NotNull(result);
            Assert.Equal(1211, (int)result["movie"]["id"]);
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
        public void BuildManualImportPayload_UsesCandidateMovieId()
        {
            var items = (JArray)JArray.Parse(ManualImportFixture);
            var candidate = (JObject)SonarrRedownloadClient.FindRecycleBinCandidate(items, "Movie.2011.Bluray-1080p.NTb.mkv");
            var target = new RedownloadTarget { ArrId = 999 };

            var payload = RadarrRedownloadClient.BuildManualImportPayload(candidate, target);

            Assert.Equal("ManualImport", (string)payload["name"]);
            Assert.Equal("move", (string)payload["importMode"]);
            var file = (JObject)payload["files"][0];
            Assert.Equal(1211, (int)file["movieId"]);
            Assert.Equal("NTb", (string)file["releaseGroup"]);
            Assert.Equal("Bluray-1080p", (string)file["quality"]["quality"]["name"]);
        }

        [Fact]
        public void BuildManualImportPayload_FallsBackToTargetWhenCandidateMissingMovieId()
        {
            var candidate = JObject.Parse(@"{
                ""path"": ""/data/plexmedia/_quarantine/radarr/Movie (2011)/Movie.2011.mkv"",
                ""quality"": { ""quality"": { ""name"": ""Bluray-1080p"" } },
                ""languages"": [ { ""id"": 1, ""name"": ""English"" } ],
                ""releaseGroup"": null
            }");
            var target = new RedownloadTarget { ArrId = 999 };

            var payload = RadarrRedownloadClient.BuildManualImportPayload(candidate, target);

            var file = (JObject)payload["files"][0];
            Assert.Equal(999, (int)file["movieId"]);
            Assert.Equal("", (string)file["releaseGroup"]);
        }
    }
}
