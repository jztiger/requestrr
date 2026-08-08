using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class SmokeTest
    {
        [Fact]
        public void MainAssembly_IsReferenced()
        {
            Assert.NotNull(typeof(Requestrr.WebApi.RequestrrBot.SettingsFile).Assembly);
        }
    }
}
