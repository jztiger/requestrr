using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public interface IBookUserInterface
    {
        Task ShowBookSelection(BookRequest request, IReadOnlyList<Book> books);
        Task WarnNoBookFoundAsync(string title);
        Task DisplayBookDetailsAsync(BookRequest request, Book book);
        Task DisplayBookRequestDeniedAsync(Book book);
        Task DisplayBookRequestSuccessAsync(Book book);
        Task WarnBookAlreadyAvailableAsync(Book book);
        Task WarnBookUnavailableAndAlreadyHasNotificationAsync(Book book);
        Task AskForNotificationBookRequestAsync(Book book);
        Task DisplayNotificationBookSuccessAsync(Book book);
    }
}
