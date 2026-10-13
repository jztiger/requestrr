using System.Reflection;
using System.Runtime.CompilerServices;
using DSharpPlus.Entities;
using Requestrr.WebApi.RequestrrBot.Books;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Xunit;

namespace Requestrr.WebApi.Tests
{
    public class BookWorkflowFactoryTests
    {
        // Seam: the internal CreateBookNotificationWorkflow (called by CreateRequestingWorkflow with its categoryId)
        // plus the internal DiscordBookUserInterface.CategoryId; the workflow's private _ui is read by reflection.
        // A DiscordInteraction can't be built in a test, so an uninitialized instance stands in (never dereferenced).
        private static int UiCategory(IBookNotificationWorkflow wf)
        {
            var ui = (DiscordBookUserInterface)wf.GetType().GetField("_ui", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(wf);
            return ui.CategoryId;
        }

        private static (BookWorkflowFactory f, DiscordInteraction i, DiscordSettings s) Make()
        {
            var f = new BookWorkflowFactory(null, null, null);
            var i = (DiscordInteraction)RuntimeHelpers.GetUninitializedObject(typeof(DiscordInteraction));
            var s = new DiscordSettings { BookDownloadClient = DownloadClient.Bookshelf, NotificationMode = "PrivateMessage" };
            return (f, i, s);
        }

        [Fact]
        public void NotificationWorkflow_ui_receives_request_category()
        {
            var (f, i, s) = Make();
            Assert.Equal(1, UiCategory(f.CreateBookNotificationWorkflow(i, s, 1)));
        }

        [Fact]
        public void NotificationWorkflow_defaults_to_minus_one()
        {
            var (f, i, s) = Make();
            Assert.Equal(-1, UiCategory(f.CreateBookNotificationWorkflow(i, s)));
        }
    }
}
