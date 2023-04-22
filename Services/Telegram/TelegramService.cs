using Discord;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;

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
        private int StatusMessageId { get; set; } = 0;
        private List<TelegramInteraction> activeInteractions = new List<TelegramInteraction>();
        
        internal override int MAX_TEXT_LENGTH { get => 4096; }
        private ILogger<TelegramService> log;
        internal string BotUsername { get; private set; } = "";

        public TelegramService(ILogger<TelegramService> log, IConfiguration cfg, IHostApplicationLifetime lifetime, ICommandLibrary lib) 
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
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");

            // start listening
            Bot.StartReceiving(updateHandler: OnUpdateReceived,
                               pollingErrorHandler: OnErrorReceived);

            BotUsername = (await Bot.GetMeAsync()).Username ?? "";
            
            IsReady = true;
            FinalizeFirstReady(EventArgs.Empty);
            //await Log($"Yip Yip I am {BotUsername}", LogLevel.Information);
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
            await Log("Awoooooo......", LogLevel.Information);
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

            TelegramUser user = new TelegramUser(data.Message.From);
            Chat chat = data.Message.Chat;

            // if there's an existing command waiting for response already, see if this is related to that       
            TelegramInteraction? existingInteraction = activeInteractions.Find(i => ((TelegramUser)i.Source.User).Account.Id == user.Account.Id && ((Message?)i.Source.Message)?.Chat.Id == chat.Id);
            if (existingInteraction is TelegramCommandInteraction && !String.IsNullOrEmpty((data.Message?.Text ?? "").Trim()))
            {
                // if this is not a command, give it to the command interaction as a response
                if (data.Message?.Text.Trim()[0] != '/')
                {
                    ((TelegramCommandInteraction)existingInteraction).ReadResponse(data.Message!);

                    if (((TelegramCommandInteraction)existingInteraction).IsReady)
                        FinalizeCommandReceived(new InteractionReceivedEventArgs(existingInteraction));

                    await Bot.DeleteMessageAsync(new ChatId(chat.Id), data.Message!.MessageId);
                    return;
                }
                // otherwise end the previous incomplete interaction so the rest of this code can start a new one
                else if(!((TelegramCommandInteraction)existingInteraction).IsReady)
                {
                    activeInteractions.Remove(existingInteraction);
                    existingInteraction.End();
                }
            }                

            InteractionSource source = new InteractionSource(new TelegramUser(data.Message!.From), this, data.Message);
            try
            {
                TelegramInteraction? newInteraction = (TelegramInteraction?)interactionFactory?.CreateInteraction(source);
                if (newInteraction is null) return;

                activeInteractions.Add(newInteraction);

                if (newInteraction is TelegramCommandInteraction && ((TelegramCommandInteraction)newInteraction).IsReady)
                    FinalizeCommandReceived(new InteractionReceivedEventArgs(newInteraction));

                else if (newInteraction is TelegramTextInteraction)
                {
                    await Bot.SendChatActionAsync(data.Message.Chat.Id, Telegram.Bot.Types.Enums.ChatAction.Typing);
                    FinalizeMessageReceived(new InteractionReceivedEventArgs(newInteraction));
                }                    
            }
            catch (ArgumentException ex)
            {
                // catch and ignore
            }
        }

        internal override async Task LoadCommands()
        {
            List<SlashCommand> botifexCommands = commandLibrary.Commands;
            List<BotCommand> telegramCommands = new();
            foreach( SlashCommand c in botifexCommands )
            {
                telegramCommands.Add(new BotCommand()
                {
                    Command = c.Name,
                    Description = c.Description
                });
                log.LogDebug($"Creating Telegram command {c.Name}");
            }
            
            await Bot.SetMyCommandsAsync(telegramCommands);
        }

        internal override async Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null) return;

            if (StatusMessageId == 0) 
                StatusMessageId = (await SendNewMessage(StatusChannel, statusText)).MessageId;

            else if (StatusChannel.Identifier is not null) 
                await Bot.EditMessageTextAsync(StatusChannel, StatusMessageId, Truncate(statusText));
        }

        internal override async Task Reply(Interaction interaction, string text, Dictionary<string, string>? options = null)
        {
            if (interaction.Source.Message is null) return;

            Message userMessage = (Message)interaction.Source.Message;

            List<KeyboardButton> buttons = new List<KeyboardButton>();
            ReplyKeyboardMarkup? keyboard = null;
            if (options is not null && options.Any())
            {
                for(int i=0; i<options.Count; i++)
                {
                    buttons.Add(new KeyboardButton($"{i+1}"));
                    text += $"\n{i+1}: {options[options.Keys.ToArray()[i]]}";
                }
                keyboard = new ReplyKeyboardMarkup(buttons);
                keyboard.ResizeKeyboard = true;
                keyboard.Selective = (userMessage.Chat.Type == Telegram.Bot.Types.Enums.ChatType.Private
                                      || userMessage.Chat.Type == Telegram.Bot.Types.Enums.ChatType.Sender);
                keyboard.OneTimeKeyboard = true;
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
                interaction.BotMessage = await Bot.EditMessageTextAsync(new ChatId(botMessage!.Chat.Id), botMessage.MessageId, Truncate(text));
            }                
            else
                interaction.BotMessage = await SendNewMessage(userMessage.Chat.Id, text, replyToMessageId: userMessage.MessageId, markup: keyboard);
        }

        // separating this out now because it'll all have to go through an API queue eventually
        private async Task<Message> SendNewMessage(ChatId chatId, string text, int? replyToMessageId = null, ReplyKeyboardMarkup? markup = null) 
        {
            return await Bot.SendTextMessageAsync(chatId, Truncate(text), replyToMessageId: replyToMessageId, replyMarkup: markup);
        }

        private async Task<Message> AppendText(Message existingMessage, string text)
        {
            if ((existingMessage.Text?.Length + text.Length) < (MAX_TEXT_LENGTH - 2))
                return await Bot.EditMessageTextAsync(new ChatId(existingMessage.Chat.Id), existingMessage.MessageId, existingMessage.Text + "\n" + text);

            else
                return await SendNewMessage(new ChatId(existingMessage.Chat.Id), text);
        }

    }
}
