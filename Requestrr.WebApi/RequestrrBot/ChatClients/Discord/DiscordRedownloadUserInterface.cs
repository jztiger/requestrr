using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DSharpPlus;
using DSharpPlus.Entities;
using Requestrr.WebApi.RequestrrBot.Redownload;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    // Thin DSharpPlus glue for /redownload's ephemeral UI. All decision logic (which of these
    // to call, and with what content) lives in RedownloadWorkflow and is unit-tested there --
    // this class only renders, against the original ephemeral interaction response. Not
    // unit-tested itself; exercised live in Task 12.
    public class DiscordRedownloadUserInterface : IRedownloadEphemeralResponder
    {
        private readonly DiscordInteraction _interactionContext;

        public DiscordRedownloadUserInterface(DiscordInteraction interactionContext)
        {
            _interactionContext = interactionContext;
        }

        public async Task ShowEmptyListAsync()
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Nothing replaceable in the recently-watched list."));
        }

        public async Task ShowListAsync(IReadOnlyList<WatchedItem> items, int animeSkipped)
        {
            var options = items
                .Select((item, index) => new DiscordSelectComponentOption(LimitStringSize(item.Display), index.ToString()))
                .ToList();

            var select = new DiscordSelectComponent($"RDS/{_interactionContext.User.Id}", "Choose what to replace", options);

            var content = "Pick what to replace:";
            if (animeSkipped > 0)
            {
                content += $"\n_skipped {animeSkipped} anime title(s) — not supported_";
            }

            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().AddComponents(select).WithContent(content));
        }

        public async Task ShowGateMessageAsync()
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Recycle Bin isn't configured in Sonarr/Radarr — /redownload is disabled until an admin sets it."));
        }

        public async Task ShowResolveErrorAsync(string displayTitle, string error)
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent($"**{displayTitle}** — {error}."));
        }

        public async Task ShowInFlightAsync(string displayTitle)
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent($"A replacement for **{displayTitle}** is already in progress."));
        }

        public async Task ShowConfirmAsync(RedownloadTarget target)
        {
            var sizeGb = target.SizeBytes / 1_000_000_000.0;
            var content = $"⚠️ Replace **{target.DisplayTitle}**?\n" +
                          $"Current file: **{target.Quality} · {sizeGb.ToString("F1", CultureInfo.InvariantCulture)} GB · {target.ReleaseGroup}**\n" +
                          "This deletes it, blocks that release from coming back, and searches for a different one.";

            // arrId is carried on the button itself so a stale card (issued for a different
            // selection than the one currently pending) can be detected by RedownloadWorkflow.
            var replaceButton = new DiscordButtonComponent(ButtonStyle.Danger, $"RDC/{_interactionContext.User.Id}/{target.ArrId}", "Replace");
            var cancelButton = new DiscordButtonComponent(ButtonStyle.Secondary, $"RDX/{_interactionContext.User.Id}", "Cancel");

            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().AddComponents(replaceButton, cancelButton).WithContent(content));
        }

        public async Task ShowStartedAsync()
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Started — watch the channel message for progress."));
        }

        public async Task ShowCancelledAsync()
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("Cancelled."));
        }

        public async Task ShowStaleCardAsync()
        {
            await _interactionContext.EditOriginalResponseAsync(
                new DiscordWebhookBuilder().WithContent("This card is out of date — run /redownload again."));
        }

        private static string LimitStringSize(string value, int limit = 100)
        {
            return value.Length > limit ? value[..(limit - 3)] + "..." : value;
        }
    }
}
