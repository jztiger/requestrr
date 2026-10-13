using System.Linq;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;

namespace Requestrr.WebApi.RequestrrBot.Notifications.Books
{
    public static class BookReadyMessage
    {
        public static string Format(Book book, string categoryName)
        {
            string suffix = string.IsNullOrWhiteSpace(categoryName) ? string.Empty : $" ({categoryName})";
            return $"📚 **{book.Title}** by {book.AuthorName} is now available{suffix}.";
        }

        /// <summary>Category name for the id, or null when settings are unreadable or the category is gone.</summary>
        public static string ResolveCategoryName(BookshelfSettingsProvider provider, int categoryId)
        {
            try
            {
                return provider.Provide().Categories?.FirstOrDefault(c => c.Id == categoryId)?.Name;
            }
            catch
            {
                return null;
            }
        }
    }
}
