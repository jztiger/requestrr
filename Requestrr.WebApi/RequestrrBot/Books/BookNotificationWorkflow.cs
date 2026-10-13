using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookNotificationWorkflow : IBookNotificationWorkflow
    {
        private readonly BookNotificationsRepository _repo;
        private readonly IBookUserInterface _ui;
        private readonly IBookSearcher _searcher;
        private readonly bool _auto;

        public BookNotificationWorkflow(BookNotificationsRepository repo, IBookUserInterface ui, IBookSearcher searcher, bool automaticNotificationForNewRequests)
        {
            _repo = repo;
            _ui = ui;
            _searcher = searcher;
            _auto = automaticNotificationForNewRequests;
        }

        public Task NotifyForNewRequestAsync(string userId, BookRequest request, Book book)
        {
            if (_auto)
                _repo.AddNotification(userId, request.CategoryId, book.BookId);

            return Task.CompletedTask;
        }

        public async Task NotifyForExistingRequestAsync(string userId, Book book)
        {
            // category is unknown here; check across all categories
            if (_repo.HasNotificationForBook(userId, book.BookId))
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
