namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class WatchedItem
    {
        public string RatingKey { get; set; }
        public string GrandparentRatingKey { get; set; }
        public bool IsMovie { get; set; }
        public string Title { get; set; }
        public string GrandparentTitle { get; set; }
        public int Year { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }

        public string Display =>
            IsMovie ? $"{Title} ({Year})"
                    : $"{GrandparentTitle} — S{SeasonNumber:00}E{EpisodeNumber:00} · {Title}";
    }
}
