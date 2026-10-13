using Requestrr.WebApi.RequestrrBot.Books;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Books
{
    public interface IBookNotifier
    {
        Task<HashSet<string>> NotifyBookAsync(IReadOnlyCollection<string> userIds, Book book, CancellationToken token);
    }
}
