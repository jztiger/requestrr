namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookUserRequester
    {
        public string UserId { get; }
        public string Username { get; }

        public BookUserRequester(string userId, string username)
        {
            UserId = userId;
            Username = username;
        }
    }
}
