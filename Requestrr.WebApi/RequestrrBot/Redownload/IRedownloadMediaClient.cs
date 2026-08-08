using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public interface IRedownloadMediaClient
    {
        Task<string> GetRecycleBinPathAsync();                       // "" when unset → caller gates
        Task<MediaFileInfo> GetCurrentFileAsync(RedownloadTarget t); // null when no file
        Task EnsureMonitoredAsync(RedownloadTarget t);
        Task<long?> GetLastGrabHistoryIdAsync(RedownloadTarget t);   // null when history pruned
        Task DeleteFileAsync(int fileId);
        Task MarkHistoryFailedAsync(long historyId);                 // POST /history/failed/{id}
        Task TriggerSearchAsync(RedownloadTarget t);
        // restore: scan bin, match originalFileName (basename), import. Returns false if no candidate matched.
        Task<bool> RestoreFromRecycleBinAsync(RedownloadTarget t, string originalFileName);
    }
}
