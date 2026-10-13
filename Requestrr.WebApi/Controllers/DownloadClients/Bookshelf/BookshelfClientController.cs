using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Requestrr.WebApi.Controllers.DownloadClients.Bookshelf
{
    [ApiController]
    [Authorize]
    [Route("/api/books/bookshelf")]
    public class BookshelfClientController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<BookshelfClient> _logger;

        public BookshelfClientController(IHttpClientFactory httpClientFactory, ILogger<BookshelfClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        /// <summary>Tests one category's Bookshelf connection.</summary>
        [HttpPost("test")]
        public async Task<IActionResult> TestBookshelfSettings([FromBody] TestBookshelfSettingsModel model)
        {
            try
            {
                await BookshelfClient.TestConnectionAsync(_httpClientFactory.CreateClient(), _logger, ConvertToCategory(model));
                return Ok(new { ok = true });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpPost("rootpath")]
        public async Task<IActionResult> GetBookshelfRootPaths([FromBody] TestBookshelfSettingsModel model)
        {
            try
            {
                var paths = await BookshelfClient.GetRootPaths(_httpClientFactory.CreateClient(), _logger, ConvertToCategory(model));
                return Ok(paths.Select(x => new { Id = x.id, Path = x.path }));
            }
            catch (Exception)
            {
                return BadRequest("Could not load the paths form Bookshelf, check your settings.");
            }
        }

        [HttpPost("profile")]
        public async Task<IActionResult> GetBookshelfProfiles([FromBody] TestBookshelfSettingsModel model)
        {
            try
            {
                var profiles = await BookshelfClient.GetProfiles(_httpClientFactory.CreateClient(), _logger, ConvertToCategory(model));
                return Ok(profiles.Select(x => new { Id = x.id, Name = x.name }));
            }
            catch (Exception)
            {
                return BadRequest("Could not load profiles from Bookshelf, check your settings.");
            }
        }

        [HttpPost("metadataprofile")]
        public async Task<IActionResult> GetBookshelfMetadataProfiles([FromBody] TestBookshelfSettingsModel model)
        {
            try
            {
                var profiles = await BookshelfClient.GetMetadataProfiles(_httpClientFactory.CreateClient(), _logger, ConvertToCategory(model));
                return Ok(profiles.Select(x => new { Id = x.id, Name = x.name }));
            }
            catch (Exception)
            {
                return BadRequest("Could not load metadata profiles from Bookshelf, check your settings.");
            }
        }

        [HttpPost("tag")]
        public async Task<IActionResult> GetBookshelfTags([FromBody] TestBookshelfSettingsModel model)
        {
            try
            {
                var tags = await BookshelfClient.GetTags(_httpClientFactory.CreateClient(), _logger, ConvertToCategory(model));
                return Ok(tags.Select(x => new { Id = x.id, Name = x.label }));
            }
            catch (Exception)
            {
                return BadRequest("Could not load tags from Bookshelf, check your settings.");
            }
        }

        /// <summary>Saves the Bookshelf categories (each with its own connection).</summary>
        [HttpPost()]
        public async Task<IActionResult> SaveAsync([FromBody] BookshelfSettingsModel model)
        {
            var error = ValidateSettings(model);
            if (error != null)
                return BadRequest(error);

            DownloadClientsSettingsRepository.SetBookshelf(new BooksSettings { Client = DownloadClient.Bookshelf }, model);
            return Ok(new { ok = true });
        }

        /// <summary>
        /// Validates (and trims, in place) the submitted settings. Returns the error message, or null when valid.
        /// </summary>
        internal static string ValidateSettings(BookshelfSettingsModel model)
        {
            if (model.Categories == null || !model.Categories.Any())
                return "At least one category is required.";

            if (model.Categories.Any(x => string.IsNullOrWhiteSpace(x.Name)))
                return "A category name is required";

            foreach (var category in model.Categories)
            {
                category.Name = category.Name.Trim();
                category.Hostname = category.Hostname?.Trim() ?? string.Empty;
                category.ApiKey = category.ApiKey?.Trim() ?? string.Empty;
                category.BaseUrl = category.BaseUrl?.Trim() ?? string.Empty;
                category.RootFolder = category.RootFolder?.Trim() ?? string.Empty;
            }

            if (new HashSet<string>(model.Categories.Select(x => x.Name.ToLower())).Count != model.Categories.Length)
                return "All categories must have different names.";

            if (new HashSet<int>(model.Categories.Select(x => x.Id)).Count != model.Categories.Length)
                return "All categories must have different ids.";

            if (model.Categories.Any(x => !Regex.IsMatch(x.Name, @"^[\w-]{1,32}$")))
                return "Invalid categorie names, make sure they only contain alphanumeric characters, dashes and underscores. (No spaces, etc)";

            if (model.Categories.Any(x => x.Hostname.Length == 0 || x.ApiKey.Length == 0))
                return "Each book category needs a Bookshelf hostname and API key.";

            return null;
        }

        private static BookshelfCategory ConvertToCategory(TestBookshelfSettingsModel model)
        {
            return new BookshelfCategory
            {
                Hostname = model.Hostname.Trim(),
                ApiKey = model.ApiKey.Trim(),
                BaseUrl = (model.BaseUrl ?? string.Empty).Trim(),
                Port = model.Port,
                UseSSL = model.UseSSL
            };
        }
    }
}
