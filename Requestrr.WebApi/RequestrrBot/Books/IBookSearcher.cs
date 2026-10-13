using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public interface IBookSearcher
    {
        Task<IReadOnlyList<Book>> SearchBookAsync(BookRequest request, string title);
        Task<Book> SearchBookByIdAsync(BookRequest request, string bookId);
        Task<Dictionary<string, Book>> SearchAvailableBooksAsync(int categoryId, HashSet<string> bookIds, CancellationToken token);
    }
}
