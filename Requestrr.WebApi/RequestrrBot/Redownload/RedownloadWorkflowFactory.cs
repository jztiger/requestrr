using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using DSharpPlus.EventArgs;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;
using Requestrr.WebApi.RequestrrBot.Redownload.Arr;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    // Pre-confirm state for one Discord user's in-flight /redownload interaction. In-memory
    // only, deliberately not persisted -- it dies with a restart, which is fine because nothing
    // destructive has happened to any file yet by this point in the flow (that only starts once
    // RedownloadWorkflow.HandleConfirmAsync calls RedownloadEngine.BeginAsync).
    internal class PendingRedownload
    {
        public System.Collections.Generic.IReadOnlyList<WatchedItem> Offered;
        public RedownloadTarget Target;
        public WatchedItem Selected;
    }

    public class RedownloadWorkflowFactory
    {
        private readonly RedownloadSettingsProvider _settingsProvider;
        private readonly TautulliClient _tautulliClient;
        private readonly SonarrRedownloadClient _sonarrClient;
        private readonly RadarrRedownloadClient _radarrClient;
        private readonly RedownloadJobsRepository _repo;
        private readonly ILogger<RedownloadWorkflow> _logger;

        // key = discord user id. Shared by every RedownloadWorkflow this factory hands out, so
        // a selection made by one workflow instance (ShowRecentListAsync/HandleSelectionAsync)
        // is visible to the later workflow instance that handles that user's confirm/cancel
        // continuation -- those are necessarily different RedownloadWorkflow objects (built
        // fresh per interaction) but must share this one dictionary.
        internal readonly ConcurrentDictionary<ulong, PendingRedownload> Pending = new ConcurrentDictionary<ulong, PendingRedownload>();

        private readonly object _engineLock = new object();
        private RedownloadEngine _engine;
        private DiscordClient _client;
        private IRedownloadMessenger _messenger;

        public RedownloadWorkflowFactory(
            RedownloadSettingsProvider settingsProvider,
            TautulliClient tautulliClient,
            SonarrRedownloadClient sonarrClient,
            RadarrRedownloadClient radarrClient,
            RedownloadJobsRepository repo,
            ILogger<RedownloadWorkflow> logger)
        {
            _settingsProvider = settingsProvider;
            _tautulliClient = tautulliClient;
            _sonarrClient = sonarrClient;
            _radarrClient = radarrClient;
            _repo = repo;
            _logger = logger;
        }

        // Movie -> Radarr, episode -> Sonarr. Reused by both the singleton engine (passed once,
        // at construction) and every workflow's own gate check (the two adapters, called
        // directly, since the gate needs BOTH regardless of which one an eventually-picked item
        // will resolve to).
        internal Func<bool, IRedownloadMediaClient> ClientSelector =>
            isMovie => isMovie ? (IRedownloadMediaClient)_radarrClient : _sonarrClient;

        // Singleton engine: the poll loop, its jobs repository, and its Pending-independent
        // state must survive across every settings-refresh cycle, not be rebuilt per call --
        // Start()'s own re-entrancy guard is a safety net, not a substitute for this caching.
        // _client is refreshed on every call (not just the first) so a bot-token change that
        // swaps out the DiscordClient (ChatBot.RestartBot) doesn't leave the messenger holding a
        // disposed client -- the messenger reads _client indirectly via a closure, at call time,
        // not by capturing the reference up front.
        public RedownloadEngine GetOrCreateEngine(DiscordClient client, ILogger logger)
        {
            lock (_engineLock)
            {
                _client = client;

                if (_engine == null)
                {
                    _messenger = new DiscordRedownloadMessenger(() => _client);
                    _engine = new RedownloadEngine(ClientSelector, _repo, _messenger, logger, TimeSpan.FromSeconds(60));
                }

                return _engine;
            }
        }

        public RedownloadWorkflow CreateWorkflow(DiscordInteraction interaction)
        {
            // ChannelId (a plain field-backed property) rather than Channel.Id -- Channel is
            // resolved from the client's cache and can come back null for an uncached channel,
            // which would NRE here and get silently swallowed by the template's try/catch.
            return BuildWorkflow(interaction.User.Id, interaction.User.Username, interaction.ChannelId,
                new DiscordRedownloadUserInterface(interaction));
        }

        public RedownloadWorkflow CreateWorkflow(ComponentInteractionCreateEventArgs e)
        {
            return BuildWorkflow(e.User.Id, e.User.Username, e.Interaction.ChannelId,
                new DiscordRedownloadUserInterface(e.Interaction));
        }

        private RedownloadWorkflow BuildWorkflow(ulong userId, string username, ulong channelId, IRedownloadEphemeralResponder responder)
        {
            if (_engine == null || _messenger == null)
            {
                // GetOrCreateEngine is called by ChatBot.ApplyBotConfigurationAsync BEFORE
                // slash commands are (re)registered on every restart/settings-refresh cycle, so
                // in practice this can never be hit by a real interaction -- it's a fail-fast
                // guard against a future wiring regression, not a normal code path.
                throw new InvalidOperationException("RedownloadWorkflowFactory.GetOrCreateEngine must be called before CreateWorkflow.");
            }

            var settings = _settingsProvider.Provide();
            var options = new RedownloadResolverOptions
            {
                RecentItemsCount = settings.RecentItemsCount,
                HistoryFetchCount = settings.HistoryFetchCount,
                AnimeRootFolder = settings.AnimeRootFolder,
            };
            var resolver = new RedownloadResolver(_tautulliClient, _sonarrClient, _radarrClient, options);

            return new RedownloadWorkflow(userId, username, channelId, settings, _sonarrClient, _radarrClient,
                resolver, _repo, _engine, _messenger, responder, Pending, _logger);
        }

        // Discord impl of IRedownloadMessenger, shared by the engine and every workflow's public
        // anime-skip snark post. Reads the client via a delegate rather than capturing it
        // directly so a client swap (bot token change) is picked up without rebuilding this
        // instance.
        private class DiscordRedownloadMessenger : IRedownloadMessenger
        {
            private readonly Func<DiscordClient> _clientAccessor;

            public DiscordRedownloadMessenger(Func<DiscordClient> clientAccessor)
            {
                _clientAccessor = clientAccessor;
            }

            public async Task<(ulong channelId, ulong messageId)> PostPublicAsync(ulong channelId, string content)
            {
                var channel = await _clientAccessor().GetChannelAsync(channelId);
                var message = await channel.SendMessageAsync(content);
                return (channelId, message.Id);
            }

            public async Task EditPublicAsync(ulong channelId, ulong messageId, string content)
            {
                var channel = await _clientAccessor().GetChannelAsync(channelId);
                var message = await channel.GetMessageAsync(messageId);
                await message.ModifyAsync(content);
            }
        }
    }
}
