using DSharpPlus;
using DSharpPlus.Entities;
using Microsoft.Extensions.Logging;
using Requestrr.WebApi.RequestrrBot.ChatClients.Discord;
using Requestrr.WebApi.RequestrrBot.DownloadClients;
using Requestrr.WebApi.RequestrrBot.DownloadClients.Bookshelf;
using Requestrr.WebApi.RequestrrBot.Notifications;
using Requestrr.WebApi.RequestrrBot.Notifications.Books;
using System;
using System.Linq;

namespace Requestrr.WebApi.RequestrrBot.Books
{
    public class BookWorkflowFactory
    {
        private readonly DiscordSettingsProvider _settingsProvider;
        private readonly BookNotificationsRepository _notificationsRepository;
        private BookshelfClient _bookshelfClient;


        public BookWorkflowFactory(
            DiscordSettingsProvider settingsProvider,
            BookNotificationsRepository bookNotificationsRepository,
            BookshelfClient bookshelfClient
        )
        {
            _settingsProvider = settingsProvider;
            _notificationsRepository = bookNotificationsRepository;
            _bookshelfClient = bookshelfClient;
        }


        public BookRequestingWorkflow CreateRequestingWorkflow(DiscordInteraction interaction, int categoryId)
        {
            DiscordSettings settings = _settingsProvider.Provide();
            return new BookRequestingWorkflow(
                new BookUserRequester(
                    interaction.User.Id.ToString(),
                    interaction.User.Username
                    ),
                categoryId,
                GetBookClient<IBookSearcher>(settings),
                GetBookClient<IBookRequester>(settings),
                new DiscordBookUserInterface(interaction, GetBookClient<IBookSearcher>(settings)),
                CreateBookNotificationWorkflow(interaction, settings)
                );
        }



        public IBookNotificationWorkflow CreateNotificationWorkflow(DiscordInteraction interaction)
        {
            DiscordSettings settings = _settingsProvider.Provide();
            return CreateBookNotificationWorkflow(interaction, settings);
        }


        public BookNotificationEngine CreateBookNotificationEngine(DiscordClient client, ILogger logger)
        {
            DiscordSettings settings = _settingsProvider.Provide();
            IBookNotifier bookNotifier = null;

            if (settings.NotificationMode == NotificationMode.PrivateMessage)
                bookNotifier = new PrivateMessageBookNotifier(client, logger);
            else if (settings.NotificationMode == NotificationMode.Channels)
                bookNotifier = new ChannelBookNotifier(client, settings.NotificationChannels.Select(x => ulong.Parse(x)).ToArray(), logger);
            else
                throw new Exception($"Could not create book notifier of type \"{settings.NotificationMode}\"");

            return new BookNotificationEngine(GetBookClient<IBookSearcher>(settings), bookNotifier, logger, _notificationsRepository);
        }



        private IBookNotificationWorkflow CreateBookNotificationWorkflow(DiscordInteraction interaction, DiscordSettings settings)
        {
            DiscordBookUserInterface userInterface = new DiscordBookUserInterface(interaction, GetBookClient<IBookSearcher>(settings));
            IBookNotificationWorkflow bookNotificationWorkflow = new DisabledBookNotificationWorkflow(userInterface);

            if (settings.NotificationMode != NotificationMode.Disabled)
                bookNotificationWorkflow = new BookNotificationWorkflow(_notificationsRepository, userInterface, GetBookClient<IBookSearcher>(settings), settings.AutomaticallyNotifyRequesters);

            return bookNotificationWorkflow;
        }



        private T GetBookClient<T>(DiscordSettings settings) where T : class
        {
            if (settings.BookDownloadClient == DownloadClient.Bookshelf)
            {
                return _bookshelfClient as T;
            }

            throw new Exception($"Invalid configured book download client {settings.BookDownloadClient}");
        }
    }
}
