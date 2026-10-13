using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.Books;
using System;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Books
{
    public class ChannelBookNotifier : IBookNotifier
    {
        private readonly DiscordClient _discordClient;
        private readonly ulong[] _channelIds;
        private readonly ILogger _logger;
        private readonly BookshelfSettingsProvider _bookshelfSettings;

        public ChannelBookNotifier(
            DiscordClient discordClient,
            ulong[] channelIds,
            ILogger logger,
            BookshelfSettingsProvider bookshelfSettings = null
        )
        {
            _bookshelfSettings = bookshelfSettings ?? new BookshelfSettingsProvider();
            _discordClient = discordClient;
            _channelIds = channelIds;
            _logger = logger;
        }


        public async Task<HashSet<string>> NotifyBookAsync(IReadOnlyCollection<string> userIds, Book book, int categoryId, CancellationToken token)
        {
            if (_discordClient.Guilds.Any())
            {
                string message = BookReadyMessage.Format(book, BookReadyMessage.ResolveCategoryName(_bookshelfSettings, categoryId));
                HashSet<ulong> discordUserIds = new HashSet<ulong>(userIds.Select(x => ulong.Parse(x)));
                HashSet<ulong> userNotified = new HashSet<ulong>();

                DiscordChannel[] channels = GetNoificationChannels();
                HandleRemovedUsers(discordUserIds, userNotified, token);

                foreach (DiscordChannel channel in channels)
                {
                    if (token.IsCancellationRequested)
                        return new HashSet<string>(userNotified.Select(x => x.ToString()));

                    try
                    {
                        await NotifyUsersInChannelForBook(book, message, discordUserIds, userNotified, channel);
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"An error occurred while sending a book notification to channel \"{channel?.Name}\" in server \"{channel?.Guild?.Name}\": " + ex.Message);
                    }
                }

                return new HashSet<string>(userNotified.Select(x => x.ToString()));
            }

            return new HashSet<string>();
        }


        private DiscordChannel[] GetNoificationChannels()
        {
            return _discordClient.Guilds
                .SelectMany(x => x.Value.Channels.Values)
                .Where(x => _channelIds.Contains(x.Id))
                .OfType<DiscordChannel>()
                .ToArray();
        }


        private void HandleRemovedUsers(HashSet<ulong> discordUserIds, HashSet<ulong> userNotified, CancellationToken token)
        {
            HashSet<ulong> currentMembers = new HashSet<ulong>(_discordClient.Guilds.SelectMany(x => x.Value.Members).Select(x => x.Value.Id));

            foreach (ulong missingUserIds in discordUserIds.Except(currentMembers))
            {
                if (token.IsCancellationRequested)
                    return;

                userNotified.Add(missingUserIds);
                _logger.LogWarning($"Removing book notification for user with ID {missingUserIds} as it could not be found in any of the guilds.");
            }
        }


        private static async Task NotifyUsersInChannelForBook(Book book, string message, HashSet<ulong> discordUserIds, HashSet<ulong> userNotified, DiscordChannel channel)
        {
            List<DiscordMember> usersToNotify = channel.Users
                .Where(x => discordUserIds.Contains(x.Id))
                .Where(x => !userNotified.Contains(x.Id))
                .ToList();

            if (usersToNotify.Any())
            {
                StringBuilder messageBuilder = new StringBuilder();
                messageBuilder.AppendLine(message);

                foreach (DiscordMember user in usersToNotify)
                {
                    string userMentionText = $"{user.Mention} ";
                    if (messageBuilder.Length + userMentionText.Length < DiscordConstants.MaxMessageLength)
                        messageBuilder.Append(userMentionText);
                }

                await channel.SendMessageAsync(messageBuilder.ToString(), DiscordBookUserInterface.GenerateBookDetails(book));

                foreach (DiscordMember user in usersToNotify)
                {
                    userNotified.Add(user.Id);
                }
            }
        }
    }
}
