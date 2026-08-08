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
        public void RedownloadSettingsProvider_returns_disabled_defaults_when_settings_file_unreadable()
        {
            // No settings.json exists at SettingsFile's configured path in the test environment,
            // so Read() throws and the provider's catch-all fallback is exercised.
            var provider = new RedownloadSettingsProvider();

            var settings = provider.Provide();

            Assert.False(settings.Enabled);
        }

        [Fact]
        public void RedownloadSettingsProvider_returns_disabled_defaults_when_redownload_section_missing()
        {
            // Directly exercises the redownloadSection == null branch (settings file present,
            // but no top-level "Redownload" key) rather than the exception fallback above.
            SettingsFile._cachedSettings = JObject.Parse(@"{ ""Version"": ""2.1.4"", ""DownloadClients"": {} }");
            try
            {
                var provider = new RedownloadSettingsProvider();

                var settings = provider.Provide();

                Assert.False(settings.Enabled);
            }
            finally
            {
                SettingsFile._cachedSettings = null;
            }
        }
    }
}
