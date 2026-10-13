using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookshelfClientTests
    {
        // `book` object from a Bookshelf (Readarr 0.4) GET /api/v1/search entry: no id and no statistics when not in the library.
        private const string SearchBookNotInLibrary = @"{
          ""title"": ""Project Hail Mary"", ""authorTitle"": ""weir, andy Project Hail Mary"",
          ""overview"": ""Ryland Grace is the sole survivor..."", ""foreignBookId"": ""54493401"", ""titleSlug"": ""54493401"",
          ""releaseDate"": ""2021-05-04T00:00:00Z"", ""monitored"": false, ""anyEditionOk"": true,
          ""images"": [ { ""coverType"": ""cover"", ""url"": ""/MediaCover/0/cover.jpg"", ""remoteUrl"": ""https://img.example/phm.jpg"" } ],
          ""author"": { ""id"": 0, ""authorName"": ""Andy Weir"", ""foreignAuthorId"": ""6540057"", ""monitored"": false, ""monitorNewItems"": ""all"", ""qualityProfileId"": 0, ""metadataProfileId"": 0, ""rootFolderPath"": """", ""tags"": [] },
          ""editions"": [ { ""foreignEditionId"": ""54493401"", ""title"": ""Project Hail Mary"", ""monitored"": true } ] }";

        // GET /api/v1/book/{id} shape: in library, carries statistics.
        private const string BookInLibraryWithFile = @"{ ""id"": 12, ""title"": ""Dune"", ""foreignBookId"": ""234225"", ""titleSlug"": ""234225"", ""releaseDate"": ""1965-08-01T00:00:00Z"", ""monitored"": true,
          ""images"": [ { ""coverType"": ""cover"", ""url"": ""/MediaCover/12/cover.jpg"" } ],
          ""author"": { ""id"": 3, ""authorName"": ""Frank Herbert"", ""foreignAuthorId"": ""58"" }, ""editions"": [], ""statistics"": { ""bookFileCount"": 1, ""sizeOnDisk"": 555 } }";

        private const string BookInLibraryNoFile = @"{ ""id"": 13, ""title"": ""Dune Messiah"", ""foreignBookId"": ""44492285"", ""titleSlug"": ""44492285"", ""releaseDate"": ""1969-01-01T00:00:00Z"", ""monitored"": true,
          ""images"": [], ""author"": { ""id"": 3, ""authorName"": ""Frank Herbert"", ""foreignAuthorId"": ""58"" }, ""editions"": [], ""statistics"": { ""bookFileCount"": 0, ""sizeOnDisk"": 0 } }";

        // /search entry for an in-library book: id present but no statistics.
        private const string SearchBookInLibraryNoStats = @"{ ""id"": 13, ""title"": ""Dune Messiah"", ""foreignBookId"": ""44492285"", ""author"": { ""id"": 3, ""authorName"": ""Frank Herbert"" } }";

        private static BookshelfCategory Cat() => new BookshelfCategory { Id = 0, Name = "audiobook", Hostname = "h", Port = 8787, ApiKey = "k", ProfileId = 5, MetadataProfileId = 2, RootFolder = "/data/plexmedia/Audiobooks", Tags = new[] { 7 }, SearchNewRequests = true, MonitorNewRequests = true };

        [Fact]
        public void ConvertToBook_NotInLibrary()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookNotInLibrary));
            Assert.Equal("", b.DownloadClientId); Assert.Equal("54493401", b.BookId);
            Assert.Equal("Project Hail Mary", b.Title); Assert.Equal("Andy Weir", b.AuthorName); Assert.Equal("6540057", b.ForeignAuthorId);
            Assert.Equal(2021, b.Year); Assert.False(b.Available); Assert.False(b.Requested);
            Assert.Equal("https://img.example/phm.jpg", b.PosterPath);
            Assert.Equal("Ryland Grace is the sole survivor...", b.Overview);
        }

        [Fact] // Review Focus 2
        public void ConvertToBook_InLibraryWithFile_IsAvailable()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(BookInLibraryWithFile));
            Assert.Equal("12", b.DownloadClientId); Assert.True(b.Available); Assert.False(b.Requested);
            Assert.Equal("/MediaCover/12/cover.jpg", b.PosterPath);
        }

        [Fact] // Review Focus 3
        public void ConvertToBook_InLibraryNoFile_IsRequested()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(BookInLibraryNoFile));
            Assert.False(b.Available); Assert.True(b.Requested);
        }

        [Fact]
        public void ConvertToBook_InLibraryWithoutStatistics_IsRequestedNotAvailable()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookInLibraryNoStats));
            Assert.Equal("13", b.DownloadClientId); Assert.False(b.Available); Assert.True(b.Requested);
            Assert.Null(b.Year); Assert.Equal("", b.Overview);
        }

        [Fact]
        public void ConvertToBook_MissingAuthor_IsEmptyString()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(@"{ ""title"": ""X"", ""foreignBookId"": ""1"", ""id"": null }"));
            Assert.Equal("", b.AuthorName); Assert.Equal("", b.DownloadClientId); Assert.False(b.Requested);
        }

        [Fact] // Review Focus 1
        public void DescribeForSelect_AuthorAndYear()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookNotInLibrary));
            Assert.Equal("Andy Weir · 2021", BookshelfClientV1.DescribeForSelect(b));
            b.Year = null; Assert.Equal("Andy Weir", BookshelfClientV1.DescribeForSelect(b));
            b.Year = 2021; b.AuthorName = ""; Assert.Equal("2021", BookshelfClientV1.DescribeForSelect(b));
            b.Year = null; Assert.Equal("", BookshelfClientV1.DescribeForSelect(b));
        }

        [Fact]
        public void BuildAddBody_SetsAuthorProfilesRootTagsAndBothAddOptions()
        {
            var src = JObject.Parse(SearchBookNotInLibrary);
            var body = BookshelfClientV1.BuildAddBody(src, Cat());
            Assert.True((bool)body["monitored"]);
            Assert.Equal("54493401", (string)body["foreignBookId"]);
            Assert.Equal("Project Hail Mary", (string)body["title"]);
            Assert.Equal(5, (int)body["author"]["qualityProfileId"]);
            Assert.Equal(2, (int)body["author"]["metadataProfileId"]);
            Assert.Equal("/data/plexmedia/Audiobooks", (string)body["author"]["rootFolderPath"]);
            Assert.Equal(7, (int)body["author"]["tags"][0]);
            Assert.Equal(7, (int)body["tags"][0]);
            Assert.True((bool)body["author"]["monitored"]);
            Assert.Equal("none", (string)body["author"]["monitorNewItems"]);
            Assert.Equal("none", (string)body["author"]["addOptions"]["monitor"]);
            Assert.Equal("54493401", (string)body["author"]["addOptions"]["booksToMonitor"][0]);
            Assert.False((bool)body["author"]["addOptions"]["searchForMissingBooks"]);
            Assert.True((bool)body["addOptions"]["searchForNewBook"]);
            Assert.Equal("6540057", (string)body["author"]["foreignAuthorId"]);
            // input untouched (deep clone)
            Assert.Equal(0, (int)src["author"]["qualityProfileId"]);
            Assert.Null(src["addOptions"]);
        }

        [Fact]
        public void BuildAddBody_HonoursSearchAndMonitorFlags()
        {
            var c = Cat(); c.SearchNewRequests = false; c.MonitorNewRequests = false;
            var body = BookshelfClientV1.BuildAddBody(JObject.Parse(SearchBookNotInLibrary), c);
            Assert.False((bool)body["addOptions"]["searchForNewBook"]);
            Assert.False((bool)body["monitored"]);
        }

        [Fact]
        public void BuildAddBody_DropsNullIdsButKeepsLibraryAuthorId()
        {
            var j = JObject.Parse(@"{ ""id"": null, ""foreignBookId"": ""9"", ""title"": ""T"", ""author"": { ""id"": 3, ""authorName"": ""A"" } }");
            var body = BookshelfClientV1.BuildAddBody(j, Cat());
            Assert.False(body.ContainsKey("id"));
            Assert.Equal(3, (int)body["author"]["id"]);
            var j2 = JObject.Parse(@"{ ""foreignBookId"": ""9"", ""author"": { ""id"": null, ""authorName"": ""A"" } }");
            Assert.False(((JObject)BookshelfClientV1.BuildAddBody(j2, Cat())["author"]).ContainsKey("id"));
        }

        [Fact]
        public void GetBaseURL_UsesCategoryConnection()
        {
            var c = Cat(); c.UseSSL = true; c.BaseUrl = "/books";
            Assert.Equal("https://h:8787/books/api/v1", BookshelfClientV1.GetBaseURL(c));
        }
    }
}
