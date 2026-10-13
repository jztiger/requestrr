namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookRequest
    {
        public int CategoryId { get; }
        public BookUserRequester User { get; }

        public BookRequest(BookUserRequester user, int categoryId)
        {
            User = user;
            CategoryId = categoryId;
        }
    }
}
