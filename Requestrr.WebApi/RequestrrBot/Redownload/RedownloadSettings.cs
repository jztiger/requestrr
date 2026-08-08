namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class RedownloadSettings
    {
        public bool Enabled { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public int RecentItemsCount { get; set; }
        public int HistoryFetchCount { get; set; }
        public int TimeoutMinutes { get; set; }
        public string AnimeRootFolder { get; set; } = string.Empty;
        public string AnimeSnarkUserId { get; set; } = string.Empty;
        public string AnimeSkipMessage { get; set; } = string.Empty;
    }
}
