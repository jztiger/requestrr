namespace Requestrr.WebApi.RequestrrBot.Music
{
    public class MusicArtist
    {
        public string DownloadClientId { get; set; }
        public string ArtistId { get; set; }
        public string ArtistName { get; set; }
        public string Disambiguation { get; set; }
        public string ArtistType { get; set; }
        public string Overview { get; set; }


        public bool Available { get; set; }
        public bool Monitored { get; set; }
        public string Quality { get; set; }
        public bool Requested { get; set; }


        public string PlexUrl { get; set; }
        public string EmbyUrl { get; set; }

        public string PosterPath { get; set; }
    }
}
