using System.Linq;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookRequestingWorkflow
    {
        private readonly int _categoryId;
        private readonly BookUserRequester _user;
        private readonly IBookSearcher _searcher;
        private readonly IBookRequester _requester;
        private readonly IBookUserInterface _ui;
        private readonly IBookNotificationWorkflow _notify;

        public BookRequestingWorkflow(BookUserRequester user, int categoryId, IBookSearcher searcher, IBookRequester requester, IBookUserInterface ui, IBookNotificationWorkflow notify)
        {
            _user = user;
            _categoryId = categoryId;
            _searcher = searcher;
            _requester = requester;
            _ui = ui;
            _notify = notify;
        }

        private BookRequest Req() => new BookRequest(_user, _categoryId);

        public async Task SearchBookAsync(string title)
        {
            title = title.Replace(".", " ").Trim();
            var books = await _searcher.SearchBookAsync(Req(), title);

            if (!books.Any())
                await _ui.WarnNoBookFoundAsync(title);
            else if (books.Count > 1)
                await _ui.ShowBookSelection(Req(), books);
            else
                await HandleSelectionAsync(books.Single(), title);
        }

        public async Task HandleBookSelectionAsync(string bookId)
        {
            await HandleSelectionAsync(await _searcher.SearchBookByIdAsync(Req(), bookId), bookId);
        }

        private async Task HandleSelectionAsync(Book book, string lookup)
        {
            if (book == null)
                await _ui.WarnNoBookFoundAsync(lookup);
            else if (book.Available)
                await _ui.WarnBookAlreadyAvailableAsync(book);
            else if (book.Requested)
                await _notify.NotifyForExistingRequestAsync(_user.UserId, book);
            else
                await _ui.DisplayBookDetailsAsync(Req(), book);
        }

        public async Task RequestBookAsync(string bookId)
        {
            var book = await _searcher.SearchBookByIdAsync(Req(), bookId);

            if (book == null)
            {
                await _ui.WarnNoBookFoundAsync(bookId);
                return;
            }

            var result = await _requester.RequestBookAsync(Req(), book);

            if (result.WasDenied)
            {
                await _ui.DisplayBookRequestDeniedAsync(book);
                return;
            }

            await _ui.DisplayBookRequestSuccessAsync(book);
            await _notify.NotifyForNewRequestAsync(_user.UserId, Req(), book);
        }
    }
}
