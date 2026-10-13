namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf
{
    public class BookshelfSettingsProvider
    {
        public BookshelfSettings Provide()
        {
            dynamic settings = SettingsFile.Read();
            return new BookshelfSettings
            {
                Categories = settings.DownloadClients.Bookshelf.Categories.ToObject<BookshelfCategory[]>(),
                Version = settings.DownloadClients.Bookshelf.Version
            };
        }
    }
}
