using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    // Discord impl in Task 10; tests use a recording fake.
    public interface IRedownloadMessenger
    {
        Task<(ulong channelId, ulong messageId)> PostPublicAsync(ulong channelId, string content);
        Task EditPublicAsync(ulong channelId, ulong messageId, string content);
    }
}
