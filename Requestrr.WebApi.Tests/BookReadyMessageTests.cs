using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookReadyMessageTests
    {
        private static readonly Book B = new Book { Title = "Dune", AuthorName = "Frank Herbert" };

        [Fact]
        public void Format_IncludesCategoryName() =>
            Assert.Equal("📚 **Dune** by Frank Herbert is now available (Audiobooks).", BookReadyMessage.Format(B, "Audiobooks"));

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Format_NoCategory_OmitsSuffix(string name) =>
            Assert.Equal("📚 **Dune** by Frank Herbert is now available.", BookReadyMessage.Format(B, name));
    }
}
