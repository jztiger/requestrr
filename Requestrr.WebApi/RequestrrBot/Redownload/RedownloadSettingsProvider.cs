using System;
using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class RedownloadSettingsProvider
    {
        // Spec defaults -- copied exactly from SettingsTemplate.json's "Redownload" section (the
        // source new settings.json files are seeded with, and what SettingsFileUpgrader writes on
        // a 2.1.3 -> 2.1.4 upgrade). Applied both when the whole section is missing/unreadable AND
        // per-field, since a hand-edited settings.json can null/blank out an individual key without
        // dropping the section itself. S-1 closes I-1's fail-open gap at the read layer: a blank
        // AnimeRootFolder previously sailed straight through into RedownloadResolver.IsAnime, whose
        // path-based OR-rule silently no-ops on an empty root -- see IsAnime in RedownloadResolver.cs.
        private const bool DefaultEnabled = false;
        private const string DefaultCommandName = "redownload";
        private const int DefaultRecentItemsCount = 5;
        private const int DefaultHistoryFetchCount = 25;
        private const int DefaultTimeoutMinutes = 45;
        private const string DefaultAnimeRootFolder = "/data/plexmedia/Anime";
        private const string DefaultAnimeSnarkUserId = "";
        private const string DefaultAnimeSkipMessage = "🍥 Skipped {count} anime title(s) — /redownload doesn't do anime: dual audio, a dozen fansub groups, and absolute numbering make \"grab a different one\" a coin flip. That means you, {user} 😉";

        public RedownloadSettings Provide()
        {
            try
            {
                dynamic settings = SettingsFile.Read();
                JObject redownloadSection = settings?.Redownload;

                if (redownloadSection == null)
                {
                    return Defaults();
                }

                var result = new RedownloadSettings
                {
                    Enabled = (bool?)redownloadSection["Enabled"] ?? DefaultEnabled,
                    CommandName = (string)redownloadSection["CommandName"] ?? DefaultCommandName,
                    RecentItemsCount = (int?)redownloadSection["RecentItemsCount"] ?? DefaultRecentItemsCount,
                    HistoryFetchCount = (int?)redownloadSection["HistoryFetchCount"] ?? DefaultHistoryFetchCount,
                    TimeoutMinutes = (int?)redownloadSection["TimeoutMinutes"] ?? DefaultTimeoutMinutes,
                    AnimeRootFolder = (string)redownloadSection["AnimeRootFolder"] ?? DefaultAnimeRootFolder,
                    AnimeSnarkUserId = (string)redownloadSection["AnimeSnarkUserId"] ?? DefaultAnimeSnarkUserId,
                    AnimeSkipMessage = (string)redownloadSection["AnimeSkipMessage"] ?? DefaultAnimeSkipMessage,
                };

                return Clamp(result);
            }
            catch
            {
                return Defaults();
            }
        }

        private static RedownloadSettings Defaults()
        {
            return new RedownloadSettings
            {
                Enabled = DefaultEnabled,
                CommandName = DefaultCommandName,
                RecentItemsCount = DefaultRecentItemsCount,
                HistoryFetchCount = DefaultHistoryFetchCount,
                TimeoutMinutes = DefaultTimeoutMinutes,
                AnimeRootFolder = DefaultAnimeRootFolder,
                AnimeSnarkUserId = DefaultAnimeSnarkUserId,
                AnimeSkipMessage = DefaultAnimeSkipMessage,
            };
        }

        // Clamps numeric fields read from a hand-edited settings.json back into the same ranges
        // RedownloadController.SaveAsync already enforces on the write path -- this is the
        // read-path backstop for files that bypassed that endpoint (or were written by an older
        // build without the bound). Blank AnimeRootFolder is folded in here too, for the same
        // fail-open reason described above.
        private static RedownloadSettings Clamp(RedownloadSettings settings)
        {
            settings.RecentItemsCount = Math.Clamp(settings.RecentItemsCount, 1, 25);
            settings.HistoryFetchCount = Math.Clamp(settings.HistoryFetchCount, 1, 200);
            settings.TimeoutMinutes = Math.Clamp(settings.TimeoutMinutes, 1, 1440);

            if (string.IsNullOrWhiteSpace(settings.AnimeRootFolder))
            {
                settings.AnimeRootFolder = DefaultAnimeRootFolder;
            }

            return settings;
        }
    }
}
