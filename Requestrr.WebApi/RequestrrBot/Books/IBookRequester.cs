using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public interface IBookRequester
    {
        Task<BookRequestResult> RequestBookAsync(BookRequest request, Book book);
    }

    public class BookRequestResult
    {
        public bool WasDenied { get; set; }
    }
}
