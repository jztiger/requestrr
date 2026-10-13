using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookNotificationWorkflow : IBookNotificationWorkflow
    {
        private readonly IBookNotificationsRepository _repo;
        private readonly int _categoryId;
        private readonly IBookUserInterface _ui;
        private readonly IBookSearcher _searcher;
        private readonly bool _auto;

        public BookNotificationWorkflow(IBookNotificationsRepository repo, IBookUserInterface ui, IBookSearcher searcher, bool automaticNotificationForNewRequests, int categoryId = -1)
        {
            _repo = repo;
            _ui = ui;
            _searcher = searcher;
            _auto = automaticNotificationForNewRequests;
            _categoryId = categoryId;
        }

        public Task NotifyForNewRequestAsync(string userId, BookRequest request, Book book)
        {
            if (_auto)
                _repo.AddNotification(userId, request.CategoryId, book.BookId);

            return Task.CompletedTask;
        }

        public async Task NotifyForExistingRequestAsync(string userId, Book book)
        {
            // audio and ebook instances share foreign book ids, so the check must be per category
            if (_repo.HasNotification(userId, _categoryId, book.BookId))
                await _ui.WarnBookUnavailableAndAlreadyHasNotificationAsync(book);
            else
                await _ui.AskForNotificationBookRequestAsync(book);
        }

        public async Task AddNotificationBookAsync(string userId, int categoryId, string bookId)
        {
            var book = await _searcher.SearchBookByIdAsync(new BookRequest(null, categoryId), bookId);

            if (book == null)
            {
                await _ui.WarnNoBookFoundAsync(bookId);
                return;
            }

            _repo.AddNotification(userId, categoryId, book.BookId);
            await _ui.DisplayNotificationBookSuccessAsync(book);
        }
    }
}
