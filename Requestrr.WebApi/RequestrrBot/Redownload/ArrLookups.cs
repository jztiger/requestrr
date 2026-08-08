using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public interface ISonarrRedownloadLookup
    {
        // series JSON by tvdbId; null if not in Sonarr. Caller reads seriesType/path/id.
        Task<SonarrSeriesInfo> FindSeriesByTvdbIdAsync(int tvdbId);
        Task<SonarrEpisodeInfo> FindEpisodeAsync(int seriesId, int season, int episode); // null if absent
        Task<MediaFileInfo> GetEpisodeFileAsync(int episodeFileId);                      // null if absent
    }

    public interface IRadarrRedownloadLookup
    {
        Task<RadarrMovieInfo> FindMovieByTmdbIdAsync(int tmdbId);   // null if not in Radarr
        Task<MediaFileInfo> GetMovieFileAsync(int movieFileId);
    }

    public class SonarrSeriesInfo
    {
        public int Id { get; set; }
        public string SeriesType { get; set; }
        public string Path { get; set; }
        public string Title { get; set; }
    }

    public class SonarrEpisodeInfo
    {
        public int Id { get; set; }
        public int EpisodeFileId { get; set; }
        public bool Monitored { get; set; }
    }

    public class RadarrMovieInfo
    {
        public int Id { get; set; }
        public int MovieFileId { get; set; }
        public bool Monitored { get; set; }
        public string Title { get; set; }
    }

    public class MediaFileInfo
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public string Quality { get; set; }
        public long SizeBytes { get; set; }
        public string ReleaseGroup { get; set; }
    }
}
