using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.ReplyMarkups;
using Telegram.Bot.Exceptions;

namespace Botifex.Services
{
    internal class TelegramService : Messenger, ITelegram
    {
        private bool isReady = false;
        public override bool IsReady { get => isReady && Bot is not null; protected private set=>isReady=value; }
        public TelegramBotClient Bot { get; private set; }
        public ChatId? LogChannel { get; private set; }
        private Message? LogMessage { get; set; }
        public ChatId? StatusChannel { get; private set; }
        private int OngoingStatusMessageId { get; set; } = 0;

        private List<TelegramInteraction> activeInteractions = new List<TelegramInteraction>();

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
            
            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            if (String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) return;
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken")!);

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0) LogChannel = new ChatId(logChannelId);

            long statusChannelId = config.GetValue<long>("TelegramStatusChannel");
            if (statusChannelId != 0) StatusChannel = new ChatId(statusChannelId);

            adminNames = config.GetSection("TelegramAdminAllowlist").Get<string[]>()?.ToList() ?? new List<string>();
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            try
            {
                // start listening
                Bot.StartReceiving(updateHandler: OnUpdateReceived,
                                   pollingErrorHandler: OnErrorReceived);

                BotUsername = (await Bot.GetMeAsync()).Username ?? "";

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

        internal override async Task Log(string m, LogLevel i = LogLevel.Information)
        {
            // log to console
            log.Log(i, m);

            // if we can log to the log channel
            if (IsReady && LogChannel is not null)
                // append to an existing message to save new message limit, make new one if there isn't one yet
                LogMessage = (LogMessage is not null) ? await AppendText(LogMessage, m) : await SendNewMessage(LogChannel, m);
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

            TelegramUser user = new TelegramUser(this, data.Message.From);
            Chat chat = data.Message.Chat;

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

                    await Bot.DeleteMessageAsync(new ChatId(chat.Id), data.Message!.MessageId);
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
                await Bot.SendChatActionAsync(data.Message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);
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
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(StatusChannel));
                
                if (LogChannel is not null) 
                    await Bot.SetMyCommandsAsync(adminCommands, BotCommandScope.ChatAdministrators(LogChannel));
            }
            
            await Bot.SetMyCommandsAsync(userCommands, BotCommandScope.AllPrivateChats());
        }

        internal override async Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null) return;

            try
            {
                if (OngoingStatusMessageId == 0)
                    OngoingStatusMessageId = (await SendNewMessage(StatusChannel, statusText)).MessageId;

                else if (StatusChannel.Identifier is not null)
                    await Bot.EditMessageTextAsync(StatusChannel, OngoingStatusMessageId, Truncate(statusText));
            }
            catch(Exception) 
            {
                // ignore
            }


        }

        internal override async Task SendOneTimeStatus(string statusText, bool notification = false)
        {
            if (!IsReady || StatusChannel is null) return;

            int messageId = (await SendNewMessage(StatusChannel, statusText)).MessageId;
#if DEBUG
            notification = false; // to stop spamming while testing other things
#endif
            if (notification)
            {
                await Bot.PinChatMessageAsync(StatusChannel, messageId, disableNotification: false);
                Thread.Sleep(3000);
                await Bot.UnpinChatMessageAsync(StatusChannel, messageId);
            }
        }


        internal override async Task Reply(Interaction interaction, string text)
        {
            if (!IsReady || interaction.Source.Message is null) return;

            Message userMessage = (Message)interaction.Source.Message;
            Message? botMessage = (Message?)interaction.BotMessage;

            // if this is a result of a menu selection, clear out the response message so the menu can go away. 
            // It will fall through to sending a new message in the next if/else
            if (interaction.Menu is not null)
            {
                interaction.Menu = null;
                await Bot.DeleteMessageAsync(new ChatId(botMessage!.Chat.Id), botMessage.MessageId);
                interaction.BotMessage = null;                
            }

            if (interaction.BotMessage is not null)
            {
                try
                {
                    interaction.BotMessage = await Bot.EditMessageTextAsync(new ChatId(botMessage!.Chat.Id), botMessage.MessageId, Truncate(text));
                }
                catch (ApiRequestException) { } // this occurs when they're typing too fast and get ahead of responses. Ignores the impatient texts
            }
            else
                interaction.BotMessage = await SendNewMessage(userMessage.Chat.Id, text, replyToMessageId: userMessage.MessageId);
        }

        internal override async Task ReplyWithOptions(Interaction interaction, string? text)
        {
            text = text ?? string.Empty;
            if (!IsReady || interaction.Source.Message is null) return;

            Message userMessage = (Message)interaction.Source.Message;

            List<KeyboardButton> buttons = new List<KeyboardButton>();
            ReplyKeyboardMarkup? keyboard = null;
            if (interaction.Menu is not null && interaction.Menu.Options.Any())
            {
                for(int i=0; i<interaction.Menu.Options.Count; i++)
                {
                    buttons.Add(new KeyboardButton($"{(interaction.Menu.NumberedChoices ? i+1 : interaction.Menu.Options.Keys.ToArray()[i])}"));
                    
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
                await Bot.DeleteMessageAsync(new ChatId(botMessage.Chat.Id), botMessage.MessageId);
                interaction.BotMessage = null;
            }

            if (interaction.BotMessage is not null)
            {
                try
                {
                    interaction.BotMessage = await Bot.EditMessageTextAsync(new ChatId(botMessage!.Chat.Id), botMessage.MessageId, Truncate(text));
                }
                catch (ApiRequestException) { } // this occurs when they're typing too fast and get ahead of responses. Ignores the impatient texts
            }                
            else
                interaction.BotMessage = await SendNewMessage(userMessage.Chat.Id, text, replyToMessageId: userMessage.MessageId, markup: keyboard);
        }

        // separating this out now because it'll all have to go through an API queue eventually
        private async Task<Message> SendNewMessage(ChatId chatId, string text, int? replyToMessageId = null, ReplyKeyboardMarkup? markup = null) 
        {
            Message newMessage = new Message();
            try
            {
                newMessage = await Bot.SendTextMessageAsync(chatId, Truncate(text), replyToMessageId: replyToMessageId, replyMarkup: markup);
            }
            catch(ApiRequestException) // this can occur on restart sometimes or if the user deletes a message before a response comes back
            {
                newMessage = await Bot.SendTextMessageAsync(chatId, Truncate(text), replyMarkup: markup);
            }
            return newMessage;
        }

        private async Task<Message> AppendText(Message existingMessage, string text)
        {
            if ((existingMessage.Text?.Length + text.Length) < (MAX_TEXT_LENGTH - 2))
                return await Bot.EditMessageTextAsync(new ChatId(existingMessage.Chat.Id), existingMessage.MessageId, existingMessage.Text + "\n" + text);

            else
                return await SendNewMessage(new ChatId(existingMessage.Chat.Id), text);
        }

        internal override async Task RemoveInteraction(Interaction i)
        {
            if (i is not TelegramInteraction) throw new ArgumentException();

            TelegramInteraction interaction = (TelegramInteraction)i;
            activeInteractions.Remove(interaction);

            if (i.BotMessage is not null && i.Menu is not null)
            {
                Message message = (Message)interaction.BotMessage!;
                await Bot.DeleteMessageAsync(new ChatId(message.Chat.Id), message.MessageId);
                await Bot.SendTextMessageAsync(new ChatId(message.Chat.Id), ((Message)i.BotMessage).Text!, replyToMessageId: ((Message)i.Source.Message!).MessageId, disableNotification: true);
            }
        }

        internal override async Task SendMessageToUser(IMessengerUser user, string message)
        {
            if(user is not TelegramUser) throw new ArgumentException();

            await SendNewMessage(new ChatId(((TelegramUser)user).Account.Id), message);
        }
    }
}
