using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

namespace Botifex.Services.TelegramBot
{
    /// <summary>
    /// Service to set up interaction with the telegram bot and process incoming messages
    /// </summary>
    internal class TelegramService : Messenger, ITelegram
    {
        private List<TelegramInteraction> activeInteractions = new List<TelegramInteraction>();
        private Dictionary<long, Channel> channelLibrary = new Dictionary<long, Channel>();
        private List<string> adminNames;
        private ILogger<TelegramService> log;

        private bool isReady = false;
        private Message? ongoingStatusMessage { get; set; }

        internal override int MAX_TEXT_LENGTH { get => 4096; }

        /// <summary>
        /// The username of the bot account this service is using to interact with users
        /// </summary>
        internal string BotUsername { get; private set; } = "";

        /// <summary>
        /// Whether the bot is ready to have interactions or not
        /// </summary>
        public override bool IsReady { get => isReady && Bot is not null; protected private set => isReady = value; }

        /// <summary>
        /// The bot client we're using to talk to Telegram
        /// </summary>
        public TelegramBotClient Bot { get; private set; }

        /// <summary>
        /// A <see cref="Channel"/> for logging admin messages to, if specified in configuration
        /// </summary>
        public Channel? LogChannel { get; private set; }

        /// <summary>
        /// A <see cref="Channel"/> for continual status updates to users, if specified in configuration
        /// </summary>
        public Channel? StatusChannel { get; private set; }


#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
        public TelegramService(ILogger<TelegramService> log, IConfiguration cfg, IHostApplicationLifetime lifetime, ICommandLibrary lib) 
#pragma warning restore CS8618 
                : base(lifetime, cfg.GetSection("Telegram"), lib)
        {
            this.log = log;
            this.interactionFactory = new TelegramInteractionFactory(lib);
            
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            // get the bot token and initialize the bot client
            if (String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) return; // TODO: don't do it this way. When this is fixed the warning disable won't be necessary
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken")!);

            // set up the log channel if specified
            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0)
            {
                LogChannel = new Channel(Bot, logChannelId);
                channelLibrary.Add(logChannelId, LogChannel);
            }

            // set up the status channel if specified
            long statusChannelId = config.GetValue<long>("TelegramStatusChannel");
            if (statusChannelId != 0)
            {
                if (statusChannelId != logChannelId)
                {
                    StatusChannel = new Channel(Bot, statusChannelId);
                    channelLibrary.Add(statusChannelId, StatusChannel);
                }
                else
                    StatusChannel = LogChannel;                
            }   

            // take note of the specified names of admins who can use admin commands
            adminNames = config.GetSection("TelegramAdminAllowlist").Get<string[]>()?.ToList() ?? new List<string>();

            try
            {
                // ignore anything that came in before we started
                ReceiverOptions options = new ReceiverOptions();
                options.ThrowPendingUpdates = true;

                // start listening
                Bot.StartReceiving(receiverOptions: options,
                                   updateHandler: OnUpdateReceived,
                                   pollingErrorHandler: OnErrorReceived);

                BotUsername = (Bot.GetMeAsync().Result).Username ?? ""; // note the bot's username (important for command processing in groups)

                IsReady = true;
                FinalizeFirstReady(EventArgs.Empty); // trigger onReady event
            }
            catch (Exception ex)
            {
                log.LogError($"[{DateTime.Now}] {ex.GetType()} - {ex.Message}");
            }
        }

        internal override void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
        }

        internal override void OnStopped()
        { 
            log.LogDebug("OnStopped has been called.");
        }

        /// <inheritdoc/>
        internal override Task Log(string m, LogLevel i = LogLevel.Information)
        {
            // log to console
            log.Log(i, m);

            // if we can log to the log channel
            if (IsReady && LogChannel is not null)
                LogChannel.Send(m);

            return Task.CompletedTask;
        }
        
        // not really implemented
        private Task OnErrorReceived(ITelegramBotClient bot, Exception ex, CancellationToken cToken)
        {
            log.LogError(ex, String.Empty);
            return Task.CompletedTask;
        }

        /// <summary>
        /// All the processing for any update received by the bot client. Telegram does not send different kinds of updates through different events like Discord, so we have to sort them out
        /// </summary>
        /// <param name="bot">The bot client that received the update</param>
        /// <param name="data">Information about the update</param>
        /// <param name="cToken"></param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        private async Task OnUpdateReceived(ITelegramBotClient bot, Update data, CancellationToken cToken)
        {
            // No use case yet for any update where message is null, so ignore
            if (data.Message is null) return;

            // handle a migration to supergroup (send a log error because failure is imminent)
            if (data.Message.Type == MessageType.MigratedToSupergroup)
            {
                long newId = data.Message.MigrateToChatId ?? 0; // these should not be null with this message type though
                string channelName = "STATUS CHANNEL";
                channelLibrary[newId] = new Channel(Bot, newId);

                long oldId = StatusChannel?.Id.Identifier ?? 0;
                StatusChannel = channelLibrary[newId];
                channelLibrary[oldId].Stopping = true; // TODO: Channel should be Disposable

                /* -- TODO: find a way to match the new chat to the old chat, since telegram's MigrateFromChatId isn't working
                 * -- UNTIL THEN: making the bold assumption that this is only happening in the status channel
                 
                channelLibrary[newId] = channelLibrary[oldId].Migrate(newId);
                
                if (StatusChannel?.Id == oldId)
                {
                    StatusChannel = channelLibrary[newId];
                    channelName = "STATUS CHANNEL";
                }
                else if (LogChannel?.Id == oldId)
                {
                    LogChannel = channelLibrary[newId];
                    channelName = "LOG CHANNEL";
                }
                else
                    channelName = data.Message.Chat.Title!;*/

                await Log($"ALERT: UPDATE SETTINGS FILE!! {channelName} HAS CHANGED TO A SUPERGROUP AND ITS ID HAS CHANGED TO {newId}\n\nBot has probably lost privileges in the status channel, expect a crash soon.", LogLevel.Error);
                return;
            }

            // log the channel in the channel library if necessary
            if (!channelLibrary.Keys.Contains(data.Message.Chat.Id))
                channelLibrary[data.Message.Chat.Id] = new Channel(Bot, data.Message.Chat.Id);

            Chat chat = data.Message.Chat;
            Channel channel = channelLibrary[chat.Id];

            // keep groups clear of join/leave/pin messages - TODO: make this a toggleable setting
            if (data.Message.Type == MessageType.ChatMembersAdded || data.Message.Type == MessageType.ChatMemberLeft
                    || data.Message.Type == MessageType.MessagePinned)
            {
                channel.Delete(data.Message.MessageId);
                return;
            }

            // ignore if no text or identifiable user 
            if (data.Message?.From is null || String.IsNullOrEmpty(data.Message?.Text)) return;

            // ignore if in a group chat and not targeted
            if (data.Message.Chat.Type != Telegram.Bot.Types.Enums.ChatType.Private
                && data.Message.Chat.Type != Telegram.Bot.Types.Enums.ChatType.Sender
                && !Regex.Match(data.Message.Text.ToLower(), $"@{BotUsername.ToLower()}(?=\\s|$)").Success)
                return;

            TelegramUser user = new TelegramUser(this, data.Message.From);            

            // if there's an existing command waiting for response already, see if this is related to that       
            TelegramInteraction? existingInteraction = activeInteractions.SingleOrDefault(i => ((TelegramUser)i.Source.User).Account.Id == user.Account.Id && ((Message?)i.Source.Message)?.Chat.Id == chat.Id);
            bool removeExistingInteraction = false;
            
            if (existingInteraction is TelegramCommandInteraction && !String.IsNullOrEmpty((data.Message?.Text ?? "").Trim()))
            {
                // if this is not a command, give it to the command interaction as a response
                if (data.Message?.Text.Trim()[0] != '/')
                {
                    if (existingInteraction.IsProcessing)
                        return;  // they jumped the gun, ignore this

                    // if a menu was presented to the user, handle this update as a response to that menu
                    if(existingInteraction.Menu is not null)
                    {
                        if (existingInteraction.Menu.NumberedChoices)
                            try
                            {
                                existingInteraction.ChooseMenuOption(int.Parse(data.Message!.Text));
                            }
                            catch (Exception ex) when (ex is ArgumentNullException or FormatException or OverflowException or ArgumentOutOfRangeException)
                            {
                                await existingInteraction.Reply("Well I wasn't expecting that");
                                await existingInteraction.End();
                                return;
                            }
                            
                        else
                            existingInteraction.ChooseMenuOption(data.Message!.Text);
                        return;
                    }

                    // otherwise pass it to the interaction as a response to a command field question
                    ((TelegramCommandInteraction)existingInteraction).ReadResponse(data.Message!);

                    // if that was the last command field question, the command is ready to go and the interaction would be ready to finalize
                    if (((TelegramCommandInteraction)existingInteraction).IsReady) { }
                        FinalizeCommandReceived(new InteractionReceivedEventArgs(existingInteraction));

                    channel.Delete(data.Message!.MessageId); // clear out answers to follow up questions
                    return;
                }
                // otherwise this is a new command so flag that the previous incomplete interaction needs to end if we successfully start a new one
                else 
                    removeExistingInteraction = true;
            }                

            // starting a new interaction
            InteractionSource source = new InteractionSource(new TelegramUser(this, data.Message!.From), data.Message);
            try
            {
                TelegramInteraction? newInteraction = (TelegramInteraction?)interactionFactory?.CreateInteraction(source);
                if (newInteraction is null) return; // it didn't work

                if (removeExistingInteraction) // this needs to wait until now to be sure the new interaction creates successfully
                    await existingInteraction!.End();

                activeInteractions.Add(newInteraction); // register this interaction

                channelLibrary[data.Message.Chat.Id].DoTyping(); // display "BotName is typing..." in the chat window

                // handle if this is a command
                if (newInteraction is TelegramCommandInteraction)
                {
                    string username = ((TelegramUser)((TelegramCommandInteraction)newInteraction).Source.User).Account.Username ?? "";

                    // reject this interaction if a non-admin is trying to use an admin command
                    if (((TelegramCommandInteraction)newInteraction).BotifexCommand.AdminOnly && !adminNames.Contains(username))
                    {
                        await Reply(newInteraction, $"Sorry, only specified admins can use that command");
                        await newInteraction.End();
                        return;
                    }                   

                    // finalize the command if it's in the ready state
                    // if not, the interaction object will send them a follow up message and this method will catch the user's reply above
                    if (((TelegramCommandInteraction)newInteraction).IsReady)
                        FinalizeCommandReceived(new InteractionReceivedEventArgs(newInteraction));
                }

                // handle if this is a text message
                else if (newInteraction is TelegramTextInteraction)
                    FinalizeMessageReceived(new InteractionReceivedEventArgs(newInteraction));
            }
            catch (ArgumentException)
            {
                // catch and ignore
            }
        }

        /// <inheritdoc />
        internal override async Task LoadCommands()
        {
            List<SlashCommand> botifexCommands = commandLibrary.Commands;
            List<BotCommand> userCommands = new();
            List<BotCommand> adminCommands = new();

            // create telegram's command objects for each of ours and sort them into admin and non-admin
            foreach( SlashCommand c in botifexCommands )
            {
                BotCommand botCommand = new BotCommand()
                {
                    Command = c.Name,
                    Description = c.Description
                };

                switch (c.AdminOnly)
                {
                    case true:
                        adminCommands.Add(botCommand);
                        break;

                    case false:
                        userCommands.Add(botCommand);
                        break;
                }
            }
            
            // admin commands will show up in the menu for admins in the channels
            if(adminCommands.Count > 0)
            {                
                if (StatusChannel is not null) 
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(StatusChannel.Id));
                
                if (LogChannel is not null) 
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(LogChannel.Id));
            }
            
            // non-admin commands will show up in the menu for everyone in DMs
            await Bot.SetMyCommandsAsync(userCommands, BotCommandScope.AllPrivateChats());
        }

        /// <inheritdoc/>
        internal override Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null || statusText == ongoingStatusMessage?.Text) return Task.CompletedTask;
                                                     // if the text is the same it'll throw an exception
            try
            {
                if (ongoingStatusMessage is null) // make a fresh one
                    StatusChannel.Send(Truncate(statusText), callback: new Action<Message>((m) =>
                    {
                        ongoingStatusMessage = m; // TODO: make this more thread-safe. Can get more than one status message if channel is rate limited
                    }));
                else // update the old one
                {
                    StatusChannel.Edit(ongoingStatusMessage.MessageId, Truncate(statusText));
                    ongoingStatusMessage.Text = Truncate(statusText);
                }      
            }
            catch(Exception) 
            {
                // ignore
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        internal override Task SendOneTimeStatus(string statusText, bool notification = false)
        {
            if (!IsReady || StatusChannel is null) return Task.CompletedTask;

            StatusChannel.Send(statusText, callback: notification == false ? null : new Action<Message>((message) =>
            {
                // one way to force a notification in telegram is to pin a message
                StatusChannel.Pin(message.MessageId, disableNotification: !notification);
                StatusChannel.Unpin(message.MessageId);
            }));
            return Task.CompletedTask;
        }
        
        /// <inheritdoc/>
        internal override Task Reply(Interaction interaction, string text)
        {
            if (!IsReady || interaction.Source.Message is null) return Task.CompletedTask; // can't send a reply so end it

            Message userMessage = (Message)interaction.Source.Message;
            Message? botMessage = (Message?)interaction.BotMessage;
            Channel channel = channelLibrary[userMessage.Chat.Id];

            // if this is a result of a menu selection, clear out the response message so the menu can go away. 
            // It will fall through to sending a new message in the next if/else
            if (interaction.Menu is not null)
            {
                interaction.Menu = null;

                channel.Delete(botMessage!.MessageId); // if there's a menu, there should be a bot message too
                interaction.BotMessage = null;
            }

            // update or create a reply message
            if (interaction.BotMessage is not null)
                channel.Edit(botMessage!.MessageId, Truncate(text), callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            else
                channel.Send(text, replyToMessageId: userMessage.MessageId, callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        internal override Task ReplyWithOptions(Interaction interaction, string? text)
        {
            text = text ?? string.Empty;
            if (!IsReady || interaction.Source.Message is null) return Task.CompletedTask; // can't do anything so don't

            Message userMessage = (Message)interaction.Source.Message;
            Channel channel = channelLibrary[userMessage.Chat.Id];

            // build the telegram menu options from our menu object
            List<KeyboardButton> buttons = new List<KeyboardButton>();
            ReplyKeyboardMarkup? keyboard = null;
            if (interaction.Menu is not null && interaction.Menu.Options.Any())
            {
                for (int i = 0; i < interaction.Menu.Options.Count; i++)
                {
                    // the button should be i+1 if NumberedChoices was true, otherwise use the dictionary key as-is
                    buttons.Add(new KeyboardButton($"{(interaction.Menu.NumberedChoices ? i + 1 : interaction.Menu.Options.Keys.ToArray()[i])}"));
                }
                keyboard = new ReplyKeyboardMarkup(buttons);
                keyboard.ResizeKeyboard = true;
                keyboard.Selective = true;
                keyboard.OneTimeKeyboard = true;

                text += "\n" + interaction.Menu.MenuText;
                text = text.Trim();
            }

            Message? botMessage = (Message?)interaction.BotMessage;
            int messageId = botMessage?.MessageId ?? 0;

            if (botMessage is not null && keyboard is null) // honestly keyboard should not be null in this method
                channel.Edit(messageId, Truncate(text), callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            else // can't edit a keyboard into an existing message, so need to send a new one
                channel.Send(text, replyToMessageId: userMessage.MessageId, markup: keyboard, callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));

            // if we sent a keyboard with this, we had to start fresh so can delete the original message.
            if (keyboard is not null && messageId != 0)
                channel.Delete(messageId);

            return Task.CompletedTask;
        }

        /* Not used currently
        private Task AppendText(Message existingMessage, string text, Action<Message>? callback = null)
        {
            Channel channel = channelLibrary[existingMessage.Chat.Id];

            if ((existingMessage.Text?.Length + text.Length) < (MAX_TEXT_LENGTH - 2))
                channel.Edit(existingMessage.MessageId, existingMessage.Text + "\n" + text, callback: callback);

            else
                channel.Send(text, callback: callback);

            return Task.CompletedTask;
        }
        */

        /// <inheritdoc/>
        internal override Task RemoveInteraction(Interaction i)
        {
            if (i is not TelegramInteraction) throw new ArgumentException();

            TelegramInteraction interaction = (TelegramInteraction)i;
            activeInteractions.Remove(interaction);

            // delete messages with menus or the menu will keep popping up for the user which is annoying
            if (i.BotMessage is not null && i.Menu is not null)
            {
                Message message = (Message)interaction.BotMessage!;
                channelLibrary[message.Chat.Id].Delete(message.MessageId);
            }
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        internal override Task SendMessageToUser(IMessengerUser user, string message)
        {
            if(user is not TelegramUser) throw new ArgumentException();

            long userId = ((TelegramUser)user).Account.Id;

            // log the channel in the channel library if necessary
            if (!channelLibrary.Keys.Contains(userId))
                channelLibrary[userId] = new Channel(Bot, userId);

            channelLibrary[userId].Send(message);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Replace the current status message with specific text, then start over with a new status message below it
        /// </summary>
        /// <param name="text">The text to replace the old message with</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal override Task ReplaceStatus(string text)
        {
            // nothing to update?
            if (ongoingStatusMessage is null || StatusChannel is null || ongoingStatusMessage.Text is null) return Task.CompletedTask;
            int oldStatusId = ongoingStatusMessage.MessageId;

            // make a copy of the old status in the same channel
            StatusChannel.Send(ongoingStatusMessage.Text, disableNotification: true, callback: new Action<Message>((m) =>
            {
                ongoingStatusMessage = m;
            }));

            // replace the text of the old status message
            if(string.IsNullOrEmpty(text))
                StatusChannel.Delete(oldStatusId);
            else
                StatusChannel.Edit(oldStatusId, Truncate(text));

            return Task.CompletedTask;
        }
    }
}
