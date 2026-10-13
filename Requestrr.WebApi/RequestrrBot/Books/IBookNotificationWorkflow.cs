using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public interface IBookNotificationWorkflow
    {
        Task NotifyForNewRequestAsync(string userId, BookRequest request, Book book);
        Task NotifyForExistingRequestAsync(string userId, Book book);
        Task AddNotificationBookAsync(string userId, int categoryId, string bookId);
    }
}
