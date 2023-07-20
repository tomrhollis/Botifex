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
    internal class TelegramService : Messenger, ITelegram
    {
        private bool isReady = false;
        public override bool IsReady { get => isReady && Bot is not null; protected private set=>isReady=value; }
        public TelegramBotClient Bot { get; private set; }
        public Channel? LogChannel { get; private set; }
        public Channel? StatusChannel { get; private set; }
        private Message? OngoingStatusMessage { get; set; }

        private List<TelegramInteraction> activeInteractions = new List<TelegramInteraction>();
        private Dictionary<long, Channel> channelLibrary = new Dictionary<long, Channel>();

        private List<string> adminNames;
        
        internal override int MAX_TEXT_LENGTH { get => 4096; }
        private ILogger<TelegramService> log;
        internal string BotUsername { get; private set; } = "";

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
                               // supporessed because the fields it's worried about ARE assigned in this constructor, not sure why it's complaining.
        public TelegramService(ILogger<TelegramService> log, IConfiguration cfg, IHostApplicationLifetime lifetime, ICommandLibrary lib) 
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
                : base(lifetime, cfg.GetSection("Telegram"), lib)
        {
            this.log = log;
            this.interactionFactory = new TelegramInteractionFactory(lib);
            
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            if (String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) return;
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken")!);

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0)
            {
                LogChannel = new Channel(Bot, logChannelId);
                channelLibrary.Add(logChannelId, LogChannel);
            }

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

                BotUsername = (Bot.GetMeAsync().Result).Username ?? "";

                IsReady = true;
                FinalizeFirstReady(EventArgs.Empty);
            }
            catch (Exception ex)
            {
                log.LogError($"[{DateTime.Now}] {ex.GetType()} - {ex.Message}");
            }
        }

        internal override /*async*/ void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
        }

        internal override void OnStopped()
        { 
            log.LogDebug("OnStopped has been called.");
        }

        internal override Task Log(string m, LogLevel i = LogLevel.Information)
        {
            // log to console
            log.Log(i, m);

            // if we can log to the log channel
            if (IsReady && LogChannel is not null)
                LogChannel.Send(m);

            return Task.CompletedTask;
        }

        private Task OnErrorReceived(ITelegramBotClient bot, Exception ex, CancellationToken cToken)
        {
            log.LogError(ex, String.Empty);
            return Task.CompletedTask;
        }

        private async Task OnUpdateReceived(ITelegramBotClient bot, Update data, CancellationToken cToken)
        {
            if (data.Message is null) return;

            // handle a migration to supergroup
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

                await Log($"ALERT: UPDATE SETTINGS FILE!! {channelName} ID HAS CHANGED TO {newId}\n\nBot has probably lost privileges in the status channel, expect a crash soon.", LogLevel.Error);
                return;
            }

            // log the channel in the channel library if necessary
            if (!channelLibrary.Keys.Contains(data.Message.Chat.Id))
                channelLibrary[data.Message.Chat.Id] = new Channel(Bot, data.Message.Chat.Id);

            Chat chat = data.Message.Chat;
            Channel channel = channelLibrary[chat.Id];

            // keep groups clear of join/leave messages - TODO: make this a toggleable setting
            if (data.Message.Type == MessageType.ChatMembersAdded || data.Message.Type == MessageType.ChatMemberLeft
                    || data.Message.Type == MessageType.MessagePinned)
            {
                channel.Delete(data.Message.MessageId);
                return;
            }

            // ignore if no identifiable user
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

                    if(existingInteraction.Menu is not null)
                    {
                        if (existingInteraction.Menu.NumberedChoices)
                            try
                            {
                                existingInteraction.ChooseMenuOption(int.Parse(data.Message!.Text));
                            }
                            catch (Exception ex) when (ex is ArgumentNullException or FormatException or OverflowException)
                            {
                                await existingInteraction.Reply("Well I wasn't expecting that");
                                await existingInteraction.End();
                                return;
                            }
                            
                        else
                            existingInteraction.ChooseMenuOption(data.Message!.Text);
                        return;
                    }

                    ((TelegramCommandInteraction)existingInteraction).ReadResponse(data.Message!);

                    if (((TelegramCommandInteraction)existingInteraction).IsReady)
                        FinalizeCommandReceived(new InteractionReceivedEventArgs(existingInteraction));

                    channel.Delete(data.Message!.MessageId);                    
                    return;
                }
                // otherwise end the previous incomplete interaction so the rest of this code can start a new one
                else 
                    removeExistingInteraction = true;
            }                

            InteractionSource source = new InteractionSource(new TelegramUser(this, data.Message!.From), data.Message);
            try
            {
                TelegramInteraction? newInteraction = (TelegramInteraction?)interactionFactory?.CreateInteraction(source);
                if (newInteraction is null) return;

                if (removeExistingInteraction) // this needs to wait until now to be sure the new interaction creates successfully
                    await existingInteraction!.End();

                activeInteractions.Add(newInteraction);
                //channelLibrary[data.Message.Chat.Id].DoTyping();
                if (newInteraction is TelegramCommandInteraction && ((TelegramCommandInteraction)newInteraction).IsReady)
                {
                    string username = ((TelegramUser)((TelegramCommandInteraction)newInteraction).Source.User).Account.Username ?? "";
                    if (((TelegramCommandInteraction)newInteraction).BotifexCommand.AdminOnly
                        && !adminNames.Contains(username))
                    {
                        await Reply(newInteraction, $"Sorry, only specified admins can use that command");
                        await newInteraction.End();
                        return;
                    }
                    FinalizeCommandReceived(new InteractionReceivedEventArgs(newInteraction));
                }

                else if (newInteraction is TelegramTextInteraction)
                    FinalizeMessageReceived(new InteractionReceivedEventArgs(newInteraction));
            }
            catch (ArgumentException)
            {
                // catch and ignore
            }
        }

        
        internal override async Task LoadCommands()
        {
            List<SlashCommand> botifexCommands = commandLibrary.Commands;
            List<BotCommand> userCommands = new();
            List<BotCommand> adminCommands = new();

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
            
            if(adminCommands.Count > 0)
            {                
                if (StatusChannel is not null) 
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(StatusChannel.Id));
                
                if (LogChannel is not null) 
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(LogChannel.Id));
            }
            
            await Bot.SetMyCommandsAsync(userCommands, BotCommandScope.AllPrivateChats());
        }

        internal override Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null || statusText == OngoingStatusMessage?.Text) return Task.CompletedTask;
                                                     // if the text is the same it'll throw an exception
            try
            {
                if (OngoingStatusMessage is null)
                    StatusChannel.Send(Truncate(statusText), callback: new Action<Message>((m) =>
                    {
                        OngoingStatusMessage = m;
                    }));
                else
                {
                    StatusChannel.Edit(OngoingStatusMessage.MessageId, Truncate(statusText));
                    OngoingStatusMessage.Text = Truncate(statusText);
                }      
            }
            catch(Exception) 
            {
                // ignore
            }
            return Task.CompletedTask;
        }

        internal override Task SendOneTimeStatus(string statusText, bool notification = false)
        {
            if (!IsReady || StatusChannel is null) return Task.CompletedTask;

            StatusChannel.Send(statusText, callback: notification == false ? null : new Action<Message>((message) =>
            {
                StatusChannel.Pin(message.MessageId, disableNotification: !notification);
                StatusChannel.Unpin(message.MessageId);
            }));
            return Task.CompletedTask;
        }
            
        internal override Task Reply(Interaction interaction, string text)
        {
            if (!IsReady || interaction.Source.Message is null) return Task.CompletedTask;

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

        internal override Task ReplyWithOptions(Interaction interaction, string? text)
        {
            text = text ?? string.Empty;
            if (!IsReady || interaction.Source.Message is null) return Task.CompletedTask;

            Message userMessage = (Message)interaction.Source.Message;
            Channel channel = channelLibrary[userMessage.Chat.Id];

            List<KeyboardButton> buttons = new List<KeyboardButton>();
            ReplyKeyboardMarkup? keyboard = null;
            if (interaction.Menu is not null && interaction.Menu.Options.Any())
            {
                for (int i = 0; i < interaction.Menu.Options.Count; i++)
                {
                    buttons.Add(new KeyboardButton($"{(interaction.Menu.NumberedChoices ? i + 1 : interaction.Menu.Options.Keys.ToArray()[i])}"));

                }
                keyboard = new ReplyKeyboardMarkup(buttons);
                keyboard.ResizeKeyboard = true;
                keyboard.Selective = (userMessage.Chat.Type == Telegram.Bot.Types.Enums.ChatType.Private
                                      || userMessage.Chat.Type == Telegram.Bot.Types.Enums.ChatType.Sender);
                keyboard.OneTimeKeyboard = true;

                text += "\n" + interaction.Menu.MenuText;
                text = text.Trim();
            }

            Message? botMessage = (Message?)interaction.BotMessage;
            int messageId = botMessage?.MessageId ?? 0;

            if (botMessage is not null && keyboard is null)
                channel.Edit(messageId, Truncate(text), callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            else
                channel.Send(text, replyToMessageId: userMessage.MessageId, markup: keyboard, callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));

            // if we sent a keyboard with this, we had to start fresh so can delete the original message.
            // (can't edit a keyboard into an existing message with this library)
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

        internal override Task RemoveInteraction(Interaction i)
        {
            if (i is not TelegramInteraction) throw new ArgumentException();

            TelegramInteraction interaction = (TelegramInteraction)i;
            activeInteractions.Remove(interaction);

            if (i.BotMessage is not null && i.Menu is not null)
            {
                Message message = (Message)interaction.BotMessage!;
                channelLibrary[message.Chat.Id].Delete(message.MessageId);
                //channelLibrary[message.Chat.Id].Send(((Message)i.BotMessage).Text!, replyToMessageId: ((Message)i.Source.Message!).MessageId, disableNotification: true);
            }
            return Task.CompletedTask;
        }

        internal override Task SendMessageToUser(IMessengerUser user, string message)
        {
            if(user is not TelegramUser) throw new ArgumentException();

            long userId = ((TelegramUser)user).Account.Id;
            // log the channel in the channel library if necessary (this shouldn't be necessary but just in case)
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
            if (OngoingStatusMessage is null || StatusChannel is null || OngoingStatusMessage.Text is null) return Task.CompletedTask;
            int oldStatusId = OngoingStatusMessage.MessageId;

            // make a copy of the old status in the same channel
            StatusChannel.Send(OngoingStatusMessage.Text, disableNotification: true, callback: new Action<Message>((m) =>
            {
                OngoingStatusMessage = m;
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
