using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookshelfClientTests
    {
        // Live-verified 2026-10-13 against bookshelf-audio (Readarr 0.4.21.182)
        // GET /api/v1/search entry `book`, NOT in library (no id; author already in the library so author.id is set): Artemis box set by Andy Weir.
        private const string SearchBookNotInLibrary = @"{
  ""title"": ""Project Hail Mary / Artemis/ The Martian"",
  ""seriesTitle"": """",
  ""overview"": ""Please Note That The Following Individual Books As Per Original ISBN and Cover Image In this Listing shall be Dispatched "",
  ""authorId"": 2,
  ""foreignBookId"": ""91054370"",
  ""foreignEditionId"": ""119255205"",
  ""titleSlug"": ""91054370"",
  ""monitored"": false,
  ""releaseDate"": ""2021-01-01T08:00:00Z"",
  ""author"": {
    ""authorMetadataId"": 2,
    ""authorName"": ""Andy Weir"",
    ""foreignAuthorId"": ""6540057"",
    ""titleSlug"": ""6540057"",
    ""path"": ""/data/plexmedia/Audiobooks/Andy Weir"",
    ""qualityProfileId"": 3,
    ""metadataProfileId"": 1,
    ""monitored"": false,
    ""monitorNewItems"": ""none"",
    ""tags"": [
      1
    ],
    ""added"": ""2026-10-13T18:04:07Z"",
    ""statistics"": {
      ""bookFileCount"": 0,
      ""bookCount"": 0,
      ""availableBookCount"": 0,
      ""totalBookCount"": 0,
      ""sizeOnDisk"": 0,
      ""percentOfBooks"": 0
    },
    ""id"": 2
  },
  ""images"": [
    {
      ""url"": ""/MediaCoverProxy/63542e629e512e009794cecfc51d235cb41c8c26d111b44cbcb98a64849bcfce/119255205.jpg"",
      ""coverType"": ""cover"",
      ""extension"": "".jpg"",
      ""remoteUrl"": ""https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1676421869i/119255205.jpg""
    }
  ],
  ""added"": ""0001-01-01T05:51:00Z"",
  ""editions"": [
    {
      ""foreignEditionId"": ""119255205"",
      ""isbn13"": ""9789124123765"",
      ""title"": ""Project Hail Mary / Artemis/ The Martian"",
      ""language"": ""eng"",
      ""format"": ""Paperback"",
      ""isEbook"": false,
      ""monitored"": true
    }
  ],
  ""grabbed"": false
}";

        // Live-verified 2026-10-13 against bookshelf-ebook (Readarr 0.4.21.182)
        // GET /api/v1/search entry `book`, NOT in library, NEW author (no book id, no author id, author profiles 0): Dune by Frank Herbert.
        private const string SearchBookNewAuthor = @"{
  ""title"": ""Dune"",
  ""seriesTitle"": """",
  ""overview"": ""Set on the desert planet Arrakis, Dune is the story of the boy Paul Atreides, heir to a noble family tasked with ruling an inhospitable world where the only thing of value is the \u201cspice\u201d melange, a dr"",
  ""authorId"": 0,
  ""foreignBookId"": ""3634639"",
  ""foreignEditionId"": ""44767458"",
  ""titleSlug"": ""3634639"",
  ""monitored"": false,
  ""releaseDate"": ""1965-06-01T07:00:00Z"",
  ""author"": {
    ""authorMetadataId"": 0,
    ""authorName"": ""Frank Herbert"",
    ""foreignAuthorId"": ""58"",
    ""titleSlug"": ""58"",
    ""qualityProfileId"": 0,
    ""metadataProfileId"": 0,
    ""monitored"": false,
    ""monitorNewItems"": ""all"",
    ""tags"": [],
    ""added"": ""0001-01-01T05:51:00Z"",
    ""statistics"": {
      ""bookFileCount"": 0,
      ""bookCount"": 0,
      ""availableBookCount"": 0,
      ""totalBookCount"": 0,
      ""sizeOnDisk"": 0,
      ""percentOfBooks"": 0
    }
  },
  ""images"": [
    {
      ""url"": ""/MediaCoverProxy/488f399e0acf1319ebf12d8d97cae5f2190757ee8f1e411fb0697fa6e267580c/44767458.jpg"",
      ""coverType"": ""cover"",
      ""extension"": "".jpg"",
      ""remoteUrl"": ""https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1555447414i/44767458.jpg""
    }
  ],
  ""added"": ""0001-01-01T05:51:00Z"",
  ""editions"": [
    {
      ""foreignEditionId"": ""44767458"",
      ""isbn13"": ""9780593099322"",
      ""title"": ""Dune"",
      ""language"": ""eng"",
      ""format"": ""Hardcover"",
      ""isEbook"": false,
      ""monitored"": true
    }
  ],
  ""grabbed"": false
}";

        // Live-verified 2026-10-13 against bookshelf-audio (Readarr 0.4.21.182)
        // GET /api/v1/book/4: in library, with a file (statistics.bookFileCount 1).
        private const string BookInLibraryWithFile = @"{
  ""title"": ""Project Hail Mary"",
  ""seriesTitle"": """",
  ""authorId"": 2,
  ""foreignBookId"": ""79106958"",
  ""foreignEditionId"": ""54493401"",
  ""titleSlug"": ""79106958"",
  ""monitored"": true,
  ""releaseDate"": ""2021-05-04T12:00:00Z"",
  ""author"": {
    ""authorMetadataId"": 2,
    ""authorName"": ""Andy Weir"",
    ""foreignAuthorId"": ""6540057"",
    ""titleSlug"": ""6540057"",
    ""path"": ""/data/plexmedia/Audiobooks/Andy Weir"",
    ""qualityProfileId"": 3,
    ""metadataProfileId"": 1,
    ""monitored"": false,
    ""monitorNewItems"": ""none"",
    ""tags"": [
      1
    ],
    ""added"": ""2026-10-13T18:04:07Z"",
    ""statistics"": {
      ""bookFileCount"": 0,
      ""bookCount"": 0,
      ""availableBookCount"": 0,
      ""totalBookCount"": 0,
      ""sizeOnDisk"": 0,
      ""percentOfBooks"": 0
    },
    ""id"": 2
  },
  ""images"": [
    {
      ""url"": ""/MediaCover/Books/4/cover.jpg?lastWrite=639003007470000000"",
      ""coverType"": ""cover"",
      ""extension"": "".jpg"",
      ""remoteUrl"": ""https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1764703833i/54493401.jpg""
    }
  ],
  ""statistics"": {
    ""bookFileCount"": 1,
    ""bookCount"": 1,
    ""totalBookCount"": 1,
    ""sizeOnDisk"": 924597510,
    ""percentOfBooks"": 100
  },
  ""added"": ""2026-10-13T18:04:07Z"",
  ""lastSearchTime"": ""2026-10-13T18:04:25Z"",
  ""grabbed"": false,
  ""id"": 4
}";

        // Live-verified 2026-10-13 against bookshelf-audio (Readarr 0.4.21.182)
        // GET /api/v1/book/3: in library, no file (statistics.bookFileCount 0).
        private const string BookInLibraryNoFile = @"{
  ""title"": ""The Lions of Lucerne"",
  ""seriesTitle"": ""Scot Harvath #1"",
  ""authorId"": 1,
  ""foreignBookId"": ""2164503"",
  ""foreignEditionId"": ""206196"",
  ""titleSlug"": ""2164503"",
  ""monitored"": false,
  ""releaseDate"": ""2002-01-02T14:00:00Z"",
  ""author"": {
    ""authorMetadataId"": 1,
    ""authorName"": ""Brad Thor"",
    ""foreignAuthorId"": ""5088"",
    ""titleSlug"": ""5088"",
    ""path"": ""/data/plexmedia/Audiobooks/Brad Thor"",
    ""qualityProfileId"": 3,
    ""metadataProfileId"": 1,
    ""monitored"": true,
    ""monitorNewItems"": ""all"",
    ""tags"": [],
    ""added"": ""2026-10-13T18:03:00Z"",
    ""statistics"": {
      ""bookFileCount"": 0,
      ""bookCount"": 0,
      ""availableBookCount"": 0,
      ""totalBookCount"": 0,
      ""sizeOnDisk"": 0,
      ""percentOfBooks"": 0
    },
    ""id"": 1
  },
  ""images"": [
    {
      ""url"": ""/MediaCover/Books/3/cover.jpg?lastWrite=636041113010000000"",
      ""coverType"": ""cover"",
      ""extension"": "".jpg"",
      ""remoteUrl"": ""https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1388522067i/206196.jpg""
    }
  ],
  ""statistics"": {
    ""bookFileCount"": 0,
    ""bookCount"": 0,
    ""totalBookCount"": 1,
    ""sizeOnDisk"": 0,
    ""percentOfBooks"": 0
  },
  ""added"": ""2026-10-13T18:03:00Z"",
  ""grabbed"": false,
  ""id"": 3
}";

        // Live-verified 2026-10-13 against bookshelf-audio (Readarr 0.4.21.182)
        // GET /api/v1/search entry `book` for an in-library book: id present but NO statistics.
        private const string SearchBookInLibraryNoStats = @"{
  ""title"": ""Project Hail Mary"",
  ""seriesTitle"": """",
  ""overview"": ""Ryland Grace is the sole survivor on a desperate, last-chance mission\u2014and if he fails, humanity and the earth itself will perish."",
  ""authorId"": 2,
  ""foreignBookId"": ""79106958"",
  ""foreignEditionId"": ""54493401"",
  ""titleSlug"": ""79106958"",
  ""monitored"": true,
  ""releaseDate"": ""2021-05-04T07:00:00Z"",
  ""author"": {
    ""authorMetadataId"": 2,
    ""authorName"": ""Andy Weir"",
    ""foreignAuthorId"": ""6540057"",
    ""titleSlug"": ""6540057"",
    ""path"": ""/data/plexmedia/Audiobooks/Andy Weir"",
    ""qualityProfileId"": 3,
    ""metadataProfileId"": 1,
    ""monitored"": false,
    ""monitorNewItems"": ""none"",
    ""tags"": [
      1
    ],
    ""added"": ""2026-10-13T18:04:07Z"",
    ""statistics"": {
      ""bookFileCount"": 0,
      ""bookCount"": 0,
      ""availableBookCount"": 0,
      ""totalBookCount"": 0,
      ""sizeOnDisk"": 0,
      ""percentOfBooks"": 0
    },
    ""id"": 2
  },
  ""images"": [
    {
      ""url"": ""/MediaCover/Books/4/cover.jpg?lastWrite=639003007470000000"",
      ""coverType"": ""cover"",
      ""extension"": "".jpg"",
      ""remoteUrl"": ""https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1764703833i/54493401.jpg""
    }
  ],
  ""added"": ""2026-10-13T18:04:07Z"",
  ""lastSearchTime"": ""2026-10-13T18:04:25Z"",
  ""editions"": [
    {
      ""foreignEditionId"": ""54493401"",
      ""isbn13"": ""9780593135204"",
      ""title"": ""Project Hail Mary"",
      ""language"": ""eng"",
      ""format"": ""Hardcover"",
      ""isEbook"": false,
      ""monitored"": true
    }
  ],
  ""grabbed"": false,
  ""id"": 4
}";

        private static BookshelfCategory Cat() => new BookshelfCategory { Id = 0, Name = "audiobook", Hostname = "h", Port = 8787, ApiKey = "k", ProfileId = 5, MetadataProfileId = 2, RootFolder = "/data/plexmedia/Audiobooks", Tags = new[] { 7 }, SearchNewRequests = true, MonitorNewRequests = true };

        [Fact]
        public void ConvertToBook_NotInLibrary()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookNotInLibrary));
            Assert.Equal("", b.DownloadClientId); Assert.Equal("91054370", b.BookId);
            Assert.Equal("Project Hail Mary / Artemis/ The Martian", b.Title); Assert.Equal("Andy Weir", b.AuthorName); Assert.Equal("6540057", b.ForeignAuthorId);
            Assert.Equal(2021, b.Year); Assert.False(b.Available); Assert.False(b.Requested);
            Assert.Equal("https://m.media-amazon.com/images/S/compressed.photo.goodreads.com/books/1676421869i/119255205.jpg", b.PosterPath);
            Assert.StartsWith("Please Note That", b.Overview);
        }

        [Fact] // Review Focus 2
        public void ConvertToBook_InLibraryWithFile_IsAvailable()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(BookInLibraryWithFile));
            Assert.Equal("4", b.DownloadClientId); Assert.True(b.Available); Assert.False(b.Requested);
            Assert.Equal("Project Hail Mary", b.Title); Assert.Equal("79106958", b.BookId); Assert.Equal(2021, b.Year); Assert.Equal("Andy Weir", b.AuthorName);
            Assert.StartsWith("https://m.media-amazon.com/", b.PosterPath); // remoteUrl preferred over the /MediaCover proxy path
        }

        [Fact] // Review Focus 3: Readarr adds an author's backlist unmonitored; that is NOT a request
        public void ConvertToBook_InLibraryNoFileUnmonitored_IsNotRequested()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(BookInLibraryNoFile));
            Assert.Equal("3", b.DownloadClientId); Assert.Equal("The Lions of Lucerne", b.Title); Assert.Equal("Brad Thor", b.AuthorName); Assert.Equal(2002, b.Year);
            Assert.False(b.Available); Assert.False(b.Requested); Assert.False(b.Monitored);
        }

        [Fact]
        public void ConvertToBook_InLibraryNoFileMonitored_IsRequested()
        {
            var j = JObject.Parse(BookInLibraryNoFile); j["monitored"] = true;
            var b = BookshelfClientV1.ConvertToBook(j);
            Assert.False(b.Available); Assert.True(b.Requested); Assert.True(b.Monitored);
        }

        [Fact]
        public void ConvertToBook_InLibraryWithoutStatistics_IsRequestedNotAvailable()
        {
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookInLibraryNoStats));
            Assert.Equal("4", b.DownloadClientId); Assert.False(b.Available); Assert.True(b.Requested);
            Assert.Equal(2021, b.Year); Assert.StartsWith("Ryland Grace", b.Overview);
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
            var b = BookshelfClientV1.ConvertToBook(JObject.Parse(SearchBookInLibraryNoStats));
            Assert.Equal("Andy Weir · 2021", BookshelfClientV1.DescribeForSelect(b));
            b.Year = null; Assert.Equal("Andy Weir", BookshelfClientV1.DescribeForSelect(b));
            b.Year = 2021; b.AuthorName = ""; Assert.Equal("2021", BookshelfClientV1.DescribeForSelect(b));
            b.Year = null; Assert.Equal("", BookshelfClientV1.DescribeForSelect(b));
        }

        [Fact]
        public void BuildAddBody_SetsAuthorProfilesRootTagsAndBothAddOptions()
        {
            var src = JObject.Parse(SearchBookNewAuthor);
            var body = BookshelfClientV1.BuildAddBody(src, Cat());
            Assert.True((bool)body["monitored"]);
            Assert.Equal("3634639", (string)body["foreignBookId"]);
            Assert.Equal("Dune", (string)body["title"]);
            Assert.Equal(5, (int)body["author"]["qualityProfileId"]);
            Assert.Equal(2, (int)body["author"]["metadataProfileId"]);
            Assert.Equal("/data/plexmedia/Audiobooks", (string)body["author"]["rootFolderPath"]);
            Assert.Equal(7, (int)body["author"]["tags"][0]);
            Assert.Equal(7, (int)body["tags"][0]);
            Assert.True((bool)body["author"]["monitored"]);
            Assert.Equal("none", (string)body["author"]["monitorNewItems"]);
            Assert.Equal("none", (string)body["author"]["addOptions"]["monitor"]);
            Assert.Equal("3634639", (string)body["author"]["addOptions"]["booksToMonitor"][0]);
            Assert.False((bool)body["author"]["addOptions"]["searchForMissingBooks"]);
            Assert.True((bool)body["addOptions"]["searchForNewBook"]);
            Assert.Equal("58", (string)body["author"]["foreignAuthorId"]);
            // input untouched (deep clone)
            Assert.Equal(0, (int)src["author"]["qualityProfileId"]);
            Assert.Null(src["addOptions"]);
            Assert.Null(src["author"]["id"]); Assert.Null(body["author"]["id"]); // new author: no id sent
            Assert.Equal("Frank Herbert", (string)body["author"]["authorName"]);
        }

        [Fact]
        public void BuildAddBody_ExistingAuthor_KeepsAuthorIdAndOverridesProfiles()
        {
            var body = BookshelfClientV1.BuildAddBody(JObject.Parse(SearchBookNotInLibrary), Cat());
            Assert.Equal(2, (int)body["author"]["id"]);
            Assert.Equal(5, (int)body["author"]["qualityProfileId"]); // live value was 3, category wins
            Assert.Equal("91054370", (string)body["author"]["addOptions"]["booksToMonitor"][0]);
        }

        [Fact]
        public void BuildAddBody_HonoursSearchAndMonitorFlags()
        {
            var c = Cat(); c.SearchNewRequests = false; c.MonitorNewRequests = false;
            var body = BookshelfClientV1.BuildAddBody(JObject.Parse(SearchBookNewAuthor), c);
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
