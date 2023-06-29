using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Exceptions;

namespace Botifex.Services.TelegramBot
{
    internal class TelegramService : Messenger, ITelegram
    {
        private bool isReady = false;
        public override bool IsReady { get => isReady && Bot is not null; protected private set=>isReady=value; }
        public TelegramBotClient Bot { get; private set; }
        public Channel? LogChannel { get; private set; }
        private Message? LogMessage { get; set; }
        public Channel? StatusChannel { get; private set; }
        private int OngoingStatusMessageId { get; set; } = 0;

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
            if (logChannelId != 0) LogChannel = new Channel(Bot, logChannelId);

            long statusChannelId = config.GetValue<long>("TelegramStatusChannel");
            if (statusChannelId != 0) StatusChannel = new Channel(Bot, statusChannelId);

            adminNames = config.GetSection("TelegramAdminAllowlist").Get<string[]>()?.ToList() ?? new List<string>();

            try
            {
                // start listening
                Bot.StartReceiving(updateHandler: OnUpdateReceived,
                                   pollingErrorHandler: OnErrorReceived);

                BotUsername = (Bot.GetMeAsync().Result).Username ?? "";

                IsReady = true;
                FinalizeFirstReady(EventArgs.Empty);
                log.LogInformation("Yip Yip");
                //await Log($"Yip Yip", LogLevel.Information);
            }
            catch (Exception ex)
            {
                log.LogError($"[{DateTime.Now}] {ex.GetType()} - {ex.Message}");
            }
        }

        internal override /*async*/ void OnStopping()
        {
            log.LogInformation("Awoooooo.....");
            //await Log("Awoooooo......", LogLevel.Information);
            log.LogDebug("OnStopping has been called.");
                        
            IsReady = false;
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
                // append to an existing message or make new one if there isn't one yet
                if (LogMessage is null)
                    LogChannel.Send(m, callback: (message) =>
                    {
                        LogMessage = message;
                    });
                else
                    AppendText(LogMessage, m, callback: new Action<Message>((m) =>
                    {
                        LogMessage = m;
                    }));
            
            return Task.CompletedTask;
        }

        private Task OnErrorReceived(ITelegramBotClient bot, Exception ex, CancellationToken cToken)
        {
            log.LogError(ex, String.Empty);
            return Task.CompletedTask;
        }

        private async Task OnUpdateReceived(ITelegramBotClient bot, Update data, CancellationToken cToken)
        {
            // ignore if no identifiable user
            if (data.Message?.From is null || String.IsNullOrEmpty(data.Message?.Text)) return;

            // ignore if in a group chat and not targeted
            if (data.Message.Chat.Type != Telegram.Bot.Types.Enums.ChatType.Private 
                && data.Message.Chat.Type != Telegram.Bot.Types.Enums.ChatType.Sender
                && !Regex.Match(data.Message.Text.ToLower(), $"@{BotUsername.ToLower()}").Success)
                return;

            // log the channel in the channel library if necessary
            if (!channelLibrary.Keys.Contains(data.Message.Chat.Id))
                channelLibrary[data.Message.Chat.Id] = new Channel(Bot, data.Message.Chat.Id);

            TelegramUser user = new TelegramUser(this, data.Message.From);
            Chat chat = data.Message.Chat;
            Channel channel = channelLibrary[chat.Id];

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
            if (!IsReady || StatusChannel is null) return Task.CompletedTask;

            try
            {
                if (OngoingStatusMessageId == 0)
                    StatusChannel.Send(statusText, callback: new Action<Message>((m) =>
                    {
                        OngoingStatusMessageId = m.MessageId;
                    }));

                else if (StatusChannel is not null)
                    StatusChannel.Edit(OngoingStatusMessageId, Truncate(statusText));
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

            StatusChannel.Send(statusText, callback: new Action<Message>(async (message) =>
            {
                StatusChannel.Pin(message.MessageId, disableNotification: false);
                StatusChannel.Unpin(message.MessageId);
            }));
            return Task.CompletedTask;
        }


        internal override Task Reply(Interaction interaction, string text)
        {
            if (!IsReady || interaction.Source.Message is null) return Task.CompletedTask;

            Message userMessage = (Message)interaction.Source.Message;
            Message? botMessage = (Message?)interaction.BotMessage;

            // if this is a result of a menu selection, clear out the response message so the menu can go away. 
            // It will fall through to sending a new message in the next if/else
            if (interaction.Menu is not null)
            {
                interaction.Menu = null;

                channelLibrary[botMessage!.Chat.Id].Delete(botMessage.MessageId);
                interaction.BotMessage = null;                
            }

            if (interaction.BotMessage is not null)
                channelLibrary[botMessage!.Chat.Id].Edit(botMessage.MessageId, Truncate(text), callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            else
                channelLibrary[userMessage.Chat.Id].Send(text, replyToMessageId: userMessage.MessageId, callback: new Action<Message>((m) =>
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

            // if we're sending a keyboard with this, we have to start fresh. can't edit a keyboard into an existing message with this library
            if (keyboard is not null && botMessage is not null)
            {
                channelLibrary[botMessage.Chat.Id].Delete(botMessage.MessageId);
                interaction.BotMessage = null;
            }

            if (interaction.BotMessage is not null)
                channelLibrary[botMessage!.Chat.Id].Edit(botMessage.MessageId, Truncate(text), callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            else
                channelLibrary[userMessage.Chat.Id].Send(text, replyToMessageId: userMessage.MessageId, markup: keyboard, callback: new Action<Message>((m) =>
                {
                    interaction.BotMessage = m;
                }));
            return Task.CompletedTask;
        }

        private Task AppendText(Message existingMessage, string text, Action<Message>? callback = null)
        {
            Channel channel = channelLibrary[existingMessage.Chat.Id];

            if ((existingMessage.Text?.Length + text.Length) < (MAX_TEXT_LENGTH - 2))
                channel.Edit(existingMessage.MessageId, existingMessage.Text + "\n" + text, callback: callback);

            else
                channel.Send(text, callback: callback);

            return Task.CompletedTask;
        }

        internal override Task RemoveInteraction(Interaction i)
        {
            if (i is not TelegramInteraction) throw new ArgumentException();

            TelegramInteraction interaction = (TelegramInteraction)i;
            activeInteractions.Remove(interaction);

            if (i.BotMessage is not null && i.Menu is not null)
            {
                Message message = (Message)interaction.BotMessage!;
                channelLibrary[message.Chat.Id].Delete(message.MessageId);
                channelLibrary[message.Chat.Id].Send(((Message)i.BotMessage).Text!, replyToMessageId: ((Message)i.Source.Message!).MessageId, disableNotification: true);
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
            if (OngoingStatusMessageId == 0 || StatusChannel is null) return Task.CompletedTask;
            int oldStatusId = OngoingStatusMessageId;

            // make a copy of the old status in the same channel
            StatusChannel.Copy(OngoingStatusMessageId, disableNotification: true, callback: new Action<MessageId>((id) =>
            {
                OngoingStatusMessageId = id.Id;
            }));

            // replace the text of the old status message
            StatusChannel.Edit(oldStatusId, text);
            return Task.CompletedTask;
        }
    }
}
