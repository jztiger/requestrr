using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookNotificationWorkflowTests
    {
        private class FakeRepo : IBookNotificationsRepository
        {
            public HashSet<(string, int, string)> Items = new HashSet<(string, int, string)>();
            public void AddNotification(string u, int c, string b) => Items.Add((u, c, b));
            public bool HasNotification(string u, int c, string b) => Items.Contains((u, c, b));
        }
        private class FakeUi : IBookUserInterface
        {
            public List<string> Calls = new List<string>();
            Task R(string s) { Calls.Add(s); return Task.CompletedTask; }
            public Task ShowBookSelection(BookRequest r, IReadOnlyList<Book> b) => R("select");
            public Task WarnNoBookFoundAsync(string t) => R("nofound");
            public Task DisplayBookDetailsAsync(BookRequest r, Book b) => R("details");
            public Task DisplayBookRequestDeniedAsync(Book b) => R("denied");
            public Task DisplayBookRequestSuccessAsync(Book b) => R("success");
            public Task WarnBookAlreadyAvailableAsync(Book b) => R("available");
            public Task WarnBookUnavailableAndAlreadyHasNotificationAsync(Book b) => R("alreadynotified");
            public Task AskForNotificationBookRequestAsync(Book b) => R("asknotify");
            public Task DisplayNotificationBookSuccessAsync(Book b) => R("notifysuccess");
        }
        private class NoSearcher : IBookSearcher
        {
            public Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest r, string t) => Task.FromResult<IReadOnlyList<Book>>(new List<Book>());
            public Task<Book> SearchBookByIdAsync(BookRequest r, string id) => Task.FromResult<Book>(null);
            public Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int c, HashSet<string> ids, CancellationToken t) => Task.FromResult(new Dictionary<string, Book>());
        }

        [Fact]
        public async Task ExistingRequest_NotificationInOtherCategory_OffersNotifyMe()
        {
            var repo = new FakeRepo(); repo.Items.Add(("u1", 0, "X"));
            var ui = new FakeUi();
            var wf = new BookNotificationWorkflow(repo, ui, new NoSearcher(), false, 1);
            await wf.NotifyForExistingRequestAsync("u1", new Book { BookId = "X" });
            Assert.Equal(new[] { "asknotify" }, ui.Calls);
        }

        [Fact]
        public async Task ExistingRequest_NotificationInSameCategory_WarnsAlreadyNotified()
        {
            var repo = new FakeRepo(); repo.Items.Add(("u1", 1, "X"));
            var ui = new FakeUi();
            var wf = new BookNotificationWorkflow(repo, ui, new NoSearcher(), false, 1);
            await wf.NotifyForExistingRequestAsync("u1", new Book { BookId = "X" });
            Assert.Equal(new[] { "alreadynotified" }, ui.Calls);
        }
    }
}
