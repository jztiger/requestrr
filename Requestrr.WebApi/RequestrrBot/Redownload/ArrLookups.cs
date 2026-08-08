using System.Collections.Generic;
using System.Threading.Tasks;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public interface ISonarrRedownloadLookup
    {
        // series JSON by tvdbId; null if not in Sonarr. Caller reads seriesType/path/id.
        Task<SonarrSeriesInfo> FindSeriesByTvdbIdAsync(int tvdbId);
        Task<SonarrEpisodeInfo> FindEpisodeAsync(int seriesId, int season, int episode); // null if absent
        Task<MediaFileInfo> GetEpisodeFileAsync(int episodeFileId);                      // null if absent

        // Fallback for items with no usable tvdb GUID (old metadata agent). The resolver decides
        // ambiguity: exactly 1 match proceeds, 0 → "not managed by Sonarr/Radarr", 2+ → "couldn't
        // be matched confidently".
        Task<IReadOnlyList<SonarrSeriesInfo>> FindSeriesByTitleAsync(string title);
    }

    public interface IRadarrRedownloadLookup
    {
        Task<RadarrMovieInfo> FindMovieByTmdbIdAsync(int tmdbId);   // null if not in Radarr
        Task<MediaFileInfo> GetMovieFileAsync(int movieFileId);

        // Fallback for items with no usable tmdb GUID (old metadata agent). Same 1/0/2+ rule as
        // FindSeriesByTitleAsync.
        Task<IReadOnlyList<RadarrMovieInfo>> FindMoviesByTitleYearAsync(string title, int year);
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
