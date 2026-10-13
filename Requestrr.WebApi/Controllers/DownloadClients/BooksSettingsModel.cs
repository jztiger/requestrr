using Requestrr.WebApi.Controllers.DownloadClients.Bookshelf;
using System.ComponentModel.DataAnnotations;

namespace Requestrr.WebApi.Controllers.DownloadClients
{
    public class BooksSettingsModel
    {
        [Required]
        public string Client { get; set; }

        [Required]
        public BookshelfSettingsModel Bookshelf { get; set; }

        [Required]
        public string[] OtherCategories { get; set; }
    }
}
