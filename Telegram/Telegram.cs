using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace Botifex
{
    internal class Telegram : Messenger, ITelegram
    {
        public override bool IsReady => Bot is not null;
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

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            log.LogDebug("StartAsync has been called.");
            Bot = new TelegramBotClient(config.GetValue<string>("TelegramBotToken"));

            await LoadCommands();

            // start listening
            Bot.StartReceiving(updateHandler: OnUpdateReceived,
                               pollingErrorHandler: OnErrorReceived);

            long logChannelId = config.GetValue<long>("TelegramLogChannel");
            if (logChannelId != 0) LogChannel = new ChatId(logChannelId);
            await Log("Yip Yip", LogLevel.Information);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await Log("Awoooooo......", LogLevel.Information);
            log.LogDebug("StopAsync has been called.");
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (!String.IsNullOrEmpty(config.GetValue<string>("TelegramBotToken"))) await StartAsync(CancellationToken.None);
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
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
            log.LogDebug($"[{data.Message.Chat.Id} - {data.Message.Chat.FirstName} {data.Message.Chat.LastName}] {data.Message.Text}");
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
            }
            
            await Bot.SetMyCommandsAsync(telegramCommands);
        }
    }
}
