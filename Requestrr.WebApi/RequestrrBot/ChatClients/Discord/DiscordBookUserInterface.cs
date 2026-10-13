using DSharpPlus.Entities;
using Requestrr.WebApi.RequestrrBot.Books;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.ChatClients.Discord
{
    // Task 4 scaffold: only GenerateBookDetails is real. The interaction handlers are filled in by Task 5.
    public class DiscordBookUserInterface : IBookUserInterface
    {
        private readonly DiscordInteraction _interactionContext;
        private readonly IBookSearcher _bookSearcher;

        public DiscordBookUserInterface(DiscordInteraction interactionContext, IBookSearcher bookSearcher)
        {
            _interactionContext = interactionContext;
            _bookSearcher = bookSearcher;
        }

        public static DiscordEmbed GenerateBookDetails(Book book)
        {
            DiscordEmbedBuilder embedBuilder = new DiscordEmbedBuilder()
                .WithTitle(string.IsNullOrWhiteSpace(book.AuthorName) ? book.Title : $"{book.Title} - {book.AuthorName}")
                .WithTimestamp(DateTime.Now)
                .WithFooter("Powered by Requestrr");

            if (!string.IsNullOrWhiteSpace(book.Overview))
                embedBuilder.WithDescription(book.Overview.Substring(0, Math.Min(book.Overview.Length, 255)) + "(...)");

            if (!string.IsNullOrWhiteSpace(book.PosterPath) && book.PosterPath.StartsWith("http", StringComparison.InvariantCultureIgnoreCase))
                embedBuilder.WithImageUrl(book.PosterPath);

            if (!string.IsNullOrWhiteSpace(book.SeriesTitle))
                embedBuilder.AddField("__Series__", book.SeriesTitle, true);

            if (book.Year.HasValue)
                embedBuilder.AddField("__Year__", book.Year.Value.ToString(), true);

            return embedBuilder.Build();
        }

        public Task ShowBookSelection(BookRequest request, IReadOnlyList<Book> books) => throw new NotImplementedException();
        public Task WarnNoBookFoundAsync(string title) => throw new NotImplementedException();
        public Task DisplayBookDetailsAsync(BookRequest request, Book book) => throw new NotImplementedException();
        public Task DisplayBookRequestDeniedAsync(Book book) => throw new NotImplementedException();
        public Task DisplayBookRequestSuccessAsync(Book book) => throw new NotImplementedException();
        public Task WarnBookAlreadyAvailableAsync(Book book) => throw new NotImplementedException();
        public Task WarnBookUnavailableAndAlreadyHasNotificationAsync(Book book) => throw new NotImplementedException();
        public Task AskForNotificationBookRequestAsync(Book book) => throw new NotImplementedException();
        public Task DisplayNotificationBookSuccessAsync(Book book) => throw new NotImplementedException();
    }
}
