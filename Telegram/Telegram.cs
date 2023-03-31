using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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

        public Telegram(ILogger<Telegram> log, IConfiguration cfg, IHost host, IHostApplicationLifetime lifetime) 
                : base(host, lifetime, cfg.GetSection("Telegram"))
        {
            this.log = log;
            
            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);            
        }

        public override async Task StartAsync()
        {
            log.LogDebug("StartAsync has been called.");
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken"));

            await LoadCommands();

            // start listening
            Bot.StartReceiving(updateHandler: OnUpdateReceived,
                               pollingErrorHandler: OnErrorReceived);

            IsReady=true;

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0) LogChannel = new ChatId(logChannelId);
            await Log("Yip Yip", LogLevel.Information);
        }

        public override async Task StopAsync()
        {
            await Log("Awoooooo......", LogLevel.Information);
            IsReady = false;            
            log.LogDebug("StopAsync has been called.");
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (!String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) await StartAsync();
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
            await StopAsync();
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
            await Log("Received message: " + data.Message?.Text, LogLevel.Debug);
            FinalizeMessageReceived(new MessageReceivedEventArgs($"{data.Message?.Text} - Type: {data.Type} / {data.Message?.Type}"));
        }

        private async Task SlashCommandHandler(Update data)
        {
            await Log("Received request for /" + data.Message?.Text, LogLevel.Debug);
            FinalizeCommandReceived(new CommandReceivedEventArgs($"{data.Type} / {data.Message?.Type}", $"{data.Message?.Text}"));
        }

    }
}
