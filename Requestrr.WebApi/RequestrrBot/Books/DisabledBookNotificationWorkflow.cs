using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class DisabledBookNotificationWorkflow : IBookNotificationWorkflow
    {
        private readonly IBookUserInterface _userInterface;

        public DisabledBookNotificationWorkflow(IBookUserInterface userInterface)
        {
            _userInterface = userInterface;
        }

        public Task AddNotificationBookAsync(string userId, int categoryId, string bookId)
        {
            return Task.CompletedTask;
        }

        public Task NotifyForExistingRequestAsync(string userId, Book book)
        {
            return _userInterface.WarnBookAlreadyAvailableAsync(book);
        }

        public Task NotifyForNewRequestAsync(string userId, BookRequest request, Book book)
        {
            return Task.CompletedTask;
        }
    }
}
