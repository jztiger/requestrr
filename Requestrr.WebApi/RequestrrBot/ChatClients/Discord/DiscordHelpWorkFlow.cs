using System.Collections.Generic;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.SlashCommands;
using Requestrr.WebApi.RequestrrBot.Locale;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    public class DiscordHelpWorkFlow
    {
        private readonly DiscordSettings _discordSettings;
        private readonly DiscordClient _discordClient;
        private readonly InteractionContext _context;

        public DiscordHelpWorkFlow(
            DiscordClient discordClient,
            InteractionContext context,
            DiscordSettingsProvider discordSettingsProvider)
        {
            _discordSettings = discordSettingsProvider.Provide();
            _discordClient = discordClient;
            _context = context;
        }
        public async Task HandleHelpAsync()
        {
            var message = Language.Current.DiscordCommandHelpMessage.ReplaceTokens(new Dictionary<string, string>
            {
                { LanguageTokens.AuthorUsername, _context.User.Mention },
                { LanguageTokens.BotUsername, _discordClient.CurrentUser.Username },
                { LanguageTokens.CommandPrefix, "/" },
                { LanguageTokens.MovieCommandTitle, $"{Language.Current.DiscordCommandRequestGroupName.ToLower()} {Language.Current.DiscordCommandMovieRequestTitleName.ToLower()}" },

                { LanguageTokens.FullCommandList, GenerateSlashList() },
                { LanguageTokens.MovieCommandIssue, $"{Language.Current.DiscordCommandIssueName.ToLower()}" }
            });

            bool issueEnabled = false;
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.IssueMovie))
                issueEnabled = SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.IssueMovie].Count != 0;

            if (!issueEnabled && SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.IssueTv))
                issueEnabled = SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.IssueTv].Count != 0;

            if (!issueEnabled)
            {
                var beginIndex = message.IndexOf(LanguageTokens.IssueEnabledStart);
                var endIndex = message.IndexOf(LanguageTokens.IssueEnabledEnd) + LanguageTokens.IssueEnabledEnd.Length;

                message = message.Replace(message.Substring(beginIndex, endIndex - beginIndex), string.Empty);
            }

            message = message.Replace(LanguageTokens.IssueEnabledStart, string.Empty);
            message = message.Replace(LanguageTokens.IssueEnabledEnd, string.Empty);

            await _context.CreateResponseAsync(InteractionResponseType.ChannelMessageWithSource, new DiscordInteractionResponseBuilder().AsEphemeral(true).WithContent(message));
        }


        private string GenerateSlashList()
        {
            string list = string.Empty;

            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Movie))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Movie]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Tv))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Tv]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Music))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Music]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Redownload))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Redownload]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.IssueMovie))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.IssueMovie]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.IssueTv))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.IssueTv]);
            if (SlashCommandBuilder.CommandList.ContainsKey(SlashCommandBuilder.CommandType.Misc))
                list += GenerateSlashType(SlashCommandBuilder.CommandList[SlashCommandBuilder.CommandType.Misc]);

            return list;
        }

        private string GenerateSlashType(List<string> list)
        {
            string commands = string.Empty;
            foreach (string i in list)
            {
                commands += $"**/{i}**\n";
            }

            return commands;
        }
    }
}