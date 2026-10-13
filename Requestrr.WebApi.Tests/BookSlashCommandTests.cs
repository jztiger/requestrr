using System;
using System.Collections.Generic;
using System.IO;
using Requestrr.WebApi.RequestrrBot;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Lidarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Ombi;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Overseerr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Sonarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Redownload;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    [Collection("SlashCommandBuilder")]
    public class BookSlashCommandTests
    {
        private static string Template()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                var candidate = Path.Combine(dir.FullName, "Requestrr.WebApi", "SlashCommands.txt");
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
                dir = dir.Parent;
            }
            throw new FileNotFoundException("SlashCommands.txt not found");
        }

        private static string Build(string bookClient, ulong[] roles = null, string musicClient = DownloadClient.Disabled)
        {
            var previous = Language.Current;
            Language.Current = new Language
            {
                DiscordCommandRequestGroupName = "request",
                DiscordCommandHelpRequestName = "help",
                DiscordCommandPingRequestName = "ping",
            };
            try
            {
                var settings = new DiscordSettings
                {
                    MovieRoles = new string[0], TvShowRoles = new string[0], MusicRoles = new string[0],
                    BookRoles = Array.ConvertAll(roles ?? new ulong[0], x => x.ToString()),
                    MonitoredChannels = new string[] { "1" },
                    MovieDownloadClient = DownloadClient.Disabled,
                    TvShowDownloadClient = DownloadClient.Disabled,
                    MusicDownloadClient = musicClient,
                    BookDownloadClient = bookClient,
                };
                var books = new BookshelfSettings
                {
                    Categories = new[]
                    {
                        new BookshelfCategory { Id = 0, Name = "audiobook" },
                        new BookshelfCategory { Id = 1, Name = "ebook" },
                    }
                };
                return SlashCommandBuilder.GetCode(Template(), settings, new RadarrSettings(), new SonarrSettings(), new OverseerrSettings(), new OmbiSettings(), new LidarrSettings { Categories = new[] { new LidarrCategory { Id = 0, Name = "music" } } }, books, new RedownloadSettings(), new TautulliSettings());
            }
            finally { Language.Current = previous; }
        }

        [Fact]
        public void Enabled_generates_one_command_per_category_with_roles()
        {
            var code = Build("Bookshelf", new ulong[] { 111, 222 });

            Assert.Contains("[SlashCommand(\"audiobook\"", code);
            Assert.Contains("[SlashCommand(\"ebook\"", code);
            Assert.Contains("BookByTitle0(", code);
            Assert.Contains("BookByTitle1(", code);
            Assert.Contains("CreateRequestingWorkflow(ctx.Interaction, 1)", code);
            Assert.Contains("new ulong[] { 111UL,222UL }", code);
            Assert.DoesNotContain("[BOOK_", code);
            Assert.DoesNotContain("[REQUIRED_BOOK_ROLE_IDS]", code);
            Assert.Equal(new List<string> { "request audiobook", "request ebook" }, SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Book]);
        }

        [Fact]
        public void Disabled_removes_book_block_and_command_list_entry()
        {
            var code = Build("Disabled");
            Assert.DoesNotContain("BookByTitle", code);
            Assert.False(SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Book));
        }

        [Fact]
        public void Music_enabled_books_disabled_removes_only_book_block()
        {
            var code = Build("Disabled", musicClient: DownloadClient.Lidarr);

            Assert.Contains("MusicArtistByName0(", code);
            Assert.DoesNotContain("BookByTitle", code);
            Assert.DoesNotContain("[BOOK_", code);
            Assert.False(SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Book));
            Assert.True(SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Music));
        }

        [Fact]
        public void Music_and_books_enabled_both_expand()
        {
            var code = Build("Bookshelf", new ulong[] { 5 }, DownloadClient.Lidarr);

            Assert.Contains("MusicArtistByName0(", code);
            Assert.Contains("BookByTitle0(", code);
            Assert.Contains("BookByTitle1(", code);
            Assert.DoesNotContain("[BOOK_", code);
            Assert.DoesNotContain("[MUSIC_", code);
            Assert.Equal(new List<string> { "request music" }, SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Music]);
            Assert.Equal(2, SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Book].Count);
        }
    }
}
