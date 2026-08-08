using System.Linq;
using Newtonsoft.Json.Linq;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Tautulli;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class TautulliClientTests
    {
        // Field shapes verified against a live Tautulli get_history response on 2026-08-07:
        // rating_key / grandparent_rating_key / media_index / parent_media_index / year are
        // JSON numbers when populated and empty strings ("") when not applicable to the row's
        // media_type (e.g. media_index is "" for movies, grandparent_rating_key is "" for movies).
        private const string HistoryFixture = @"
        {
          ""response"": {
            ""result"": ""success"",
            ""message"": null,
            ""data"": {
              ""recordsFiltered"": 5,
              ""recordsTotal"": 5,
              ""data"": [
                {
                  ""rating_key"": 5613,
                  ""grandparent_rating_key"": """",
                  ""media_type"": ""movie"",
                  ""title"": ""Sweet Home Alabama"",
                  ""grandparent_title"": """",
                  ""year"": 2002,
                  ""media_index"": """",
                  ""parent_media_index"": """"
                },
                {
                  ""rating_key"": 82824,
                  ""grandparent_rating_key"": 82655,
                  ""media_type"": ""episode"",
                  ""title"": ""The Beating"",
                  ""grandparent_title"": ""Lioness"",
                  ""year"": 2023,
                  ""media_index"": 2,
                  ""parent_media_index"": 1
                },
                {
                  ""rating_key"": 6001,
                  ""grandparent_rating_key"": """",
                  ""media_type"": ""movie"",
                  ""title"": ""Another Movie"",
                  ""grandparent_title"": """",
                  ""year"": 2010,
                  ""media_index"": """",
                  ""parent_media_index"": """"
                },
                {
                  ""rating_key"": 82901,
                  ""grandparent_rating_key"": 82700,
                  ""media_type"": ""episode"",
                  ""title"": ""Another Episode"",
                  ""grandparent_title"": ""Another Show"",
                  ""year"": 2021,
                  ""media_index"": 5,
                  ""parent_media_index"": 3
                },
                {
                  ""rating_key"": 9001,
                  ""grandparent_rating_key"": 9000,
                  ""media_type"": ""track"",
                  ""title"": ""Some Song"",
                  ""grandparent_title"": ""Some Artist"",
                  ""year"": """",
                  ""media_index"": 1,
                  ""parent_media_index"": 1
                },
                {
                  ""rating_key"": 6100,
                  ""grandparent_rating_key"": """",
                  ""media_type"": ""movie"",
                  ""title"": ""No Year Movie"",
                  ""grandparent_title"": """",
                  ""year"": """",
                  ""media_index"": """",
                  ""parent_media_index"": """"
                }
              ]
            }
          }
        }";

        private const string MetadataFixture = @"
        {
          ""response"": {
            ""result"": ""success"",
            ""message"": null,
            ""data"": {
              ""rating_key"": 82824,
              ""guids"": [
                ""imdb://tt21291752"",
                ""tmdb://3826097"",
                ""tvdb://9232367""
              ]
            }
          }
        }";

        [Fact]
        public void ParseHistory_DropsRowsThatAreNotMoviesOrEpisodes()
        {
            var apiResponse = JObject.Parse(HistoryFixture);

            var result = TautulliClient.ParseHistory(apiResponse);

            Assert.Equal(5, result.Count);
            Assert.DoesNotContain(result, x => x.RatingKey == "9001");
        }

        [Fact]
        public void ParseHistory_EmptyStringNumericFields_DoNotThrowAndDefaultToZero()
        {
            // Tautulli emits "" (not null/absent) for not-applicable numeric fields,
            // e.g. year on a movie row with no known release year.
            var apiResponse = JObject.Parse(HistoryFixture);

            var result = TautulliClient.ParseHistory(apiResponse);
            var movie = result.Single(x => x.RatingKey == "6100");

            Assert.True(movie.IsMovie);
            Assert.Equal(0, movie.Year);
        }

        [Fact]
        public void ParseHistory_ParsesMovieFields()
        {
            var apiResponse = JObject.Parse(HistoryFixture);

            var result = TautulliClient.ParseHistory(apiResponse);
            var movie = result.Single(x => x.RatingKey == "5613");

            Assert.True(movie.IsMovie);
            Assert.Equal("Sweet Home Alabama", movie.Title);
            Assert.Null(movie.GrandparentRatingKey);
            Assert.Null(movie.GrandparentTitle);
            Assert.Equal(2002, movie.Year);
            Assert.Equal(0, movie.SeasonNumber);
            Assert.Equal(0, movie.EpisodeNumber);
            Assert.Equal("Sweet Home Alabama (2002)", movie.Display);
        }

        [Fact]
        public void ParseHistory_ParsesEpisodeFields()
        {
            var apiResponse = JObject.Parse(HistoryFixture);

            var result = TautulliClient.ParseHistory(apiResponse);
            var episode = result.Single(x => x.RatingKey == "82824");

            Assert.False(episode.IsMovie);
            Assert.Equal("The Beating", episode.Title);
            Assert.Equal("82655", episode.GrandparentRatingKey);
            Assert.Equal("Lioness", episode.GrandparentTitle);
            Assert.Equal(0, episode.Year);
            Assert.Equal(1, episode.SeasonNumber);
            Assert.Equal(2, episode.EpisodeNumber);
            Assert.Equal("Lioness — S01E02 · The Beating", episode.Display);
        }

        [Fact]
        public void ParseGuids_ReturnsAllThreeGuidForms()
        {
            var apiResponse = JObject.Parse(MetadataFixture);

            var result = TautulliClient.ParseGuids(apiResponse);

            Assert.Equal(new[] { "imdb://tt21291752", "tmdb://3826097", "tvdb://9232367" }, result);
        }
    }
}
