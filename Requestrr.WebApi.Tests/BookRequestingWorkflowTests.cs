using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.Books;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookRequestingWorkflowTests
    {
        private class FakeSearcher : IBookSearcher
        {
            public List<Book> Results = new List<Book>();
            public Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest r, string t) => Task.FromResult<IReadOnlyList<Book>>(Results);
            public Task<Book> SearchBookByIdAsync(BookRequest r, string id) => Task.FromResult(Results.Find(b => b.BookId == id));
            public Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int c, HashSet<string> ids, CancellationToken t) => Task.FromResult(new Dictionary<string, Book>());
        }
        private class FakeRequester : IBookRequester
        {
            public bool Deny; public Book Requested;
            public Task<BookRequestResult> RequestBookAsync(BookRequest r, Book b) { Requested = b; return Task.FromResult(new BookRequestResult { WasDenied = Deny }); }
        }
        private class FakeUi : IBookUserInterface
        {
            public List<string> Calls = new List<string>();
            Task R(string s) { Calls.Add(s); return Task.CompletedTask; }
            public Task ShowBookSelection(BookRequest r, IReadOnlyList<Book> b) => R($"select:{b.Count}");
            public Task WarnNoBookFoundAsync(string t) => R("nofound");
            public Task DisplayBookDetailsAsync(BookRequest r, Book b) => R("details");
            public Task DisplayBookRequestDeniedAsync(Book b) => R("denied");
            public Task DisplayBookRequestSuccessAsync(Book b) => R("success");
            public Task WarnBookAlreadyAvailableAsync(Book b) => R("available");
            public Task WarnBookUnavailableAndAlreadyHasNotificationAsync(Book b) => R("alreadynotified");
            public Task AskForNotificationBookRequestAsync(Book b) => R("asknotify");
            public Task DisplayNotificationBookSuccessAsync(Book b) => R("notifysuccess");
        }
        private class FakeNotify : IBookNotificationWorkflow
        {
            public List<string> Calls = new List<string>();
            public Task NotifyForNewRequestAsync(string u, BookRequest r, Book b) { Calls.Add("new:" + b.BookId); return Task.CompletedTask; }
            public Task NotifyForExistingRequestAsync(string u, Book b) { Calls.Add("existing:" + b.BookId); return Task.CompletedTask; }
            public Task AddNotificationBookAsync(string u, int c, string id) { Calls.Add("add:" + id); return Task.CompletedTask; }
        }

        private static (BookRequestingWorkflow wf, FakeSearcher s, FakeRequester rq, FakeUi ui, FakeNotify n) Make()
        {
            var s = new FakeSearcher(); var rq = new FakeRequester(); var ui = new FakeUi(); var n = new FakeNotify();
            return (new BookRequestingWorkflow(new BookUserRequester("1", "u"), 0, s, rq, ui, n), s, rq, ui, n);
        }

        [Fact] // Review Focus 5
        public async Task Search_NoResults_WarnsOnly()
        {
            var (wf, s, rq, ui, n) = Make();
            await wf.SearchBookAsync("zzz");
            Assert.Equal(new[] { "nofound" }, ui.Calls);
        }

        [Fact]
        public async Task Search_ManyResults_ShowsSelection()
        {
            var (wf, s, rq, ui, n) = Make();
            s.Results.AddRange(new[] { new Book { BookId = "a", Title = "A" }, new Book { BookId = "b", Title = "B" } });
            await wf.SearchBookAsync("x");
            Assert.Equal(new[] { "select:2" }, ui.Calls);
        }

        [Fact]
        public async Task Search_SingleRequestable_ShowsDetails()
        {
            var (wf, s, rq, ui, n) = Make();
            s.Results.Add(new Book { BookId = "a", Title = "A" });
            await wf.SearchBookAsync("x");
            Assert.Equal(new[] { "details" }, ui.Calls);
        }

        [Fact] // Review Focus 2
        public async Task Selection_Available_WarnsAvailable()
        {
            var (wf, s, rq, ui, n) = Make();
            s.Results.Add(new Book { BookId = "a", Available = true });
            await wf.HandleBookSelectionAsync("a");
            Assert.Equal(new[] { "available" }, ui.Calls); Assert.Empty(n.Calls);
        }

        [Fact] // Review Focus 3
        public async Task Selection_RequestedNotAvailable_RoutesToExistingNotification()
        {
            var (wf, s, rq, ui, n) = Make();
            s.Results.Add(new Book { BookId = "a", Requested = true });
            await wf.HandleBookSelectionAsync("a");
            Assert.Empty(ui.Calls); Assert.Equal(new[] { "existing:a" }, n.Calls);
        }

        [Fact]
        public async Task Request_Success_NotifiesNew()
        {
            var (wf, s, rq, ui, n) = Make();
            s.Results.Add(new Book { BookId = "a" });
            await wf.RequestBookAsync("a");
            Assert.Equal("a", rq.Requested.BookId);
            Assert.Equal(new[] { "success" }, ui.Calls); Assert.Equal(new[] { "new:a" }, n.Calls);
        }

        [Fact]
        public async Task Request_Denied_ShowsDenied()
        {
            var (wf, s, rq, ui, n) = Make(); rq.Deny = true;
            s.Results.Add(new Book { BookId = "a" });
            await wf.RequestBookAsync("a");
            Assert.Equal(new[] { "denied" }, ui.Calls); Assert.Empty(n.Calls);
        }

        [Fact]
        public async Task Selection_NotFound_WarnsNoBookFound()
        {
            var (wf, s, rq, ui, n) = Make();
            await wf.HandleBookSelectionAsync("missing");
            Assert.Equal(new[] { "nofound" }, ui.Calls); Assert.Empty(n.Calls);
        }

        [Fact]
        public async Task Request_NotFound_WarnsAndDoesNotRequest()
        {
            var (wf, s, rq, ui, n) = Make();
            await wf.RequestBookAsync("missing");
            Assert.Null(rq.Requested);
            Assert.Equal(new[] { "nofound" }, ui.Calls); Assert.Empty(n.Calls);
        }
    }
}
