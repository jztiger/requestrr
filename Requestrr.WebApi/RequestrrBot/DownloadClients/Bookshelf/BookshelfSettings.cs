using System;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf
{
    public class BookshelfSettings
    {
        public BookshelfCategory[] Categories { get; set; } = Array.Empty<BookshelfCategory>();
        public string Version { get; set; } = "1";
    }

    /// <summary>One Bookshelf instance + the profile to add books with. Name doubles as the slash-command name.</summary>
    public class BookshelfCategory
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Hostname { get; set; } = string.Empty;
        public int Port { get; set; } = 8787;
        public string ApiKey { get; set; } = string.Empty;
        public string BaseUrl { get; set; } = string.Empty;
        public bool UseSSL { get; set; }
        public int ProfileId { get; set; } = 1;
        public int MetadataProfileId { get; set; } = 1;
        public string RootFolder { get; set; } = string.Empty;
        public int[] Tags { get; set; } = Array.Empty<int>();
        public bool SearchNewRequests { get; set; } = true;
        public bool MonitorNewRequests { get; set; } = true;
    }
}
