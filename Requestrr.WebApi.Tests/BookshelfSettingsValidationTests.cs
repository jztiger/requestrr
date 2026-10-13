using Requestrr.WebApi.Controllers.DownloadClients.Bookshelf;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookshelfSettingsValidationTests
    {
        private static BookshelfSettingsCategory Cat(int id, string name, string host = "192.168.1.37", string key = "abc") =>
            new BookshelfSettingsCategory { Id = id, Name = name, Hostname = host, ApiKey = key };

        private static BookshelfSettingsModel Model(params BookshelfSettingsCategory[] cats) =>
            new BookshelfSettingsModel { Categories = cats, Version = "1" };

        [Fact]
        public void Valid_input_returns_null_and_trims()
        {
            var m = Model(Cat(0, " audiobook ", " host ", " key "), Cat(1, "ebook"));
            Assert.Null(BookshelfClientController.ValidateSettings(m));
            Assert.Equal("audiobook", m.Categories[0].Name);
            Assert.Equal("host", m.Categories[0].Hostname);
            Assert.Equal("key", m.Categories[0].ApiKey);
        }

        [Fact]
        public void No_categories() =>
            Assert.Equal("At least one category is required.", BookshelfClientController.ValidateSettings(Model()));

        [Fact]
        public void Empty_name() =>
            Assert.Equal("A category name is required", BookshelfClientController.ValidateSettings(Model(Cat(0, "  "))));

        [Fact]
        public void Duplicate_names_case_insensitive() =>
            Assert.Equal("All categories must have different names.", BookshelfClientController.ValidateSettings(Model(Cat(0, "ebook"), Cat(1, "EBook"))));

        [Fact]
        public void Duplicate_ids() =>
            Assert.Equal("All categories must have different ids.", BookshelfClientController.ValidateSettings(Model(Cat(0, "ebook"), Cat(0, "audiobook"))));

        [Theory]
        [InlineData("has space")]
        [InlineData("bad!")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void Bad_name(string name) =>
            Assert.StartsWith("Invalid categorie names", BookshelfClientController.ValidateSettings(Model(Cat(0, name))));

        [Theory]
        [InlineData("", "key")]
        [InlineData("  ", "key")]
        [InlineData("host", "")]
        [InlineData("host", null)]
        public void Missing_hostname_or_key(string host, string key) =>
            Assert.Equal("Each book category needs a Bookshelf hostname and API key.", BookshelfClientController.ValidateSettings(Model(Cat(0, "ebook", host, key))));
    }
}
