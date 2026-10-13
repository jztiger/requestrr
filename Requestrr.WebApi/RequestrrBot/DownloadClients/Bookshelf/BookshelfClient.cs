using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Books;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf
{
    /// <summary>Thin facade over <see cref="BookshelfClientV1"/> (same shape as LidarrClient).</summary>
    public class BookshelfClient : IBookSearcher, IBookRequester
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BookshelfClient> _logger;
        private readonly BookshelfSettingsProvider _settingsProvider;

        public BookshelfClient(IHttpClientFactory httpClientFactory, ILogger<BookshelfClient> logger, BookshelfSettingsProvider settingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _settingsProvider = settingsProvider;
        }

        /// <summary>Used to test a connection to a Bookshelf instance (one category).</summary>
        public static Task TestConnectionAsync(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory category)
            => BookshelfClientV1.TestConnectionAsync(httpClient, logger, category);

        /// <summary>Returns all root folders configured in the instance.</summary>
        public static Task<IList<JSONRootPath>> GetRootPaths(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory category)
            => BookshelfClientV1.GetRootPaths(httpClient, logger, category);

        /// <summary>Returns all quality profiles configured in the instance.</summary>
        public static Task<IList<JSONProfile>> GetProfiles(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory category)
            => BookshelfClientV1.GetProfiles(httpClient, logger, category);

        /// <summary>Returns all metadata profiles configured in the instance.</summary>
        public static Task<IList<JSONProfile>> GetMetadataProfiles(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory category)
            => BookshelfClientV1.GetMetadataProfiles(httpClient, logger, category);

        /// <summary>Returns all tags configured in the instance.</summary>
        public static Task<IList<JSONTag>> GetTags(HttpClient httpClient, ILogger<BookshelfClient> logger, BookshelfCategory category)
            => BookshelfClientV1.GetTags(httpClient, logger, category);

        public Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest request, string title)
            => CreateInstance().SearchBookAsync(request, title);

        public Task<Book> SearchBookByIdAsync(BookRequest request, string bookId)
            => CreateInstance().SearchBookByIdAsync(request, bookId);

        public Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int categoryId, HashSet<string> bookIds, CancellationToken token)
            => CreateInstance().SearchAvailableBooksAsync(categoryId, bookIds, token);

        public Task<BookRequestResult> RequestBookAsync(BookRequest request, Book book)
            => CreateInstance().RequestBookAsync(request, book);

        private BookshelfClientV1 CreateInstance() => new BookshelfClientV1(_httpClientFactory, _logger, _settingsProvider);

        public class JSONRootPath
        {
            public string path { get; set; }
            public int id { get; set; }
        }

        public class JSONProfile
        {
            public string name { get; set; }
            public int id { get; set; }
        }

        public class JSONTag
        {
            public string label { get; set; }
            public int id { get; set; }
        }
    }
}
