namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public class RedownloadTarget
    {
        public bool IsMovie { get; set; }
        public int ArrId { get; set; }          // episodeId or movieId
        public int SeriesId { get; set; }        // 0 for movies
        public int FileId { get; set; }          // episodeFileId or movieFileId
        public bool Monitored { get; set; }
        public string DisplayTitle { get; set; }
        public string FilePath { get; set; }
        public string Quality { get; set; }
        public long SizeBytes { get; set; }
        public string ReleaseGroup { get; set; }
    }
}
