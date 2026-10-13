using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Requestrr.WebApi.config;
using Requestrr.WebApi.Controllers.DownloadClients.Bookshelf;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Radarr;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Sonarr;
using Requestrr.WebApi.RequestrrBot.Locale;
using Requestrr.WebApi.RequestrrBot.Movies;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Lidarr;
using Requestrr.WebApi.RequestrrBot.TvShows;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Requestrr.WebApi.Controllers.DownloadClients
{
    [ApiController]
    [Authorize]
    [Route("/api/books")]
    public class BooksDownloadClientController : ControllerBase
    {
        private readonly BooksSettings _booksSettings;
        private readonly MoviesSettings _moviesSettings;
        private readonly TvShowsSettings _tvShowsSettings;
        private readonly DownloadClientsSettings _downloadClientsSettings;
        private readonly IHttpClientFactory _httpClientFactory;

        public BooksDownloadClientController(
            IHttpClientFactory httpClientFactory,
            BooksSettingsProvider booksSettingsProvider,
            MoviesSettingsProvider moviesSettingsProvider,
            TvShowsSettingsProvider tvShowsSettingsProvider,
            DownloadClientsSettingsProvider downloadClientsSettingsProvider )
        {
            _httpClientFactory = httpClientFactory;
            _booksSettings = booksSettingsProvider.Provide();
            _moviesSettings = moviesSettingsProvider.Provide();
            _tvShowsSettings = tvShowsSettingsProvider.Provide();
            _downloadClientsSettings = downloadClientsSettingsProvider.Provide();
        }


        [HttpGet()]
        public async Task<IActionResult> GetAsync()
        {
            List<string> otherCategories = new List<string>();
            switch (_moviesSettings.Client)
            {
                case "Radarr":
                    foreach (RadarrCategory category in _downloadClientsSettings.Radarr.Categories)
                    {
                        otherCategories.Add(category.Name.ToLower());
                    }
                    break;
                case "Overseerr":
                    foreach (RequestrrBot.DownloadClients.Overseerr.OverseerrMovieCategory category in _downloadClientsSettings.Overseerr.Movies.Categories)
                    {
                        otherCategories.Add(category.Name.ToLower());
                    }
                    if (otherCategories.Count == 0)
                        otherCategories.Add(Language.Current.DiscordCommandMovieRequestTitleName.ToLower());
                    break;
                case "Ombi":
                    otherCategories.Add(Language.Current.DiscordCommandMovieRequestTitleName.ToLower());
                    break;
            }

            switch (_tvShowsSettings.Client)
            {
                case "Sonarr":
                    foreach (SonarrCategory category in _downloadClientsSettings.Sonarr.Categories)
                    {
                        otherCategories.Add(category.Name.ToLower());
                    }
                    break;
                case "Overseerr":
                    foreach (RequestrrBot.DownloadClients.Overseerr.OverseerrTvShowCategory category in _downloadClientsSettings.Overseerr.TvShows.Categories)
                    {
                        otherCategories.Add(category.Name.ToLower());
                    }
                    if (otherCategories.Count == 0)
                        otherCategories.Add(Language.Current.DiscordCommandTvRequestTitleName.ToLower());
                    break;
                case "Ombi":
                    otherCategories.Add(Language.Current.DiscordCommandTvRequestTitleName.ToLower());
                    break;
            }

            foreach (var category in _downloadClientsSettings.Lidarr.Categories)
            {
                otherCategories.Add(category.Name.ToLower());
            }

            return Ok(new BooksSettingsModel
            {
                Client = _booksSettings.Client,
                Bookshelf = new BookshelfSettingsModel
                {
                    Version = _downloadClientsSettings.Bookshelf.Version,
                    Categories = _downloadClientsSettings.Bookshelf.Categories.Select(x => new BookshelfSettingsCategory
                    {
                        Id = x.Id,
                        Name = x.Name,
                        Hostname = x.Hostname,
                        Port = x.Port,
                        ApiKey = x.ApiKey,
                        BaseUrl = x.BaseUrl,
                        UseSSL = x.UseSSL,
                        ProfileId = x.ProfileId,
                        MetadataProfileId = x.MetadataProfileId,
                        RootFolder = x.RootFolder,
                        Tags = x.Tags,
                        SearchNewRequests = x.SearchNewRequests,
                        MonitorNewRequests = x.MonitorNewRequests
                    }).ToArray()
                },
                OtherCategories = otherCategories.ToArray()
            });
        }


        [HttpPost("disable")]
        public async Task<IActionResult> SaveAsync()
        {
            _booksSettings.Client = DownloadClient.Disabled;
            DownloadClientsSettingsRepository.SetDisabledClient(_booksSettings);
            return Ok(new { ok = true });
        }
    }
}
