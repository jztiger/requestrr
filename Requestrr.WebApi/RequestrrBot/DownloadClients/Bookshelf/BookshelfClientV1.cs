using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.Extensions;
using Requestrr.WebApi.RequestrrBot.Books;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf.BookshelfClient;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf
{
    /// <summary>
    /// Bookshelf (Readarr 0.4) v1 API client. Each call resolves the BookshelfCategory (one instance) from the request's CategoryId.
    /// Search uses GET /search (GET /book/lookup omits author/editions/id); availability comes from GET /book/{id} statistics.
    /// </summary>
    public class BookshelfClientV1 : IBookSearcher, IBookRequester
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BookshelfClient> _logger;
        private readonly BookshelfSettingsProvider _settingsProvider;

        public BookshelfClientV1(IHttpClientFactory httpClientFactory, ILogger<BookshelfClient> logger, BookshelfSettingsProvider settingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        // ---------- pure helpers (unit-tested) ----------

        public static string GetBaseURL(BookshelfCategory c) => $"{(c.UseSSL ? "https" : "http")}://{c.Hostname}:{c.Port}{c.BaseUrl}/api/v1";

        /// <summary>Converts a `book` object (from /search or GET /book/{id}) to a <see cref="Book"/>.</summary>
        public static Book ConvertToBook(JObject j)
        {
            int id = j.Value<int?>("id") ?? 0;
            var stats = j["statistics"] as JObject;
            bool available = stats != null && (stats.Value<int?>("bookFileCount") ?? 0) > 0;
            var cover = (j["images"] as JArray)?.FirstOrDefault(i => string.Equals((string)i["coverType"], "cover", StringComparison.OrdinalIgnoreCase));
            var coverUrl = (string)cover?["remoteUrl"];
            if (string.IsNullOrEmpty(coverUrl)) coverUrl = (string)cover?["url"];

            return new Book
            {
                DownloadClientId = id > 0 ? id.ToString() : string.Empty,
                BookId = (string)j["foreignBookId"],
                Title = (string)j["title"],
                AuthorName = (string)j["author"]?["authorName"] ?? string.Empty,
                ForeignAuthorId = (string)j["author"]?["foreignAuthorId"] ?? string.Empty,
                SeriesTitle = (string)j["seriesTitle"] ?? string.Empty,
                Year = ParseYear(j["releaseDate"]),
                Overview = (string)j["overview"] ?? string.Empty,
                Available = available,
                Monitored = j.Value<bool?>("monitored") ?? false,
                Requested = id > 0 && !available,
                PosterPath = coverUrl ?? string.Empty,
                TitleSlug = (string)j["titleSlug"] ?? string.Empty
            };
        }

        private static int? ParseYear(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type == JTokenType.Date) return token.Value<DateTime>().Year; // JObject.Parse yields UTC for "...Z"
            var s = (string)token;
            return s != null && s.Length >= 4 && int.TryParse(s.Substring(0, 4), out var y) ? y : (int?)null;
        }

        /// <summary>"Author · Year", omitting whichever half is unknown (disambiguates same-title books).</summary>
        public static string DescribeForSelect(Book b)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(b.AuthorName)) parts.Add(b.AuthorName);
            if (b.Year.HasValue) parts.Add(b.Year.Value.ToString());
            return string.Join(" · ", parts);
        }

        /// <summary>POST /book payload: the /search `book` object plus author profile fields and both addOptions blocks.</summary>
        public static JObject BuildAddBody(JObject searchBook, BookshelfCategory c)
        {
            var body = (JObject)searchBook.DeepClone();
            var foreignBookId = (string)body["foreignBookId"];
            RemoveIfNull(body, "id");

            body["monitored"] = c.MonitorNewRequests;
            body["tags"] = JArray.FromObject(c.Tags ?? Array.Empty<int>());
            body["addOptions"] = new JObject { ["searchForNewBook"] = c.SearchNewRequests };

            var author = body["author"] as JObject ?? new JObject();
            RemoveIfNull(author, "id");
            author["qualityProfileId"] = c.ProfileId;
            author["metadataProfileId"] = c.MetadataProfileId;
            author["rootFolderPath"] = c.RootFolder;
            author["monitored"] = true;
            author["monitorNewItems"] = "none";
            author["tags"] = JArray.FromObject(c.Tags ?? Array.Empty<int>());
            author["addOptions"] = new JObject
            {
                ["monitor"] = "none",
                ["booksToMonitor"] = new JArray(foreignBookId),
                ["searchForMissingBooks"] = false
            };
            body["author"] = author;
            return body;
        }

        private static void RemoveIfNull(JObject o, string name)
        {
            if (o.TryGetValue(name, out var v) && v.Type == JTokenType.Null) o.Remove(name);
        }

        // ---------- IBookSearcher ----------

        public async Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest request, string title)
        {
            try
            {
                var c = Category(request.CategoryId);
                var books = new List<Book>();
                foreach (var j in await SearchBookObjectsAsync(c, title.Trim()))
                    books.Add(await ConvertWithStatisticsAsync(c, j));
                return books.Where(b => !string.IsNullOrEmpty(b.BookId)).GroupBy(b => b.BookId).Select(g => g.First()).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred searching for a book \"{title}\" with Bookshelf: {ex.Message}");
            }

            throw new Exception("An error occurred while searching for a book with Bookshelf");
        }

        /// <summary>Returns null when no book matches (Lidarr convention); throws (after logging) when a request fails.</summary>
        public async Task<Book> SearchBookByIdAsync(BookRequest request, string bookId)
        {
            try
            {
                var c = Category(request.CategoryId);
                var j = await FindByForeignIdAsync(c, bookId, null);
                return j == null ? null : await ConvertWithStatisticsAsync(c, j);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred searching for book id \"{bookId}\" with Bookshelf: {ex.Message}");
            }

            throw new Exception("An error occurred while searching for a book by id with Bookshelf");
        }

        /// <summary>
        /// Per-id lookup (foreign book ids are not Bookshelf's internal ids, so there is no direct GET /book/{id}):
        /// resolve each id through search, then fetch statistics for library hits. Costs ~2 calls per polled id
        /// instead of downloading the whole library on every poll.
        /// </summary>
        public async Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int categoryId, HashSet<string> bookIds, CancellationToken token)
        {
            try
            {
                var c = Category(categoryId);
                var result = new Dictionary<string, Book>();
                foreach (var bookId in bookIds)
                {
                    if (token.IsCancellationRequested) break;
                    var j = await FindByForeignIdAsync(c, bookId, null);
                    if (j == null || (j.Value<int?>("id") ?? 0) <= 0) continue;
                    var book = await ConvertWithStatisticsAsync(c, j);
                    if (book.Available) result[bookId] = book;
                }
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred checking available books with Bookshelf: " + ex.Message);
            }

            throw new Exception("An error occurred while checking available books with Bookshelf");
        }

        // ---------- IBookRequester ----------

        public async Task<BookRequestResult> RequestBookAsync(BookRequest request, Book book)
        {
            var notFound = false;
            try
            {
                var c = Category(request.CategoryId);
                var searchBook = await FindByForeignIdAsync(c, book.BookId, book);
                if (searchBook == null)
                {
                    notFound = true;
                    throw new Exception($"Book {book.BookId} was not found in Bookshelf search");
                }
                int id = searchBook.Value<int?>("id") ?? 0;

                if (id > 0)
                {
                    // Already in the library: make sure it is monitored, then trigger a search if the category wants one.
                    var get = await HttpGetAsync(c, $"/book/{id}");
                    await get.ThrowIfNotSuccessfulAsync("BookshelfGetBook failed", x => x.error);
                    var existing = JObject.Parse(await get.Content.ReadAsStringAsync());
                    existing["monitored"] = true;
                    var put = await HttpSendAsync(c, HttpMethod.Put, $"/book/{id}", existing);
                    await put.ThrowIfNotSuccessfulAsync("BookshelfUpdateBook failed", x => x.error);

                    if (c.SearchNewRequests)
                    {
                        var cmd = await HttpSendAsync(c, HttpMethod.Post, "/command", new JObject { ["name"] = "BookSearch", ["bookIds"] = new JArray(id) });
                        await cmd.ThrowIfNotSuccessfulAsync("BookshelfBookSearch failed", x => x.error);
                    }
                }
                else
                {
                    var post = await HttpSendAsync(c, HttpMethod.Post, "/book", BuildAddBody(searchBook, c));
                    await post.ThrowIfNotSuccessfulAsync("BookshelfAddBook failed", x => x.error);
                }

                return new BookRequestResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"An error occurred requesting book \"{book.Title}\" from Bookshelf: {ex.Message}");
                if (notFound) throw new Exception("The book could not be found in Bookshelf search");
            }

            throw new Exception("An error occurred while requesting a book from Bookshelf");
        }

        // ---------- static admin helpers used by the settings controller ----------

        public static async Task TestConnectionAsync(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c)
        {
            if (!string.IsNullOrWhiteSpace(c.BaseUrl) && !c.BaseUrl.StartsWith("/"))
            {
                throw new Exception("Invalid base URL, must start with /");
            }

            try
            {
                var response = await HttpGetAsync(httpClient, c, "/system/status");

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new Exception("Invalid api key");
                }
                else if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new Exception("Incorrect api version");
                }
                else if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Invalid host and/or port");
                }

                var json = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (json["appName"] == null && json["version"] == null)
                {
                    throw new Exception("Not a Bookshelf/Readarr instance");
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "Error while testing Bookshelf connection: " + ex.Message);
                throw new Exception("Invalid host and/or port");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error while testing Bookshelf connection: " + ex.Message);

                if (ex.GetType() == typeof(Exception))
                {
                    throw;
                }

                throw new Exception("Invalid host and/or port");
            }
        }

        public static Task<IList<JSONRootPath>> GetRootPaths(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c)
            => GetListAsync<JSONRootPath>(httpClient, logger, c, "/rootfolder", "root paths");

        public static Task<IList<JSONProfile>> GetProfiles(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c)
            => GetListAsync<JSONProfile>(httpClient, logger, c, "/qualityprofile", "profiles");

        public static Task<IList<JSONProfile>> GetMetadataProfiles(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c)
            => GetListAsync<JSONProfile>(httpClient, logger, c, "/metadataprofile", "metadata profiles");

        public static Task<IList<JSONTag>> GetTags(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c)
            => GetListAsync<JSONTag>(httpClient, logger, c, "/tag", "tags");

        private static async Task<IList<T>> GetListAsync<T>(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory c, string path, string what)
        {
            try
            {
                var response = await HttpGetAsync(httpClient, c, path);
                return JsonConvert.DeserializeObject<IList<T>>(await response.Content.ReadAsStringAsync());
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, $"An error while getting Bookshelf {what}: " + ex.Message);
            }

            throw new Exception($"An error occurred while getting Bookshelf {what}");
        }

        // ---------- plumbing ----------

        private BookshelfCategory Category(int id)
            => _settingsProvider.Provide().Categories.SingleOrDefault(x => x.Id == id) ?? throw new Exception($"No Bookshelf category with id {id}");

        /// <summary>GET /search; keeps only entries carrying a `book` object (drops bare-author hits).</summary>
        private async Task<List<JObject>> SearchBookObjectsAsync(BookshelfCategory c, string term)
        {
            var response = await HttpGetAsync(c, $"/search?term={Uri.EscapeDataString(term)}");
            await response.ThrowIfNotSuccessfulAsync("BookshelfSearch failed", x => x.error);
            return JArray.Parse(await response.Content.ReadAsStringAsync())
                .OfType<JObject>()
                .Select(e => e["book"] as JObject)
                .Where(b => b != null)
                .ToList();
        }

        /// <summary>Finds the /search `book` object with this foreignBookId: tries the "work:" id lookup, then falls back to a title (+ author) search.</summary>
        private async Task<JObject> FindByForeignIdAsync(BookshelfCategory c, string foreignBookId, Book hint)
        {
            var terms = new List<string> { "work:" + foreignBookId };
            if (hint != null && !string.IsNullOrWhiteSpace(hint.Title))
            {
                terms.Add($"{hint.Title} {hint.AuthorName}".Trim());
                terms.Add(hint.Title);
            }

            // HTTP failures propagate (distinct from "no match"); only a clean empty/non-matching result moves to the next term.
            foreach (var term in terms)
            {
                var match = (await SearchBookObjectsAsync(c, term)).FirstOrDefault(b => (string)b["foreignBookId"] == foreignBookId);
                if (match != null) return match;
            }

            return null;
        }

        /// <summary>/search carries no statistics; for in-library books fetch GET /book/{id} to learn whether a file exists.</summary>
        private async Task<Book> ConvertWithStatisticsAsync(BookshelfCategory c, JObject searchBook)
        {
            var book = ConvertToBook(searchBook);
            if (string.IsNullOrEmpty(book.DownloadClientId)) return book;

            try
            {
                var response = await HttpGetAsync(c, $"/book/{book.DownloadClientId}");
                await response.ThrowIfNotSuccessfulAsync("BookshelfGetBook failed", x => x.error);
                return ConvertToBook(JObject.Parse(await response.Content.ReadAsStringAsync()));
            }
            catch (Exception ex)
            {
                // Degrade to "Requested" rather than failing the whole search.
                _logger.LogWarning(ex, $"Could not fetch Bookshelf book {book.DownloadClientId} statistics: {ex.Message}");
                return book;
            }
        }

        private Task<HttpResponseMessage> HttpGetAsync(BookshelfCategory c, string path)
            => HttpGetAsync(_httpClientFactory.CreateClient(), c, path);

        private static async Task<HttpResponseMessage> HttpGetAsync(HttpClient httpClient, BookshelfCategory c, string path)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, GetBaseURL(c) + path);
            request.Headers.Add("Accept", "application/json");
            request.Headers.Add("X-Api-Key", c.ApiKey);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            return await httpClient.SendAsync(request, cts.Token);
        }

        private async Task<HttpResponseMessage> HttpSendAsync(BookshelfCategory c, HttpMethod method, string path, JObject body)
        {
            var request = new HttpRequestMessage(method, GetBaseURL(c) + path)
            {
                Content = new StringContent(body.ToString(Formatting.None), System.Text.Encoding.UTF8, "application/json")
            };
            request.Headers.Add("X-Api-Key", c.ApiKey);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            return await _httpClientFactory.CreateClient().SendAsync(request, cts.Token);
        }
    }
}
