namespace Requestrr.WebApi.Controllers.Redownload
{
    /// <summary>
    /// Flat model backing the Redownload settings page. Combines the Tautulli connection
    /// fields with the Redownload command/anime settings into a single payload.
    /// </summary>
    public class RedownloadSettingsModel
    {
        // Tautulli
        public string TautulliHostname { get; set; } = string.Empty;
        public int TautulliPort { get; set; }
        public string TautulliApiKey { get; set; } = string.Empty;
        public bool TautulliUseSSL { get; set; }

        // Redownload
        public bool Enabled { get; set; }
        public string CommandName { get; set; } = string.Empty;
        public int RecentItemsCount { get; set; }
        public int HistoryFetchCount { get; set; }
        public int TimeoutMinutes { get; set; }
        public string AnimeRootFolder { get; set; } = string.Empty;
        public string AnimeSnarkUserId { get; set; } = string.Empty;
        public string AnimeSkipMessage { get; set; } = string.Empty;
    }



    /// <summary>
    /// Payload for testing a Tautulli connection without persisting it.
    /// </summary>
    public class TestTautulliSettingsModel
    {
        public string Hostname { get; set; } = string.Empty;
        public int Port { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public bool UseSSL { get; set; }
    }
}
