using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Books;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Books
{
    public class BookNotificationEngine
    {
        private object _lock = new object();
        private readonly IBookSearcher _searcher;
        private readonly IBookNotifier _notifier;
        private readonly ILogger _logger;
        private readonly BookNotificationsRepository _notificationsRepository;
        private Task _notificationTask = null;
        private CancellationTokenSource _tokenSource = new CancellationTokenSource();

        public BookNotificationEngine(
            IBookSearcher bookSearcher,
            IBookNotifier notifier,
            ILogger logger,
            BookNotificationsRepository bookNotificationsRepository
        )
        {
            _searcher = bookSearcher;
            _notifier = notifier;
            _logger = logger;
            _notificationsRepository = bookNotificationsRepository;
        }


        public void Start()
        {
            _notificationTask = Task.Run(async () =>
            {
                while (!_tokenSource.IsCancellationRequested)
                {
                    try
                    {
                        var current = _notificationsRepository.GetAllBookNotifications();
                        foreach (var catGroup in current.GroupBy(x => x.Key.categoryId))
                        {
                            var ids = new HashSet<string>(catGroup.Select(x => x.Key.bookId));
                            var available = await _searcher.SearchAvailableBooksAsync(catGroup.Key, ids, _tokenSource.Token);

                            foreach (var kv in catGroup.Where(x => available.ContainsKey(x.Key.bookId)))
                            {
                                if (_tokenSource.IsCancellationRequested)
                                    return;

                                try
                                {
                                    var notified = await _notifier.NotifyBookAsync(kv.Value.ToArray(), available[kv.Key.bookId], _tokenSource.Token);
                                    foreach (var u in notified)
                                        _notificationsRepository.RemoveNotification(u, kv.Key.categoryId, kv.Key.bookId);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "An error occurred while processing book notifications: " + ex.Message);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "An error occurred while retrieving all book notifications: " + ex.Message);
                    }

                    await Task.Delay(TimeSpan.FromMinutes(1), _tokenSource.Token);
                }
            }, _tokenSource.Token);
        }


        public async Task StopAsync()
        {
            try
            {
                _tokenSource.Cancel();
                await _notificationTask;
            }
            catch
            {
                _tokenSource.Dispose();
                _tokenSource = new CancellationTokenSource();
            }
        }
    }
}
