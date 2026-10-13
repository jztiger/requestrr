using System.Collections.Generic;
using System.Linq;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Books
{
    public interface IBookNotificationsRepository
    {
        void AddNotification(string userId, int categoryId, string bookId);
        bool HasNotification(string userId, int categoryId, string bookId);
    }

    public class BookNotificationsRepository : IBookNotificationsRepository
    {
        private readonly HashSet<(string userId, int categoryId, string bookId)> _n = new HashSet<(string, int, string)>();
        private readonly object _lock = new object();

        public BookNotificationsRepository()
        {
            foreach (var entry in NotificationsFile.ReadBooks())
            {
                foreach (string key in entry.Value)
                {
                    var i = key.IndexOf(':');
                    if (i < 1 || !int.TryParse(key.Substring(0, i), out var categoryId))
                        continue;

                    _n.Add((entry.Key, categoryId, key.Substring(i + 1)));
                }
            }
        }

        public void AddNotification(string userId, int categoryId, string bookId)
        {
            lock (_lock)
                if (_n.Add((userId, categoryId, bookId)))
                    Persist();
        }

        public void RemoveNotification(string userId, int categoryId, string bookId)
        {
            lock (_lock)
                if (_n.Remove((userId, categoryId, bookId)))
                    Persist();
        }

        public bool HasNotification(string userId, int categoryId, string bookId)
        {
            lock (_lock)
                return _n.Contains((userId, categoryId, bookId));
        }

        public bool HasNotificationForBook(string userId, string bookId)
        {
            lock (_lock)
                return _n.Any(x => x.userId == userId && x.bookId == bookId);
        }

        /// <summary>(categoryId, bookId) -> users waiting.</summary>
        public Dictionary<(int categoryId, string bookId), HashSet<string>> GetAllBookNotifications()
        {
            lock (_lock)
                return _n.GroupBy(x => (x.categoryId, x.bookId))
                    .ToDictionary(g => g.Key, g => new HashSet<string>(g.Select(x => x.userId)));
        }

        private void Persist()
        {
            NotificationsFile.WriteBooks(_n.GroupBy(x => x.userId)
                .ToDictionary(g => g.Key, g => g.Select(x => $"{x.categoryId}:{x.bookId}").ToArray()));
        }
    }
}
