using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class SettingsUpgraderTests : IDisposable
    {
        private readonly string _tempPath;

        private const string AnimeSkipMessage = "🍥 Skipped {count} anime title(s) — /redownload doesn't do anime: dual audio, a dozen fansub groups, and absolute numbering make \"grab a different one\" a coin flip. That means you, {user} 😉";

        private const string Settings213Json = @"
        {
          ""DownloadClients"": {
            ""Radarr"": { ""Hostname"": """", ""Port"": 7878 },
            ""Sonarr"": { ""Hostname"": """", ""Port"": 8989 }
          },
          ""Version"": ""2.1.3""
        }";

        public SettingsUpgraderTests()
        {
            _tempPath = Path.GetTempFileName();
        }

        public void Dispose()
        {
            if (File.Exists(_tempPath))
                File.Delete(_tempPath);
        }

        [Fact]
        public void Upgrades_213_settings_to_214_with_tautulli_and_redownload_defaults()
        {
            File.WriteAllText(_tempPath, Settings213Json);

            SettingsFileUpgrader.Upgrade(_tempPath);

            var result = JObject.Parse(File.ReadAllText(_tempPath));

            Assert.Equal("2.1.4", (string)result["Version"]);

            var tautulli = (JObject)result["DownloadClients"]["Tautulli"];
            Assert.NotNull(tautulli);
            Assert.Equal(string.Empty, (string)tautulli["Hostname"]);
            Assert.Equal(8181, (int)tautulli["Port"]);
            Assert.Equal(string.Empty, (string)tautulli["ApiKey"]);
            Assert.False((bool)tautulli["UseSSL"]);

            var redownload = (JObject)result["Redownload"];
            Assert.NotNull(redownload);
            Assert.False((bool)redownload["Enabled"]);
            Assert.Equal("redownload", (string)redownload["CommandName"]);
            Assert.Equal(5, (int)redownload["RecentItemsCount"]);
            Assert.Equal(25, (int)redownload["HistoryFetchCount"]);
            Assert.Equal(45, (int)redownload["TimeoutMinutes"]);
            Assert.Equal("/data/plexmedia/Anime", (string)redownload["AnimeRootFolder"]);
            Assert.Equal(string.Empty, (string)redownload["AnimeSnarkUserId"]);
            Assert.Equal(AnimeSkipMessage, (string)redownload["AnimeSkipMessage"]);
        }

        [Fact]
        public void Upgrade_is_idempotent_once_already_at_214()
        {
            File.WriteAllText(_tempPath, Settings213Json);
            SettingsFileUpgrader.Upgrade(_tempPath);
            var afterFirstUpgrade = File.ReadAllText(_tempPath);

            SettingsFileUpgrader.Upgrade(_tempPath);
            var afterSecondUpgrade = File.ReadAllText(_tempPath);

            Assert.Equal(afterFirstUpgrade, afterSecondUpgrade);
        }

        [Fact]
        public void RedownloadSettingsProvider_returns_full_spec_defaults_when_settings_file_unreadable()
        {
            // No settings.json exists at SettingsFile's configured path in the test environment,
            // so Read() throws and the provider's catch-all fallback is exercised. S-1: this must
            // return the FULL spec default set (matching SettingsTemplate.json), not just a
            // disabled flag with zeroed-out numeric fields.
            var provider = new RedownloadSettingsProvider();

            var settings = provider.Provide();

            AssertSpecDefaults(settings);
        }

        [Fact]
        public void RedownloadSettingsProvider_returns_full_spec_defaults_when_redownload_section_missing()
        {
            // Directly exercises the redownloadSection == null branch (settings file present,
            // but no top-level "Redownload" key) rather than the exception fallback above.
            SettingsFile._cachedSettings = JObject.Parse(@"{ ""Version"": ""2.1.4"", ""DownloadClients"": {} }");
            try
            {
                var provider = new RedownloadSettingsProvider();

                var settings = provider.Provide();

                AssertSpecDefaults(settings);
            }
            finally
            {
                SettingsFile._cachedSettings = null;
            }
        }

        [Fact]
        public void RedownloadSettingsProvider_clamps_out_of_range_values_in_section()
        {
            // S-1: a hand-edited settings.json (or one written by an older build without the
            // controller's bound checks) can carry out-of-range numeric fields. The read path is
            // the last backstop before those values reach RedownloadResolver/RedownloadEngine.
            SettingsFile._cachedSettings = JObject.Parse(@"
            {
              ""Redownload"": {
                ""Enabled"": true,
                ""CommandName"": ""redownload"",
                ""RecentItemsCount"": 999,
                ""HistoryFetchCount"": 0,
                ""TimeoutMinutes"": -5,
                ""AnimeRootFolder"": ""/data/plexmedia/Anime"",
                ""AnimeSnarkUserId"": ""123"",
                ""AnimeSkipMessage"": ""hi""
              }
            }");
            try
            {
                var provider = new RedownloadSettingsProvider();

                var settings = provider.Provide();

                Assert.True(settings.Enabled);
                Assert.Equal(25, settings.RecentItemsCount); // 999 clamped down to the max
                Assert.Equal(1, settings.HistoryFetchCount); // 0 clamped up to the min
                Assert.Equal(1, settings.TimeoutMinutes); // -5 clamped up to the min
            }
            finally
            {
                SettingsFile._cachedSettings = null;
            }
        }

        [Fact]
        public void RedownloadSettingsProvider_blank_AnimeRootFolder_falls_back_to_default()
        {
            // S-1 closes I-1's fail-open gap at the read layer: a blank AnimeRootFolder must never
            // reach RedownloadResolver.IsAnime, whose path-based OR-rule silently no-ops on an
            // empty root and would otherwise re-enable redownloads for anime-rooted-but-standard-
            // typed shows (e.g. SpongeBob/He-Man, per the finding).
            SettingsFile._cachedSettings = JObject.Parse(@"
            {
              ""Redownload"": {
                ""Enabled"": true,
                ""CommandName"": ""redownload"",
                ""RecentItemsCount"": 5,
                ""HistoryFetchCount"": 25,
                ""TimeoutMinutes"": 45,
                ""AnimeRootFolder"": ""   "",
                ""AnimeSnarkUserId"": """",
                ""AnimeSkipMessage"": """"
              }
            }");
            try
            {
                var provider = new RedownloadSettingsProvider();

                var settings = provider.Provide();

                Assert.Equal("/data/plexmedia/Anime", settings.AnimeRootFolder);
            }
            finally
            {
                SettingsFile._cachedSettings = null;
            }
        }

        private static void AssertSpecDefaults(RedownloadSettings settings)
        {
            Assert.False(settings.Enabled);
            Assert.Equal("redownload", settings.CommandName);
            Assert.Equal(5, settings.RecentItemsCount);
            Assert.Equal(25, settings.HistoryFetchCount);
            Assert.Equal(45, settings.TimeoutMinutes);
            Assert.Equal("/data/plexmedia/Anime", settings.AnimeRootFolder);
            Assert.Equal(string.Empty, settings.AnimeSnarkUserId);
            Assert.Equal(AnimeSkipMessage, settings.AnimeSkipMessage);
        }
    }
}
