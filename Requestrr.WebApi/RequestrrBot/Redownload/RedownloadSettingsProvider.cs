using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class RedownloadSettingsProvider
    {
        public RedownloadSettings Provide()
        {
            try
            {
                dynamic settings = SettingsFile.Read();
                JObject redownloadSection = settings?.Redownload;

                if (redownloadSection == null)
                {
                    return new RedownloadSettings();
                }

                return new RedownloadSettings
                {
                    Enabled = (bool?)redownloadSection["Enabled"] ?? false,
                    CommandName = (string)redownloadSection["CommandName"] ?? string.Empty,
                    RecentItemsCount = (int?)redownloadSection["RecentItemsCount"] ?? 0,
                    HistoryFetchCount = (int?)redownloadSection["HistoryFetchCount"] ?? 0,
                    TimeoutMinutes = (int?)redownloadSection["TimeoutMinutes"] ?? 0,
                    AnimeRootFolder = (string)redownloadSection["AnimeRootFolder"] ?? string.Empty,
                    AnimeSnarkUserId = (string)redownloadSection["AnimeSnarkUserId"] ?? string.Empty,
                    AnimeSkipMessage = (string)redownloadSection["AnimeSkipMessage"] ?? string.Empty,
                };
            }
            catch
            {
                return new RedownloadSettings();
            }
        }
    }
}
