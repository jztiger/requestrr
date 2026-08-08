using Newtonsoft.Json.Linq;

namespace Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli
{
    public class TautulliSettingsProvider
    {
        public TautulliSettings Provide()
        {
            try
            {
                dynamic settings = SettingsFile.Read();
                JObject tautulliSection = settings?.DownloadClients?.Tautulli;

                if (tautulliSection == null)
                {
                    return new TautulliSettings();
                }

                return new TautulliSettings
                {
                    Hostname = (string)tautulliSection["Hostname"] ?? string.Empty,
                    Port = (int?)tautulliSection["Port"] ?? 0,
                    ApiKey = (string)tautulliSection["ApiKey"] ?? string.Empty,
                    UseSSL = (bool?)tautulliSection["UseSSL"] ?? false,
                };
            }
            catch
            {
                return new TautulliSettings();
            }
        }
    }
}
