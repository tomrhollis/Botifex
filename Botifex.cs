using Botifex.Models;
using Botifex.Services;
using Botifex.Services.Discord;
using Botifex.Services.TelegramBot;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex
{
    /// <summary>
    /// Initializer and coordinator of the Botifex system. Registers commands and events from the calling program and passes requests back to it.
    /// The eventual aim is to completely abstract away all the messengers supported by Botifex (currently Discord and Telegram)
    /// </summary>
    public class Botifex : IBotifex 
    {
        private IHostApplicationLifetime appLifetime;
        private IConfiguration config;
        private ILogger<Botifex> log;
        private ICommandLibrary commandLibrary;

        private EventHandler<InteractionReceivedEventArgs>? onCommand;
        private EventHandler<InteractionReceivedEventArgs>? onText;
        private EventHandler<UserUpdateEventArgs>? onUserUpdate;
        private EventHandler<EventArgs>? onReady;

        private Messenger[] messengers;
        private List<BotifexUser> knownUsers = new List<BotifexUser>();

        public Botifex(IHostApplicationLifetime lifetime, IConfiguration cfg, ILogger<Botifex> log, ICommandLibrary lib, IDiscord dc, ITelegram tg)
        {
            appLifetime = lifetime;
            config = cfg;
            this.log = log;
            commandLibrary = lib;
            messengers = new Messenger[] { (DiscordService)dc, (TelegramService)tg };

            // respond to application lifetime events
            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            onReady += DoReadyTasks;
            if (onReady != null)
                // register tasks to complete when they're ready with the messengers
                foreach (var messenger in messengers)
                    messenger.OnFirstReady += onReady;
        }

        private void OnStarted()
        {
#if DEBUG
            log.LogDebug("OnStarted has been called.");
#endif

            if (onCommand != null) // register command handler with the messengers
                foreach (var messenger in messengers)
                    messenger.OnCommandReceived += CommandReceived;

            if (onText != null) // register text handler with the messengers
                foreach (var messenger in messengers)
                    messenger.OnMessageReceived += MessageReceived;
        }

        private void OnStopping()
        {
#if DEBUG
            log.LogDebug("Botifex is shutting down");
            log.LogDebug("OnStopping has been called.");
#endif
        }

        private void OnStopped()
        {
#if DEBUG
            log.LogDebug("OnStopped has been called.");
#endif
        }

        /// <summary>
        /// Send a log message to the messengers, which will post to their specified Log channel, if it exists
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task LogAll(string message)
        {
            await Task.WhenAll(messengers.Select((m)=>m.Log(message, LogLevel.Information)));
        }        

        /// <summary>
        /// Assign the text handler
        /// </summary>
        /// <param name="handler"></param>
        public void RegisterTextHandler(EventHandler<InteractionReceivedEventArgs> handler)
        {
            onText = handler;
        }

        /// <summary>
        /// Assign the command handler
        /// </summary>
        /// <param name="handler"></param>
        public void RegisterCommandHandler(EventHandler<InteractionReceivedEventArgs> handler)
        {
            onCommand = handler;
        }

        /// <summary>
        /// Assign the handler for when messengers are ready
        /// </summary>
        /// <param name="handler"></param>
        public void RegisterReadyHandler(EventHandler<EventArgs> handler)
        {
            onReady = handler;
        }

        /// <summary>
        /// Assign the handler for when a user's info is found to have been updated
        /// </summary>
        /// <param name="handler"></param>
        public void RegisterUserUpdateHandler(EventHandler<UserUpdateEventArgs> handler)
        {
            onUserUpdate = handler;
        }

        /// <summary>
        /// Add a <see cref="SlashCommand"/> to the <see cref="commandLibrary"/>
        /// </summary>
        /// <param name="command"></param>
        public void AddCommand(SlashCommand command)
        {
            commandLibrary.RegisterCommand(command);
        }

        /// <summary>
        /// Perform tasks that need to wait for a messenger to be loaded and ready to go. Currently just pushing the commands.
        /// </summary>
        /// <param name="sender">The initiator of this event (a <see cref="Messenger"/>)</param>
        /// <param name="e">Unused</param>
        private async void DoReadyTasks(object? sender, EventArgs e)
        {
            if (sender is null) return;
            try
            {
                await PushCommands((Messenger)sender);
            }
            catch(Exception ex)
            {
                log.LogError($"[{DateTime.Now}] {ex.GetType()} - {ex.Message}");
            }            
        }

        /// <summary>
        /// Tell a messenger to load the command library
        /// </summary>
        /// <param name="m">The <see cref="Messenger"/> that should load commands</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task PushCommands(Messenger m)
        {
            await m.LoadCommands();
        }

        /// <summary>
        /// Update the status message in the status chanel
        /// </summary>
        /// <param name="message">The new text of the status message</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task SendStatusUpdate(string message)
        {
            await Task.WhenAll(messengers.Select((m) => m.CreateOrUpdateStatus(message)));
        }

        /// <summary>
        /// Send a single message to the status channel, not replacing the continually updated status message
        /// </summary>
        /// <param name="message">The text of the message to send</param>
        /// <param name="notification">Whether to create a notification if possible</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task SendOneTimeStatusUpdate(string message, bool notification = false)
        {
            await Task.WhenAll(messengers.Select((m) => m.SendOneTimeStatus(message, notification)));
        }

        /// <summary>
        /// Replace the continually updated status message with a one time message, then repost the continually updated status message
        /// </summary>
        /// <param name="newMessage">The text of the message to send</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task ReplaceStatusMessage(string newMessage)
        {
            await Task.WhenAll(messengers.Select((m) => m.ReplaceStatus(newMessage)));
        }

        /// <summary>
        /// Find the <see cref="BotifexUser"/> that owns a specific <see cref="IMessengerUser"/> account
        /// </summary>
        /// <param name="messengerAccount">The account to find</param>
        /// <returns>The <see cref="BotifexUser"/> that owns it</returns>
        public BotifexUser? GetUser(IMessengerUser messengerAccount)
        {
            return knownUsers.Find((u) => u.Accounts.Contains(messengerAccount));
        }

        /// <summary>
        /// Send a message to a specific user's primary account
        /// </summary>
        /// <param name="user">The <see cref="BotifexUser"/> to send the message to</param>
        /// <param name="message">The message to send</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public async Task SendToUser(BotifexUser? user, string message)
        {
            if(user is null)
            {
                await LogAll("Message to non-Botifex user: " + message);
            }
            else
            {
                await user.Accounts[0].Messenger.SendMessageToUser(user.Accounts[0], message);
            }
        }

        /// <summary>
        /// Keep track of the users we've seen already, return an existing user or a new user as needed
        /// </summary>
        /// <param name="i">The <see cref="Interaction"/> initiated by this user</param>
        /// <returns>The <see cref="BotifexUser"/> associated with this interaction</returns>
        private BotifexUser CreateOrFindUser(Interaction i)
        {
            // find a known user that owns the messenger account associated with the interaction, if that known user exists
            BotifexUser? user = knownUsers.FirstOrDefault(u => u.Accounts.FirstOrDefault(a => a.Id == i.Source.User.Id) is not null);

            if (user is null) // create a BotifexUser for them if they haven't interacted with this system before
            {
                user = new BotifexUser(i.Source.User);
                knownUsers.Add(user);
            }

            else // otherwise see if their info has changed and update user info if necessary (allow for name changes)
            {
                int index = user.Accounts.FindIndex((a) => a.Id == i.Source.User.Id);
                if (user.Accounts[index].Name != i.Source.User.Name
                    || user.Accounts[index].At != i.Source.User.At)
                {
                    user.Accounts[index] = i.Source.User; // refresh the messenger user account object attached to this BotifexUser

                    // trigger the user info update event
                    EventHandler<UserUpdateEventArgs>? handler = onUserUpdate;
                    if (handler is not null)
                    {
                        handler(this, new UserUpdateEventArgs(user));
                    }
                }            
            }
            return user;
        }

        /// <summary>
        /// assign a botifex user to the interaction and call the registered event for text messages
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void MessageReceived(object? sender, InteractionReceivedEventArgs e)
        {
            e.Interaction.User = CreateOrFindUser(e.Interaction);

            EventHandler<InteractionReceivedEventArgs>? handler = onText;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// assign a botifex user to the interaction and call the registered event for commands
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        internal void CommandReceived(object? sender, InteractionReceivedEventArgs e)
        {
            e.Interaction.User = CreateOrFindUser(e.Interaction);

            EventHandler<InteractionReceivedEventArgs>? handler = onCommand;
            if (handler is not null)
            {
                handler(this, e);
            }
        }
    }

    public class UserUpdateEventArgs : EventArgs
    {
        public BotifexUser User { get; set; }
        public UserUpdateEventArgs(BotifexUser u)
        {
            User = u;
        }
    }
}