using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookshelfClientHttpTests
    {
        public class Recorded { public HttpMethod Method; public string Path; public string Query; public string Body; public string ApiKey; }

        private class FakeHandler : HttpMessageHandler
        {
            public readonly List<Recorded> Requests = new List<Recorded>();
            // key: "METHOD path?query" (query decoded) or "METHOD path"; value: (status, json)
            public readonly Dictionary<string, (HttpStatusCode, string)> Routes = new Dictionary<string, (HttpStatusCode, string)>();

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var rec = new Recorded
                {
                    Method = request.Method,
                    Path = request.RequestUri.AbsolutePath,
                    Query = Uri.UnescapeDataString(request.RequestUri.Query),
                    Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(),
                    ApiKey = request.Headers.TryGetValues("X-Api-Key", out var v) ? v.Single() : null
                };
                Requests.Add(rec);
                var withQuery = $"{rec.Method} {rec.Path}{rec.Query}";
                var plain = $"{rec.Method} {rec.Path}";
                if (Routes.TryGetValue(withQuery, out var r) || Routes.TryGetValue(plain, out r))
                    return new HttpResponseMessage(r.Item1) { Content = new StringContent(r.Item2) };
                return new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{}") };
            }
        }

        private class Factory : IHttpClientFactory
        {
            private readonly HttpMessageHandler _h; public Factory(HttpMessageHandler h) { _h = h; }
            public HttpClient CreateClient(string name) => new HttpClient(_h, false);
        }

        private class Provider : BookshelfSettingsProvider
        {
            private readonly BookshelfCategory[] _c; public Provider(params BookshelfCategory[] c) { _c = c; }
            public override BookshelfSettings Provide() => new BookshelfSettings { Categories = _c };
        }

        private static BookshelfCategory Cat(int id = 0, string key = "key-A", bool search = true) => new BookshelfCategory
        { Id = id, Name = "c" + id, Hostname = "h", Port = 8787, ApiKey = key, ProfileId = 5, MetadataProfileId = 2, RootFolder = "/data/Books", Tags = new[] { 7 }, SearchNewRequests = search, MonitorNewRequests = true };

        private static (BookshelfClientV1, FakeHandler) Make(params BookshelfCategory[] cats)
        {
            var h = new FakeHandler();
            return (new BookshelfClientV1(new Factory(h), NullLogger<BookshelfClient>.Instance, new Provider(cats)), h);
        }

        private static BookRequest Req(int cat = 0) => new BookRequest(new BookUserRequester("1", "u"), cat);

        // Live-verified 2026-10-13 against bookshelf-audio (Readarr 0.4.21.182); NotInLib (Dune) captured from bookshelf-ebook, same version
        // NotInLib = /search `book` (ebook instance capture, Dune/Frank Herbert) with no id; InLib* = PHM id 4 (search entry has no statistics; GET /book/4 does).
        private const string NotInLib = @"{
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

        private const string InLibStubJson = @"{
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

        private const string InLibFullJson = @"{
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

        private static string Entry(string bookJson) => $"{{ \"id\": 1, \"foreignId\": \"x\", \"book\": {bookJson} }}";
        private const string AuthorOnlyEntry = @"{ ""foreignId"": ""6540057"", ""id"": 1, ""author"": { ""authorName"": ""Andy Weir"", ""foreignAuthorId"": ""6540057"" } }";
        private static string InLibStub(int id) => InLibStubJson;
        private static string InLibFull(int id, int files) { var j = JObject.Parse(InLibFullJson); j["statistics"]["bookFileCount"] = files; return j.ToString(); }

        [Fact]
        public async Task Search_InLibraryHit_FetchesStatistics_AvailableWhenFileExists()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))},{AuthorOnlyEntry},{Entry(NotInLib)}]");
            h.Routes["GET /api/v1/book/4"] = (HttpStatusCode.OK, InLibFull(4, 2));

            var books = await client.SearchBookAsync(Req(), "dune");

            Assert.Equal(2, books.Count); // author-only entry dropped
            var dune = books.Single(b => b.BookId == "79106958"); // PHM, in library with a file
            Assert.True(dune.Available); Assert.False(dune.Requested);
            Assert.False(books.Single(b => b.BookId == "3634639").Available);
            Assert.Contains(h.Requests, r => r.Path == "/api/v1/book/4");
            Assert.DoesNotContain(h.Requests, r => r.Path.StartsWith("/api/v1/book/") && r.Path != "/api/v1/book/4");
            Assert.All(h.Requests, r => Assert.Equal("key-A", r.ApiKey));
        }

        [Fact]
        public async Task Search_InLibraryHitWithoutFile_IsRequested()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))}]");
            h.Routes["GET /api/v1/book/4"] = (HttpStatusCode.OK, InLibFull(4, 0));
            var b = (await client.SearchBookAsync(Req(), "dune")).Single();
            Assert.False(b.Available); Assert.True(b.Requested);
        }

        [Fact]
        public async Task Search_StatisticsFetchFails_DegradesToRequested()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))}]");
            h.Routes["GET /api/v1/book/4"] = (HttpStatusCode.InternalServerError, "{}");
            var b = (await client.SearchBookAsync(Req(), "dune")).Single();
            Assert.False(b.Available); Assert.True(b.Requested);
        }

        [Fact]
        public async Task Search_SearchEndpointFails_Throws()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.InternalServerError, "{}");
            await Assert.ThrowsAsync<Exception>(() => client.SearchBookAsync(Req(), "dune"));
        }

        [Fact]
        public async Task Request_NotInLibrary_PostsAddBodyWithCategoryApiKey()
        {
            var c1 = Cat(1, "key-B");
            var (client, h) = Make(Cat(0, "key-A"), c1);
            h.Routes["GET /api/v1/search?term=work:3634639"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");
            h.Routes["POST /api/v1/book"] = (HttpStatusCode.Created, "{}");

            var result = await client.RequestBookAsync(Req(1), new Book { BookId = "3634639", Title = "Dune" });

            Assert.False(result.WasDenied);
            var post = h.Requests.Single(r => r.Method == HttpMethod.Post);
            Assert.Equal("/api/v1/book", post.Path);
            Assert.Equal("key-B", post.ApiKey);
            var expected = BookshelfClientV1.BuildAddBody(JObject.Parse(NotInLib), c1);
            Assert.True(JToken.DeepEquals(expected, JObject.Parse(post.Body)));
            Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Put);
        }

        [Fact]
        public async Task Request_InLibrary_PutsBookMonitorThenSearches()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:79106958"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))}]");
            h.Routes["PUT /api/v1/book/monitor"] = (HttpStatusCode.Accepted, "{}");
            h.Routes["POST /api/v1/command"] = (HttpStatusCode.Created, "{}");

            await client.RequestBookAsync(Req(), new Book { BookId = "79106958", Title = "Project Hail Mary" });

            var seq = h.Requests.Select(r => $"{r.Method} {r.Path}").ToList();
            Assert.Equal(new[] { "GET /api/v1/search", "PUT /api/v1/book/monitor", "POST /api/v1/command" }, seq);
            Assert.True(JToken.DeepEquals(JObject.Parse(@"{""bookIds"":[4],""monitored"":true}"), JObject.Parse(h.Requests[1].Body)));
            var cmd = JObject.Parse(h.Requests[2].Body);
            Assert.Equal("BookSearch", (string)cmd["name"]); Assert.Equal(4, (int)cmd["bookIds"][0]);
        }

        [Fact]
        public async Task Request_InLibrary_NoSearchWhenCategoryDisablesIt()
        {
            var (client, h) = Make(Cat(0, "k", search: false));
            h.Routes["GET /api/v1/search?term=work:79106958"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))}]");
            h.Routes["PUT /api/v1/book/monitor"] = (HttpStatusCode.Accepted, "{}");
            await client.RequestBookAsync(Req(), new Book { BookId = "79106958", Title = "Project Hail Mary" });
            var seq = h.Requests.Select(r => $"{r.Method} {r.Path}").ToList();
            Assert.Equal(new[] { "GET /api/v1/search", "PUT /api/v1/book/monitor" }, seq);
        }

        [Fact]
        public async Task Request_BookNotFound_ThrowsAndPostsNothing()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search"] = (HttpStatusCode.OK, "[]");
            await Assert.ThrowsAsync<Exception>(() => client.RequestBookAsync(Req(), new Book { BookId = "1", Title = "T", AuthorName = "A" }));
            Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Post);
        }

        [Fact]
        public async Task FindById_WorkTermMatches_NoFallbackSearch()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:3634639"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");
            var b = await client.SearchBookByIdAsync(Req(), "3634639");
            Assert.Equal("Dune", b.Title);
            Assert.Single(h.Requests);
        }

        [Fact]
        public async Task FindById_WorkTermEmpty_FallsBackToTitleSearchWithExactIdMatch()
        {
            var (client, h) = Make(Cat());
            var other = NotInLib.Replace("3634639", "999");
            h.Routes["GET /api/v1/search?term=work:3634639"] = (HttpStatusCode.OK, "[]");
            h.Routes["GET /api/v1/search?term=Dune Frank Herbert"] = (HttpStatusCode.OK, $"[{Entry(other)},{Entry(NotInLib)}]");
            h.Routes["POST /api/v1/book"] = (HttpStatusCode.Created, "{}");

            await client.RequestBookAsync(Req(), new Book { BookId = "3634639", Title = "Dune", AuthorName = "Frank Herbert" });

            Assert.Equal(new[] { "work:3634639", "Dune Frank Herbert" },
                h.Requests.Where(r => r.Path == "/api/v1/search").Select(r => r.Query.Replace("?term=", "")).ToArray());
            var post = JObject.Parse(h.Requests.Single(r => r.Method == HttpMethod.Post).Body);
            Assert.Equal("3634639", (string)post["foreignBookId"]); // not the "999" near-match
        }

        [Fact]
        public async Task SearchById_NoMatch_ReturnsNull()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:42"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]"); // different foreign id
            Assert.Null(await client.SearchBookByIdAsync(Req(), "42"));
        }

        [Fact]
        public async Task SearchById_HttpFailure_ThrowsRatherThanNull()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:42"] = (HttpStatusCode.InternalServerError, "{}");
            await Assert.ThrowsAsync<Exception>(() => client.SearchBookByIdAsync(Req(), "42"));
        }

        [Fact]
        public async Task Request_SearchHttpFailure_ThrowsGenericNotNotFound()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search"] = (HttpStatusCode.InternalServerError, "{}");
            var ex = await Assert.ThrowsAsync<Exception>(() => client.RequestBookAsync(Req(), new Book { BookId = "1", Title = "T" }));
            Assert.DoesNotContain("could not be found", ex.Message);
        }

        [Fact]
        public async Task Available_PerId_ReturnsOnlyBooksWithFiles_NoLibraryScan()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:79106958"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(4))}]");
            h.Routes["GET /api/v1/book/4"] = (HttpStatusCode.OK, InLibFull(4, 1));
            h.Routes["GET /api/v1/search?term=work:3634639"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");

            var res = await client.SearchAvailableBooksAsync(0, new HashSet<string> { "79106958", "3634639" }, CancellationToken.None);

            Assert.Equal(new[] { "79106958" }, res.Keys.ToArray());
            Assert.DoesNotContain(h.Requests, r => r.Path == "/api/v1/book");
        }
    }
}
