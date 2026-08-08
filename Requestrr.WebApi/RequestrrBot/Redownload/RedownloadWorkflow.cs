using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    // Thin seam so the Discord-facing rendering is fakeable in tests, mirroring how
    // RedownloadEngine took IRedownloadMessenger. DiscordRedownloadUserInterface (in
    // ChatClients/Discord) is the only real implementation -- it turns these calls into
    // DSharpPlus embeds/components against the ephemeral interaction response. It is
    // deliberately NOT unit-tested itself (thin glue, exercised live in Task 12); every branch
    // that decides WHICH of these to call, with what content, lives in RedownloadWorkflow below
    // and IS unit-tested.
    public interface IRedownloadEphemeralResponder
    {
        Task ShowEmptyListAsync();
        Task ShowListAsync(IReadOnlyList<WatchedItem> items, int animeSkipped);
        Task ShowGateMessageAsync();
        Task ShowResolveErrorAsync(string displayTitle, string error);
        Task ShowInFlightAsync(string displayTitle);
        Task ShowConfirmAsync(RedownloadTarget target);
        Task ShowStartedAsync();
        Task ShowCancelledAsync();
        Task ShowStaleCardAsync();
    }

    // One instance per interaction (slash-command invocation, or a select/button continuation
    // of one), built by RedownloadWorkflowFactory. All /redownload decision logic lives here;
    // the responder/messenger/engine/resolver it's handed are the only points of I/O, which is
    // what makes this class unit-testable with fakes/stubs and no real Discord connection.
    public class RedownloadWorkflow
    {
        private readonly ulong _userId;
        private readonly string _username;
        private readonly ulong _channelId;
        private readonly RedownloadSettings _settings;
        private readonly IRedownloadMediaClient _sonarrClient;
        private readonly IRedownloadMediaClient _radarrClient;
        private readonly RedownloadResolver _resolver;
        private readonly RedownloadJobsRepository _repo;
        private readonly RedownloadEngine _engine;
        private readonly IRedownloadMessenger _messenger;
        private readonly IRedownloadEphemeralResponder _responder;
        private readonly ConcurrentDictionary<ulong, PendingRedownload> _pending;
        private readonly ILogger _logger;

        // internal: PendingRedownload (a constructor parameter's generic argument) is itself
        // internal, so this constructor cannot be public. RedownloadWorkflowFactory (same
        // assembly) is the only production caller; tests reach it via InternalsVisibleTo.
        internal RedownloadWorkflow(
            ulong userId,
            string username,
            ulong channelId,
            RedownloadSettings settings,
            IRedownloadMediaClient sonarrClient,
            IRedownloadMediaClient radarrClient,
            RedownloadResolver resolver,
            RedownloadJobsRepository repo,
            RedownloadEngine engine,
            IRedownloadMessenger messenger,
            IRedownloadEphemeralResponder responder,
            ConcurrentDictionary<ulong, PendingRedownload> pending,
            ILogger logger)
        {
            _userId = userId;
            _username = username;
            _channelId = channelId;
            _settings = settings;
            _sonarrClient = sonarrClient;
            _radarrClient = radarrClient;
            _resolver = resolver;
            _repo = repo;
            _engine = engine;
            _messenger = messenger;
            _responder = responder;
            _pending = pending;
            _logger = logger;
        }

        // Slash command entry. Sequence is BINDING: Enabled check (the command shouldn't even
        // be registered when disabled, but this is the belt-and-suspenders stop) -> recycle-bin
        // gate on BOTH adapters, BEFORE any Tautulli/history call -> build+offer the recent
        // list -> public anime-skip snark, fired once per invocation, independent of whether the
        // snark user id is configured (only the PUBLIC post is gated on that; the footnote on
        // the ephemeral list always reflects the real skip count).
        public async Task ShowRecentListAsync()
        {
            if (!_settings.Enabled)
            {
                return;
            }

            string sonarrBin;
            string radarrBin;

            try
            {
                sonarrBin = await _sonarrClient.GetRecycleBinPathAsync();
                radarrBin = await _radarrClient.GetRecycleBinPathAsync();
            }
            catch (Exception ex)
            {
                // An arr being unreachable must not strand the ephemeral on "Looking up recently
                // watched..." forever -- degrade to the same gate message a genuinely unset
                // recycle bin would produce.
                _logger.LogError(ex, "Redownload: recycle bin lookup failed: " + ex.Message);
                ClearPendingTarget();
                await _responder.ShowGateMessageAsync();
                return;
            }

            if (string.IsNullOrEmpty(sonarrBin) || string.IsNullOrEmpty(radarrBin))
            {
                // The gate closing invalidates any confirm card issued from an earlier, still-
                // open invocation: without this, an old RDC/{userId}/{arrId} button whose arrId
                // still happens to match a leftover pending.Target would sail past the
                // HandleConfirmAsync staleness check even though the feature (or at least the
                // recycle bin) is no longer configured.
                ClearPendingTarget();
                await _responder.ShowGateMessageAsync();
                return;
            }

            var recentList = await _resolver.BuildRecentListAsync();

            if (recentList.Items != null && recentList.Items.Count > 0)
            {
                _pending[_userId] = new PendingRedownload { Offered = recentList.Items };
                await _responder.ShowListAsync(recentList.Items, recentList.AnimeSkipped);
            }
            else
            {
                ClearPendingTarget();
                await _responder.ShowEmptyListAsync();
            }

            // AnimeSkipMessage defaults to "" -- guard it too, not just the user id, so a
            // half-configured snark section can never post an empty message (which Discord
            // rejects with a 400 that would otherwise throw out of this method AFTER the list
            // has already been rendered to the user).
            if (recentList.AnimeSkipped > 0 && !string.IsNullOrWhiteSpace(_settings.AnimeSnarkUserId)
                && !string.IsNullOrWhiteSpace(_settings.AnimeSkipMessage))
            {
                var content = _settings.AnimeSkipMessage
                    .Replace("{count}", recentList.AnimeSkipped.ToString())
                    .Replace("{user}", $"<@{_settings.AnimeSnarkUserId}>");

                await _messenger.PostPublicAsync(_channelId, content);
            }
        }

        // RDS continuation: user picked an index out of the list offered at list time. Re-checks
        // Enabled: an open list/card must not still be actionable after an admin disables the
        // feature -- component routing outlives command (un)registration, so this is the only
        // place that stops it.
        public async Task HandleSelectionAsync(int index)
        {
            if (!_settings.Enabled)
            {
                return;
            }

            if (!_pending.TryGetValue(_userId, out var pending) || pending.Offered == null
                || index < 0 || index >= pending.Offered.Count)
            {
                return;
            }

            var item = pending.Offered[index];
            var result = await _resolver.ResolveTargetAsync(item);

            if (result.Error != null)
            {
                // A resolve failure for THIS selection must not leave a PREVIOUS selection's
                // still-valid-looking Target (and its still-clickable confirm card) behind.
                pending.Target = null;
                await _responder.ShowResolveErrorAsync(item.Display, result.Error);
                return;
            }

            if (_repo.HasActiveFor(result.Target.IsMovie, result.Target.ArrId))
            {
                pending.Target = null;
                await _responder.ShowInFlightAsync(result.Target.DisplayTitle);
                return;
            }

            pending.Selected = item;
            pending.Target = result.Target;
            await _responder.ShowConfirmAsync(result.Target);
        }

        // RDC continuation. arrId comes from the confirm button's own custom id
        // (RDC/{userId}/{arrId}), captured at the moment that specific card was rendered. A user
        // can have selected a DIFFERENT item since that card was shown (pending.Target now points
        // at item B while an older, still-open card for item A is clicked) -- comparing arrId
        // against the CURRENT pending.Target.ArrId catches that: a mismatch means this card is
        // stale and must not be actioned, or the engine would delete B's file while every message
        // on screen still describes A. Also re-checks Enabled (see HandleSelectionAsync) and
        // re-checks HasActiveFor immediately before committing -- closes the race where a second
        // confirmation for the same underlying arr item (a different Discord session, or a
        // double-click that got through before the first commit cleared Pending) would otherwise
        // trigger a second, redundant BeginAsync. Once BeginAsync has been called, Pending is
        // cleared -- a repeated HandleConfirmAsync call for this user (e.g. a duplicate component
        // event) finds nothing pending and is a silent no-op.
        public async Task HandleConfirmAsync(int arrId)
        {
            if (!_settings.Enabled)
            {
                return;
            }

            if (!_pending.TryGetValue(_userId, out var pending) || pending.Target == null)
            {
                return;
            }

            if (pending.Target.ArrId != arrId)
            {
                await _responder.ShowStaleCardAsync();
                return;
            }

            if (_repo.HasActiveFor(pending.Target.IsMovie, pending.Target.ArrId))
            {
                await _responder.ShowInFlightAsync(pending.Target.DisplayTitle);
                _pending.TryRemove(_userId, out _);
                return;
            }

            await _engine.BeginAsync(pending.Target, _userId, _username, _channelId, _settings.TimeoutMinutes);
            await _responder.ShowStartedAsync();
            _pending.TryRemove(_userId, out _);
        }

        // RDX continuation. Nothing destructive has happened yet at this point in the flow, so
        // there is nothing to undo -- just drop the offered/selected state for this user.
        public async Task HandleCancelAsync()
        {
            _pending.TryRemove(_userId, out _);
            await _responder.ShowCancelledAsync();
        }

        // Drops just the Target off an existing Pending entry for this user, if one exists,
        // leaving Offered (the dropdown's own backing list) intact. Called from every
        // ShowRecentListAsync path that doesn't itself set a fresh Target, so a stale confirm
        // card issued before this call can never be actioned against a state the user has since
        // moved past (gate closed, list came back empty).
        private void ClearPendingTarget()
        {
            if (_pending.TryGetValue(_userId, out var pending))
            {
                pending.Target = null;
            }
        }
    }
}
