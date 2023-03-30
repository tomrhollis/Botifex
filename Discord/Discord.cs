using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex
{
    public class Discord : Messenger, IDiscord
    {
        public DiscordSocketClient DiscordClient { get; set; }
        public ITextChannel LogChannel { get; set; }
        internal override int MAX_TEXT_LENGTH { get => 2000; }

        public override bool IsReady
        {
            get=>DiscordClient is not null && DiscordClient.ConnectionState == ConnectionState.Connected;
        }        

        private ILogger<Discord> log;
        
        public Discord(ILogger<Discord> log, IConfiguration cfg, IHost host, IHostApplicationLifetime lifetime) 
            : base(host, lifetime, cfg.GetSection("Discord"))
        {
            this.log = log;

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            DiscordClient = new DiscordSocketClient();
            DiscordClient.Log += DiscordLog;
            DiscordClient.Ready += OnConnect;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            log.LogDebug("StartAsync has been called.");
            await DiscordClient.LoginAsync(TokenType.Bot, config.GetValue<string>("DiscordBotToken"));
            await DiscordClient.StartAsync();
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await Log("Awoooooo......", LogLevel.Information);
            await DiscordClient.StopAsync();
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (!String.IsNullOrEmpty(config.GetValue<string>("DiscordBotToken"))) await StartAsync(CancellationToken.None);
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
            await StopAsync(CancellationToken.None);
        }

        internal override void OnStopped()
        {
            log.LogDebug("OnStopped has been called.");
        }

        public async Task OnConnect()
        {
            if (!String.IsNullOrEmpty(config?.GetValue<string>("DiscordLogChannel")))
            {
                LogChannel = (ITextChannel)await DiscordClient.GetChannelAsync(ulong.Parse(config.GetValue<string>("DiscordLogChannel")));                
            }
            await Log("Yip Yip", LogLevel.Information);

            await LoadCommands();

            /* add handlers
            DiscordClient.SlashCommandExecuted += SlashCommandHandler;
            DiscordClient.ButtonExecuted += ButtonHandler;
            DiscordClient.SelectMenuExecuted += SelectMenuHandler;
            */
        }

        private async Task DiscordLog(LogMessage msg)
        {
            await Log(msg.ToString(), LogLevel.Debug);
        }

        internal override async Task Log(string m, LogLevel i = LogLevel.Information)
        {
            log.Log(i, m);
#if !DEBUG
            if(i == LogLevel.Debug) return;
#endif
            if (IsReady && LogChannel is not null)
            {
                await LogChannel.SendMessageAsync($"[{i}] {(m.Length > (MAX_TEXT_LENGTH - 100) ? m.Substring(0,MAX_TEXT_LENGTH - 100) + "..." : m)}");                
            }
        }

        internal override async Task LoadCommands()
        {
            DiscordClient.GetGlobalApplicationCommandsAsync().Result.ToList().ForEach(c => { c.DeleteAsync(); });
            botifex.Commands.ForEach(async command => { await LoadCommand(command); });
        }

        internal async Task LoadCommand(SlashCommand command)
        {
            var newCommand = new SlashCommandBuilder().WithName(command.Name).WithDescription(command.Description).Build();
            try
            {
                await DiscordClient.CreateGlobalApplicationCommandAsync(newCommand);
            }
            catch (HttpException ex)
            {
                await Log(ex.GetType() + ": " + ex.Message, LogLevel.Warning);
            }
        }



        /*
        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            Log(LogLevel.Debug, "Received request for /" + command.Data.Name);
        }

        private async Task ButtonHandler(SocketMessageComponent component)
        {
            // 
        }

        private async Task SelectMenuHandler(SocketMessageComponent component)
        {
            try
            {
                //
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.GetType() + ": " + ex.Message + "\n" + ex.StackTrace);
            }            
        }
        */
    }
}
