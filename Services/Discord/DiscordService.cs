using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex.Services
{
    public class DiscordService : Messenger, IDiscord
    {
        public DiscordSocketClient DiscordClient { get; set; }
        public ITextChannel? LogChannel { get; set; }
        public ITextChannel? StatusChannel { get; set; }
        internal override int MAX_TEXT_LENGTH { get => 2000; }
        private ulong StatusMessageId { get; set; } = 0;
        private List<DiscordInteraction> activeInteractions = new List<DiscordInteraction>();
        

        public override bool IsReady
        {
            get=>DiscordClient is not null && DiscordClient.ConnectionState == ConnectionState.Connected;
        }        

        private ILogger<DiscordService> log;
        
        public DiscordService(ILogger<DiscordService> log, IConfiguration cfg, IHostApplicationLifetime lifetime, ICommandLibrary lib) 
            : base(lifetime, cfg.GetSection("Discord"), lib)
        {
            this.log = log;
            this.interactionFactory = new DiscordInteractionFactory(lib);

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            DiscordClient = new DiscordSocketClient();
            DiscordClient.Log += DiscordLog;
            DiscordClient.Ready += OnConnect;
        }

        internal override async void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            if (String.IsNullOrEmpty(config.GetValue<string>("DiscordBotToken"))) return;
            await DiscordClient.LoginAsync(TokenType.Bot, config.GetValue<string>("DiscordBotToken"));
            await DiscordClient.StartAsync();
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
            await Log("Awoooooo......", LogLevel.Information);
            await DiscordClient.StopAsync();
        }

        internal override void OnStopped()
        {
            log.LogDebug("OnStopped has been called.");
        }

        public async Task OnConnect()
        {
            if (!String.IsNullOrEmpty(config?.GetValue<string>("DiscordLogChannel")))
            {
                LogChannel = (ITextChannel)await DiscordClient.GetChannelAsync(ulong.Parse(config.GetValue<string>("DiscordLogChannel")!));                
            }
            if (!String.IsNullOrEmpty(config?.GetValue<string>("DiscordStatusChannel")))
            {
                StatusChannel = (ITextChannel)await DiscordClient.GetChannelAsync(ulong.Parse(config.GetValue<string>("DiscordStatusChannel")!));
            }
            await Log("Yip Yip", LogLevel.Information);
            FinalizeFirstReady(EventArgs.Empty);

            DiscordClient.MessageReceived += MessageHandler;
            DiscordClient.SlashCommandExecuted += SlashCommandHandler;
            
            /* add handlers
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
                await LogChannel.SendMessageAsync($"[{i}] {Truncate(m)}");                
            }
        }

        internal override async Task LoadCommands()
        {
            //DiscordClient.GetGlobalApplicationCommandsAsync().Result.ToList().ForEach(c => { c.DeleteAsync(); }); // only uncomment to fix problems
            List<SlashCommandProperties> discordSlashCommands = new List<SlashCommandProperties>();
            
            foreach(var command in  commandLibrary.Commands)
                discordSlashCommands.Add(BuildCommand(command));

            try
            {
                await DiscordClient.BulkOverwriteGlobalApplicationCommandsAsync(discordSlashCommands.ToArray());
            }
            catch (HttpException ex)
            {
                await Log(ex.GetType() + ": " + ex.Message, LogLevel.Warning);
            }
        }

        private SlashCommandProperties BuildCommand(SlashCommand botifexCommand)
        {
            var newCommand = new SlashCommandBuilder().WithName(botifexCommand.Name).WithDescription(botifexCommand.Description);

            if (botifexCommand.Options is not null)
            {
                foreach (var option in botifexCommand.Options)
                {
                    newCommand.AddOption(option.Name, ApplicationCommandOptionType.String, option.Description, option.Required);
                }
            }
            return newCommand.Build();
        }


        internal override async Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null) return;

            if (StatusMessageId == 0)
                StatusMessageId = (await StatusChannel.SendMessageAsync(Truncate(statusText))).Id;

            else
                await StatusChannel.ModifyMessageAsync(StatusMessageId, (m) =>
                {
                    m.Content = statusText;
                });
        }

        private Task MessageHandler(SocketMessage message)
        {
            // ignore if text message is from bot, is empty
            if (message.Author.IsBot || String.IsNullOrEmpty(message.Content)) return Task.CompletedTask;

            // if it's not from a DM make sure it has a mention of the bot otherwise abort (requires the intent for bots to see contents of messages in group chats)
            if(message.Channel.GetChannelType() != ChannelType.DM && !message.MentionedUsers.Contains<SocketUser>(DiscordClient.CurrentUser)) return Task.CompletedTask;

            DiscordInteraction? interaction = (DiscordInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(message.Author), this, message));
            if (interaction is null) return Task.CompletedTask;

            FinalizeMessageReceived(new InteractionReceivedEventArgs(interaction));
            return Task.CompletedTask;
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            DiscordInteraction? interaction = 
                (DiscordInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(command.User), this, command));

            if (interaction is null)
            {
                await command.DeleteOriginalResponseAsync();
                return;
            }

            await command.DeferAsync(ephemeral: command.Channel.GetChannelType().GetValueOrDefault() != ChannelType.DM);
            FinalizeCommandReceived(new InteractionReceivedEventArgs(interaction));
        }

        internal override async Task Reply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return;
            
            SocketMessage message = (SocketMessage)interaction.Source.Message;

            await message.Channel.SendMessageAsync(text, messageReference: new MessageReference(message.Id));           
        }

        internal async Task CommandReply(Interaction interaction, string text)
        {
            if(interaction.Source.Message is null) return;

            SocketSlashCommand command = (SocketSlashCommand)interaction.Source.Message;

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = text;
            });
        }

        /*


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
