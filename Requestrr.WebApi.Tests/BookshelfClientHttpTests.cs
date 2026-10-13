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

        private const string NotInLib = @"{ ""title"": ""Project Hail Mary"", ""foreignBookId"": ""54493401"", ""releaseDate"": ""2021-05-04T00:00:00Z"", ""author"": { ""id"": 0, ""authorName"": ""Andy Weir"", ""foreignAuthorId"": ""6540057"" }, ""editions"": [ { ""foreignEditionId"": ""1"", ""monitored"": true } ] }";
        private static string Entry(string bookJson) => $"{{ \"id\": 1, \"foreignId\": \"x\", \"book\": {bookJson} }}";
        private const string AuthorOnlyEntry = @"{ ""id"": 9, ""foreignId"": ""a"", ""author"": { ""authorName"": ""Andy Weir"" } }";
        private static string InLibStub(int id) => $@"{{ ""id"": {id}, ""title"": ""Dune"", ""foreignBookId"": ""234225"", ""author"": {{ ""id"": 3, ""authorName"": ""Frank Herbert"" }} }}";
        private static string InLibFull(int id, int files) => $@"{{ ""id"": {id}, ""title"": ""Dune"", ""foreignBookId"": ""234225"", ""monitored"": false, ""author"": {{ ""id"": 3, ""authorName"": ""Frank Herbert"" }}, ""statistics"": {{ ""bookFileCount"": {files} }} }}";

        [Fact]
        public async Task Search_InLibraryHit_FetchesStatistics_AvailableWhenFileExists()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))},{AuthorOnlyEntry},{Entry(NotInLib)}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.OK, InLibFull(12, 2));

            var books = await client.SearchBookAsync(Req(), "dune");

            Assert.Equal(2, books.Count); // author-only entry dropped
            var dune = books.Single(b => b.BookId == "234225");
            Assert.True(dune.Available); Assert.False(dune.Requested);
            Assert.False(books.Single(b => b.BookId == "54493401").Available);
            Assert.Contains(h.Requests, r => r.Path == "/api/v1/book/12");
            Assert.DoesNotContain(h.Requests, r => r.Path.StartsWith("/api/v1/book/") && r.Path != "/api/v1/book/12");
            Assert.All(h.Requests, r => Assert.Equal("key-A", r.ApiKey));
        }

        [Fact]
        public async Task Search_InLibraryHitWithoutFile_IsRequested()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.OK, InLibFull(12, 0));
            var b = (await client.SearchBookAsync(Req(), "dune")).Single();
            Assert.False(b.Available); Assert.True(b.Requested);
        }

        [Fact]
        public async Task Search_StatisticsFetchFails_DegradesToRequested()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=dune"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.InternalServerError, "{}");
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
            h.Routes["GET /api/v1/search?term=work:54493401"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");
            h.Routes["POST /api/v1/book"] = (HttpStatusCode.Created, "{}");

            var result = await client.RequestBookAsync(Req(1), new Book { BookId = "54493401", Title = "Project Hail Mary" });

            Assert.False(result.WasDenied);
            var post = h.Requests.Single(r => r.Method == HttpMethod.Post);
            Assert.Equal("/api/v1/book", post.Path);
            Assert.Equal("key-B", post.ApiKey);
            var expected = BookshelfClientV1.BuildAddBody(JObject.Parse(NotInLib), c1);
            Assert.True(JToken.DeepEquals(expected, JObject.Parse(post.Body)));
            Assert.DoesNotContain(h.Requests, r => r.Method == HttpMethod.Put);
        }

        [Fact]
        public async Task Request_InLibrary_GetsPutsMonitoredThenSearches()
        {
            var (client, h) = Make(Cat());
            h.Routes["GET /api/v1/search?term=work:234225"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.OK, InLibFull(12, 0));
            h.Routes["PUT /api/v1/book/12"] = (HttpStatusCode.Accepted, "{}");
            h.Routes["POST /api/v1/command"] = (HttpStatusCode.Created, "{}");

            await client.RequestBookAsync(Req(), new Book { BookId = "234225", Title = "Dune" });

            var seq = h.Requests.Select(r => $"{r.Method} {r.Path}").ToList();
            Assert.Equal(new[] { "GET /api/v1/search", "GET /api/v1/book/12", "PUT /api/v1/book/12", "POST /api/v1/command" }, seq);
            var put = JObject.Parse(h.Requests[2].Body);
            Assert.True((bool)put["monitored"]);
            var fetched = JObject.Parse(InLibFull(12, 0)); fetched["monitored"] = true;
            Assert.True(JToken.DeepEquals(fetched, put));
            var cmd = JObject.Parse(h.Requests[3].Body);
            Assert.Equal("BookSearch", (string)cmd["name"]); Assert.Equal(12, (int)cmd["bookIds"][0]);
        }

        [Fact]
        public async Task Request_InLibrary_NoSearchWhenCategoryDisablesIt()
        {
            var (client, h) = Make(Cat(0, "k", search: false));
            h.Routes["GET /api/v1/search?term=work:234225"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.OK, InLibFull(12, 0));
            h.Routes["PUT /api/v1/book/12"] = (HttpStatusCode.Accepted, "{}");
            await client.RequestBookAsync(Req(), new Book { BookId = "234225", Title = "Dune" });
            Assert.DoesNotContain(h.Requests, r => r.Path == "/api/v1/command");
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
            h.Routes["GET /api/v1/search?term=work:54493401"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");
            var b = await client.SearchBookByIdAsync(Req(), "54493401");
            Assert.Equal("Project Hail Mary", b.Title);
            Assert.Single(h.Requests);
        }

        [Fact]
        public async Task FindById_WorkTermEmpty_FallsBackToTitleSearchWithExactIdMatch()
        {
            var (client, h) = Make(Cat());
            var other = NotInLib.Replace("54493401", "999");
            h.Routes["GET /api/v1/search?term=work:54493401"] = (HttpStatusCode.OK, "[]");
            h.Routes["GET /api/v1/search?term=Project Hail Mary Andy Weir"] = (HttpStatusCode.OK, $"[{Entry(other)},{Entry(NotInLib)}]");
            h.Routes["POST /api/v1/book"] = (HttpStatusCode.Created, "{}");

            await client.RequestBookAsync(Req(), new Book { BookId = "54493401", Title = "Project Hail Mary", AuthorName = "Andy Weir" });

            Assert.Equal(new[] { "work:54493401", "Project Hail Mary Andy Weir" },
                h.Requests.Where(r => r.Path == "/api/v1/search").Select(r => r.Query.Replace("?term=", "")).ToArray());
            var post = JObject.Parse(h.Requests.Single(r => r.Method == HttpMethod.Post).Body);
            Assert.Equal("54493401", (string)post["foreignBookId"]); // not the "999" near-match
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
            h.Routes["GET /api/v1/search?term=work:234225"] = (HttpStatusCode.OK, $"[{Entry(InLibStub(12))}]");
            h.Routes["GET /api/v1/book/12"] = (HttpStatusCode.OK, InLibFull(12, 1));
            h.Routes["GET /api/v1/search?term=work:54493401"] = (HttpStatusCode.OK, $"[{Entry(NotInLib)}]");

            var res = await client.SearchAvailableBooksAsync(0, new HashSet<string> { "234225", "54493401" }, CancellationToken.None);

            Assert.Equal(new[] { "234225" }, res.Keys.ToArray());
            Assert.DoesNotContain(h.Requests, r => r.Path == "/api/v1/book");
        }
    }
}
