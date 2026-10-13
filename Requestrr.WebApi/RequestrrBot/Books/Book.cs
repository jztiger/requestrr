namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class Book
    {
        public string DownloadClientId { get; set; }   // Bookshelf internal id ("" if not in library)
        public string BookId { get; set; }             // foreignBookId (metadata id) — the stable key
        public string Title { get; set; }
        public string AuthorName { get; set; }
        public string ForeignAuthorId { get; set; }
        public string SeriesTitle { get; set; }
        public int? Year { get; set; }
        public string Overview { get; set; }
        public bool Available { get; set; }            // has a file
        public bool Monitored { get; set; }
        public bool Requested { get; set; }            // in library, no file yet
        public string PosterPath { get; set; }
        public string TitleSlug { get; set; }
    }
}
