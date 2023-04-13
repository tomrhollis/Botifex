using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Botifex
{
    internal class Telegram : Messenger, ITelegram
    {
        private bool isReady = false;
        public override bool IsReady { get => isReady && Bot is not null; protected private set=>isReady=value; }
        public TelegramBotClient Bot { get; private set; }
        public ChatId LogChannel { get; private set; }

        internal override int MAX_TEXT_LENGTH { get => 4096; }
        private ILogger<Telegram> log;
        private string BotUsername = "";

        public Telegram(ILogger<Telegram> log, IConfiguration cfg, IHost host, IHostApplicationLifetime lifetime) 
                : base(host, lifetime, cfg.GetSection("Telegram"))
        {
            this.log = log;
            
            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);            
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) return;
            
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken"));

            await LoadCommands();

            // start listening
            Bot.StartReceiving(updateHandler: OnUpdateReceived,
                               pollingErrorHandler: OnErrorReceived);

            IsReady = true;

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0) LogChannel = new ChatId(logChannelId);
            BotUsername = (await Bot.GetMeAsync()).Username;
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

        private async Task OnErrorReceived(ITelegramBotClient bot, Exception ex, CancellationToken cToken)
        {
            throw new NotImplementedException();
        }

        private async Task OnUpdateReceived(ITelegramBotClient bot, Update data, CancellationToken cToken)
        {
            log.LogDebug($"[{data.Message?.Chat.Id} - {data.Message?.Chat.FirstName} {data.Message?.Chat.LastName}] {data.Message?.Text}");
            await MessageHandler(data);           
        }

        internal override async Task LoadCommands()
        {
            List<SlashCommand> botifexCommands = botifex.Commands;
            List<BotCommand> telegramCommands = new();
            foreach( SlashCommand c in botifexCommands )
            {
                telegramCommands.Add(new BotCommand()
                {
                    Command = c.Name,
                    Description = c.Description
                });
                await Log($"Creating Telegram command {c.Name}");
            }
            
            await Bot.SetMyCommandsAsync(telegramCommands);
        }

        private async Task MessageHandler(Update data)
        {
            string text = data.Message?.Text;
            if (String.IsNullOrEmpty(text)) return;

            // see if it's a command
            string commandName = Regex.Match(text, "^\\/([^@\\s]*)").Groups[1].Value;           

            // see if it's a command with a bot name that isn't this one
            if (!String.IsNullOrEmpty(commandName))
            {
                string botName = Regex.Match(text, $"\\/{commandName}@([\\S]*)").Groups[1].Value;
                if (!String.IsNullOrEmpty(botName))
                {
                    await Log($"Bot name {botName} parsed");
                    if (botName.ToLower() != BotUsername.ToLower()) return;
                }

                if (botifex.HasCommand(commandName))
                {
                    await Log($"Command {commandName} received");
                    SlashCommand botCommand = botifex.GetCommand(commandName);
                    FinalizeCommandReceived(new CommandReceivedEventArgs(botCommand, $"{text}"));
                    return;
                }                
            }
            // if none of that matched, it's just a text message
            FinalizeMessageReceived(new MessageReceivedEventArgs($"{text} - Type: {data.Type} / {data.Message?.Type}"));
        }


    }
}
