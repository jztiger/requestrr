using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Redownload;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Requestrr.WebApi.Controllers.Redownload
{
    [ApiController]
    [Authorize]
    [Route("/api/redownload")]
    public class RedownloadController : ControllerBase
    {
        private static readonly Regex CommandNamePattern = new Regex(@"^[a-z0-9_-]{1,32}$");

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<TautulliClient> _logger;
        private readonly RedownloadSettingsProvider _redownloadSettingsProvider;
        private readonly TautulliSettingsProvider _tautulliSettingsProvider;

        public RedownloadController(
            IHttpClientFactory httpClientFactory,
            ILogger<TautulliClient> logger,
            RedownloadSettingsProvider redownloadSettingsProvider,
            TautulliSettingsProvider tautulliSettingsProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _redownloadSettingsProvider = redownloadSettingsProvider;
            _tautulliSettingsProvider = tautulliSettingsProvider;
        }



        /// <summary>
        /// Returns the combined Redownload + Tautulli settings for the settings page.
        /// </summary>
        [HttpGet()]
        public IActionResult GetAsync()
        {
            var redownloadSettings = _redownloadSettingsProvider.Provide();
            var tautulliSettings = _tautulliSettingsProvider.Provide();

            return Ok(new RedownloadSettingsModel
            {
                TautulliHostname = tautulliSettings.Hostname,
                TautulliPort = tautulliSettings.Port,
                TautulliApiKey = tautulliSettings.ApiKey,
                TautulliUseSSL = tautulliSettings.UseSSL,

                Enabled = redownloadSettings.Enabled,
                CommandName = redownloadSettings.CommandName,
                RecentItemsCount = redownloadSettings.RecentItemsCount,
                HistoryFetchCount = redownloadSettings.HistoryFetchCount,
                TimeoutMinutes = redownloadSettings.TimeoutMinutes,
                AnimeRootFolder = redownloadSettings.AnimeRootFolder,
                AnimeSnarkUserId = redownloadSettings.AnimeSnarkUserId,
                AnimeSkipMessage = redownloadSettings.AnimeSkipMessage
            });
        }



        /// <summary>
        /// Handles the testing of Tautulli settings, pass the settings into a Tautulli client
        /// </summary>
        /// <param name="model">Test settings for Tautulli</param>
        /// <returns>Returns if connection was successful</returns>
        [HttpPost("testtautulli")]
        public async Task<IActionResult> TestTautulliSettings([FromBody] TestTautulliSettingsModel model)
        {
            try
            {
                await TautulliClient.TestConnectionAsync(_httpClientFactory.CreateClient(), _logger, new TautulliSettings
                {
                    Hostname = model.Hostname?.Trim(),
                    Port = model.Port,
                    ApiKey = model.ApiKey?.Trim(),
                    UseSSL = model.UseSSL
                });

                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }



        /// <summary>
        /// Handles the saving of Redownload + Tautulli settings
        /// </summary>
        /// <param name="model">Redownload settings</param>
        /// <returns></returns>
        [HttpPost()]
        public IActionResult SaveAsync([FromBody] RedownloadSettingsModel model)
        {
            var commandName = model.CommandName?.Trim() ?? string.Empty;

            if (!CommandNamePattern.IsMatch(commandName))
                return BadRequest("Command name must be 1-32 characters, using only lowercase letters, numbers, dashes and underscores.");

            if (GetReservedCommandNames().Contains(commandName))
                return BadRequest($"CommandName '{commandName}' collides with an existing command.");

            if (model.RecentItemsCount < 1 || model.RecentItemsCount > 25)
                return BadRequest("Recent items count must be between 1 and 25.");

            if (model.HistoryFetchCount < 1 || model.HistoryFetchCount > 200)
                return BadRequest("History fetch count must be between 1 and 200.");

            if (model.TimeoutMinutes < 1 || model.TimeoutMinutes > 1440)
                return BadRequest("Timeout must be between 1 and 1440 minutes.");

            SettingsFile.Write(settings =>
            {
                settings.Redownload.Enabled = model.Enabled;
                settings.Redownload.CommandName = commandName;
                settings.Redownload.RecentItemsCount = model.RecentItemsCount;
                settings.Redownload.HistoryFetchCount = model.HistoryFetchCount;
                settings.Redownload.TimeoutMinutes = model.TimeoutMinutes;
                settings.Redownload.AnimeRootFolder = model.AnimeRootFolder?.Trim() ?? string.Empty;
                settings.Redownload.AnimeSnarkUserId = model.AnimeSnarkUserId?.Trim() ?? string.Empty;
                settings.Redownload.AnimeSkipMessage = model.AnimeSkipMessage ?? string.Empty;

                settings.DownloadClients.Tautulli.Hostname = model.TautulliHostname?.Trim() ?? string.Empty;
                settings.DownloadClients.Tautulli.Port = model.TautulliPort;
                settings.DownloadClients.Tautulli.ApiKey = model.TautulliApiKey?.Trim() ?? string.Empty;
                settings.DownloadClients.Tautulli.UseSSL = model.TautulliUseSSL;
            });

            return Ok(new { ok = true });
        }



        /// <summary>
        /// Top-level Discord command/group names that already occupy the bot's top-level
        /// slash-command namespace, resolved the same way SlashCommandBuilder resolves them
        /// (see SlashCommandBuilder.GetCode / SlashCommands.txt): the ping and help commands
        /// are bare top-level [SlashCommand]s, and the request/issue groups are top-level
        /// [SlashCommandGroup]s. /redownload is also registered bare top-level, so a
        /// CommandName matching any of these makes SlashCommandBuilder throw
        /// "Duplicate Slash Commands detected" on the bot's next settings poll, taking the
        /// bot offline until an admin edits the name back.
        /// Falls back to the hardcoded English defaults if Language.Current hasn't been
        /// initialized yet in this context (it's set at startup in Program.cs, so this should
        /// not normally happen, but controller code shouldn't assume it).
        /// </summary>
        private static HashSet<string> GetReservedCommandNames()
        {
            var language = Language.Current;

            string help = language?.DiscordCommandHelpRequestName;
            string ping = language?.DiscordCommandPingRequestName;
            string requestGroup = language?.DiscordCommandRequestGroupName;
            string issueGroup = language?.DiscordCommandIssueName;

            return new HashSet<string>(new[]
            {
                string.IsNullOrWhiteSpace(help) ? "help" : help,
                string.IsNullOrWhiteSpace(ping) ? "ping" : ping,
                string.IsNullOrWhiteSpace(requestGroup) ? "request" : requestGroup,
                string.IsNullOrWhiteSpace(issueGroup) ? "issue" : issueGroup,
            }, StringComparer.OrdinalIgnoreCase);
        }
    }
}
