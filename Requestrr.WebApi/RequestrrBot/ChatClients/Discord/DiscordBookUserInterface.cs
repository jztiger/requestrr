using DSharpPlus;
using DSharpPlus.Entities;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    public class DiscordBookUserInterface : IBookUserInterface
    {
        private readonly DiscordInteraction _interactionContext;
        private readonly IBookSearcher _bookSearcher;
        // AskForNotificationBookRequestAsync(Book) carries no request, so the category is captured at construction.
        private readonly int _categoryId;

        public DiscordBookUserInterface(DiscordInteraction interactionContext, IBookSearcher bookSearcher, int categoryId = -1)
        {
            _interactionContext = interactionContext;
            _bookSearcher = bookSearcher;
            _categoryId = categoryId;
        }


        public async Task ShowBookSelection(BookRequest request, IReadOnlyList<Book> books)
        {
            List<DiscordSelectComponentOption> options = books.Take(15).Select(x => new DiscordSelectComponentOption(LimitStringSize(x.Title), $"{request.CategoryId}/{x.BookId}", LimitStringSize(BookshelfClientV1.DescribeForSelect(x)))).ToList();
            DiscordSelectComponent select = new DiscordSelectComponent($"BkRS/{_interactionContext.User.Id}/{request.CategoryId}", "Select a book", options);

            await _interactionContext.EditOriginalResponseAsync(new DiscordWebhookBuilder().AddComponents(select).WithContent("Which book did you mean?"));
        }


        public async Task WarnNoBookFoundAsync(string title)
        {
            await _interactionContext.EditOriginalResponseAsync(new DiscordWebhookBuilder().WithContent($"No book found for \"{title}\"."));
        }


        public async Task DisplayBookDetailsAsync(BookRequest request, Book book)
        {
            DiscordButtonComponent requestButton = new DiscordButtonComponent(ButtonStyle.Primary, $"BkRC/{_interactionContext.User.Id}/{request.CategoryId}/{book.BookId}", "Request");

            var builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(requestButton).WithContent("Is this the book you want?");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task DisplayBookRequestDeniedAsync(Book book)
        {
            DiscordButtonComponent deniedButton = new DiscordButtonComponent(ButtonStyle.Danger, "0/1/0", "Denied");
            DiscordWebhookBuilder builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(deniedButton).WithContent("Request denied.");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task DisplayBookRequestSuccessAsync(Book book)
        {
            DiscordButtonComponent successButton = new DiscordButtonComponent(ButtonStyle.Success, "0/1/0", "Requested");
            DiscordWebhookBuilder builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(successButton).WithContent($"📚 **{book.Title}** has been requested. You'll get a DM when it's ready.");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task WarnBookAlreadyAvailableAsync(Book book)
        {
            var availableButton = new DiscordButtonComponent(ButtonStyle.Primary, $"BkMU/{_interactionContext.User.Id}/{book.BookId}", "Available", true);
            var builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(availableButton).WithContent("This book is already available.");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task WarnBookUnavailableAndAlreadyHasNotificationAsync(Book book)
        {
            DiscordButtonComponent requestButton = new DiscordButtonComponent(ButtonStyle.Primary, $"BkMU/{_interactionContext.User.Id}/{book.BookId}", "Request", true);
            DiscordWebhookBuilder builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(requestButton).WithContent("This book was already requested — you'll be notified when it lands.");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task AskForNotificationBookRequestAsync(Book book)
        {
            var notificationButton = new DiscordButtonComponent(ButtonStyle.Primary, $"BkNR/{_interactionContext.User.Id}/{_categoryId}/{book.BookId}", "Notify me", false, new DiscordComponentEmoji(DiscordEmoji.FromUnicode("🔔")));
            DiscordWebhookBuilder builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(notificationButton).WithContent("This book was already requested. Want a DM when it's available?");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public async Task DisplayNotificationBookSuccessAsync(Book book)
        {
            DiscordButtonComponent successButton = new DiscordButtonComponent(ButtonStyle.Success, "0/1/0", "Notification set");
            DiscordWebhookBuilder builder = (await AddPreviousDropdownsAsync(book, new DiscordWebhookBuilder().AddEmbed(GenerateBookDetails(book)))).AddComponents(successButton).WithContent($"🔔 You'll be notified when **{book.Title}** is available.");
            await _interactionContext.EditOriginalResponseAsync(builder);
        }


        public static DiscordEmbed GenerateBookDetails(Book book)
        {
            DiscordEmbedBuilder embedBuilder = new DiscordEmbedBuilder()
                .WithTitle(book.Title)
                .WithTimestamp(DateTime.Now)
                .WithFooter("Powered by Requestrr");

            if (!string.IsNullOrWhiteSpace(book.Overview))
                embedBuilder.WithDescription(book.Overview.Substring(0, Math.Min(book.Overview.Length, 255)) + "(...)");

            if (!string.IsNullOrWhiteSpace(book.PosterPath) && book.PosterPath.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
                embedBuilder.WithImageUrl(book.PosterPath);

            if (!string.IsNullOrWhiteSpace(book.AuthorName))
                embedBuilder.AddField("__Author__", book.AuthorName, true);

            if (book.Year.HasValue)
                embedBuilder.AddField("__Year__", book.Year.Value.ToString(), true);

            if (!string.IsNullOrWhiteSpace(book.SeriesTitle))
                embedBuilder.AddField("__Series__", book.SeriesTitle, true);

            return embedBuilder.Build();
        }


        private static string LimitStringSize(string value, int limit = 100)
        {
            if (value == null) return string.Empty;
            return value.Length > limit ? value[..(limit - 3)] + "..." : value;
        }


        private async Task<DiscordWebhookBuilder> AddPreviousDropdownsAsync(Book book, DiscordWebhookBuilder builder)
        {
            DiscordSelectComponent previousSelector = (await _interactionContext.GetOriginalResponseAsync()).FilterComponents<DiscordSelectComponent>().FirstOrDefault();
            if (previousSelector != null)
            {
                DiscordSelectComponent selector = new DiscordSelectComponent(previousSelector.CustomId, LimitStringSize(book.Title), previousSelector.Options);
                builder.AddComponents(selector);
            }

            return builder;
        }
    }
}
