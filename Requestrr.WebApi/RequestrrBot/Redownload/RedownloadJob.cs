using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Requestrr.WebApi.RequestrrBot.Redownload
{
    public enum RedownloadJobState { Searching, Completed, Restored, RestoreFailed }

    public class RedownloadJob
    {
        public Guid Id { get; set; }
        public bool IsMovie { get; set; }

        public int ArrId { get; set; }
        public int SeriesId { get; set; }
        public int OldFileId { get; set; }

        public string DisplayTitle { get; set; }

        public string OldFilePath { get; set; }
        public string OldFileName { get; set; }   // basename, restore match key

        public string OldQuality { get; set; }
        public long OldSizeBytes { get; set; }
        public string OldReleaseGroup { get; set; }

        public long? BlocklistedHistoryId { get; set; }   // null = history was pruned

        public ulong ChannelId { get; set; }
        public ulong MessageId { get; set; }               // public status message

        public ulong RequestedByUserId { get; set; }
        public string RequestedByUsername { get; set; }

        public DateTime CreatedUtc { get; set; }
        public DateTime DeadlineUtc { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public RedownloadJobState State { get; set; }
    }
}
