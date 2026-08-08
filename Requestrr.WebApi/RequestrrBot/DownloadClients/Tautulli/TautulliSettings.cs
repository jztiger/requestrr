namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli
{
    public class TautulliSettings
    {
        public string Hostname { get; set; } = string.Empty;
        public int Port { get; set; }
        public string ApiKey { get; set; } = string.Empty;
        public bool UseSSL { get; set; }
    }
}
