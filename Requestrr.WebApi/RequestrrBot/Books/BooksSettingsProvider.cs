namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BooksSettingsProvider
    {
        public BooksSettings Provide()
        {
            dynamic settings = SettingsFile.Read();
            return new BooksSettings { Client = settings.Books.Client };
        }
    }
}
