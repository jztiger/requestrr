using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookNotificationEngineTests
    {
        private class ThrowingSearcher : IBookSearcher
        {
            public List<int> Queried = new List<int>();
            public Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest r, string t) => throw new NotSupportedException();
            public Task<Book> SearchBookByIdAsync(BookRequest r, string id) => throw new NotSupportedException();
            public Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int c, HashSet<string> ids, CancellationToken t)
            {
                Queried.Add(c);
                if (c == 0) throw new InvalidOperationException("bookshelf down");
                return Task.FromResult(new Dictionary<string, Book> { ["b1"] = new Book { BookId = "b1", Title = "T" } });
            }
        }

        private class FakeNotifier : IBookNotifier
        {
            public List<string> Notified = new List<string>();
            public Task<HashSet<string>> NotifyBookAsync(IReadOnlyCollection<string> userIds, Book book, int categoryId, CancellationToken token)
            {
                foreach (var u in userIds) Notified.Add(u + ":" + book.BookId);
                return Task.FromResult(new HashSet<string>(userIds));
            }
        }

        private class CapturingLogger : ILogger
        {
            public List<string> Errors = new List<string>();
            public IDisposable BeginScope<TState>(TState state) => null;
            public bool IsEnabled(LogLevel l) => true;
            public void Log<TState>(LogLevel l, EventId e, TState s, Exception ex, Func<TState, Exception, string> f)
            { if (l == LogLevel.Error) Errors.Add(f(s, ex)); }
        }

        [Fact]
        public async Task FailingCategory_DoesNotStarveLaterCategories()
        {
            var searcher = new ThrowingSearcher(); var notifier = new FakeNotifier(); var logger = new CapturingLogger();
            var engine = new BookNotificationEngine(searcher, notifier, logger, null);
            var current = new Dictionary<(int categoryId, string bookId), HashSet<string>>
            {
                [(0, "b0")] = new HashSet<string> { "u0" },
                [(1, "b1")] = new HashSet<string> { "u1" },
            };
            var removed = new List<string>();

            await engine.ProcessNotificationsAsync(current, (u, c, b) => removed.Add($"{u}:{c}:{b}"));

            Assert.Equal(new[] { 0, 1 }, searcher.Queried);
            Assert.Equal(new[] { "u1:b1" }, notifier.Notified);
            Assert.Equal(new[] { "u1:1:b1" }, removed);
            Assert.Contains(logger.Errors, e => e.Contains("category 0"));
        }
    }
}
