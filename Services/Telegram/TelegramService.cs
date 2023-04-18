using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal class TelegramService : Messenger, ITelegram
    {
        private bool isReady = false;
        public override bool IsReady { get => isReady && Bot is not null; protected private set=>isReady=value; }
        public TelegramBotClient Bot { get; private set; }
        public ChatId LogChannel { get; private set; }
        public ChatId StatusChannel { get; private set; }
        private int StatusMessageId { get; set; } = 0;
        //private List<TelegramInteraction> activeInteractions = new List<TelegramInteraction>();
        
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
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) return;
            
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken")!);

            // start listening
            Bot.StartReceiving(updateHandler: OnUpdateReceived,
                               pollingErrorHandler: OnErrorReceived);

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0) LogChannel = new ChatId(logChannelId);

            long statusChannelId = config.GetValue<long>("TelegramStatusChannel");
            if (statusChannelId != 0) StatusChannel = new ChatId(statusChannelId);

            BotUsername = (await Bot.GetMeAsync()).Username ?? "";
            
            IsReady = true;
            FinalizeFirstReady(EventArgs.Empty);
            await Log($"Yip Yip I am {BotUsername}", LogLevel.Information);
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
            log.Log(i, m);
            if (IsReady && LogChannel is not null) await Bot.SendTextMessageAsync(LogChannel, m);
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

            // if this isn't a command and there's an existing interaction already, pass it over to that
            /*
            TelegramInteraction? existingInteraction = activeInteractions.Find(i => i.Source.User == user && ((Message?)i.Source.Message)?.Chat == chat);
            if (existingInteraction is not null && !String.IsNullOrEmpty((data.Message?.Text ?? "").Trim()) && data.Message?.Text.Trim()[0] != '/')
            {
                existingInteraction.FollowUp(data);
                return Task.CompletedTask;
            }
            else existingInteraction?.End();*/

            InteractionSource source = new InteractionSource(new TelegramUser(data.Message!.From), this, data.Message);
            try
            {
                TelegramInteraction? newInteraction = (TelegramInteraction?)interactionFactory.CreateInteraction(source);
                if (newInteraction is null) return;

                //activeInteractions.Add(newInteraction);

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
                StatusMessageId = (await Bot.SendTextMessageAsync(StatusChannel, statusText)).MessageId;

            else if (StatusChannel.Identifier is not null) 
                await Bot.EditMessageTextAsync(new ChatId((long)StatusChannel.Identifier), StatusMessageId, statusText);
        }

        internal override async Task Reply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return;

            Message message = (Message)interaction.Source.Message;
            await Bot.SendTextMessageAsync(message.Chat.Id, text, replyToMessageId: message.MessageId);
        }
    }
}
