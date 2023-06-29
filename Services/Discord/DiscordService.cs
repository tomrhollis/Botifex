using Discord;
using Discord.Net;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace Botifex.Services.Discord
{
    public class DiscordService : Messenger, IDiscord
    {
        public DiscordSocketClient DiscordClient { get; set; }
        public ITextChannel? LogChannel { get; set; }
        public ITextChannel? StatusChannel { get; set; }
        internal override int MAX_TEXT_LENGTH { get => 2000; }
        private ulong StatusMessageId { get; set; } = 0;

        private List<ulong> adminIds = new List<ulong>();
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

            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            DiscordClient = new DiscordSocketClient(new DiscordSocketConfig() { AlwaysDownloadUsers = true });
            DiscordClient.Log += DiscordLog;
            DiscordClient.Ready += OnConnect;

            if (!String.IsNullOrEmpty(config.GetValue<string>("DiscordBotToken")))
            {
                DiscordClient.LoginAsync(TokenType.Bot, config.GetValue<string>("DiscordBotToken")).Wait();
                DiscordClient.StartAsync().Wait();
                Log("Yip Yip", LogLevel.Information).Wait();
            }
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

            LoadAdminIds(config?.GetSection("DiscordAdminAllowlist").Get<string[]>());
            FinalizeFirstReady(EventArgs.Empty);

            DiscordClient.MessageReceived += MessageHandler;
            DiscordClient.SlashCommandExecuted += SlashCommandHandler;
            DiscordClient.ButtonExecuted += ButtonHandler;
            
            //DiscordClient.SelectMenuExecuted += SelectMenuHandler;
            
        }

        private async Task DiscordLog(LogMessage msg)
        {
            await Log(msg.ToString(), LogLevel.Debug);
        }

        internal override async Task Log(string m, LogLevel i = LogLevel.Information)
        {
            log.Log(i, "Discord: "+ m);
#if !DEBUG
            if(i == LogLevel.Debug) return;
#endif
            if (IsReady && LogChannel is not null)
            {
                await LogChannel.SendMessageAsync(Truncate($"[{i}] {m}"));
            }
        }

        private void LoadAdminIds(string[]? usernames)
        {
            if (usernames is null || usernames.Length == 0) return;

            List<SocketUser> adminUsers = new List<SocketUser>();

            foreach (string username in usernames)
            {
                if (!Regex.Match(username, "^(?!(discordtag|here|everyone)).[^\\@\\#\\:]{2,32}#[\\d]{4}$").Success)
                    throw new ArgumentException($"{username} is not a proper Discord username");

                Match splitName = Regex.Match(username, "^(.*)#(.*)$");
                SocketUser adminUser = DiscordClient.GetUser(splitName.Groups[1].Value, splitName.Groups[2].Value);
                
                // if discord couldn't find this user, ignore and move on                
                if (adminUser is null)
                    continue;
                
                adminUsers.Add(adminUser);
            }   
            adminIds = (adminUsers.Count == 0) ? new List<ulong>() : adminUsers.Select(u=>u.Id).ToList();
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
                newCommand.WithDMPermission(!botifexCommand.AdminOnly);

                if (botifexCommand.AdminOnly)
                    newCommand.WithDefaultMemberPermissions(GuildPermission.ManageEvents);
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

        internal override async Task SendOneTimeStatus(string statusText, bool notification = false)
        {
            if (!IsReady || StatusChannel is null) return;

            if (notification) statusText += "\n@here";
            await StatusChannel.SendMessageAsync(Truncate(statusText));
        }
                    

        private Task MessageHandler(SocketMessage message)
        {
            // ignore if text message is from bot, is empty
            if (message.Author.IsBot || String.IsNullOrEmpty(message.Content)) return Task.CompletedTask;

            // if it's not from a DM make sure it has a mention of the bot otherwise abort (requires the intent for bots to see contents of messages in group chats)
            if(message.Channel.GetChannelType() != ChannelType.DM && !message.MentionedUsers.Contains<SocketUser>(DiscordClient.CurrentUser)) return Task.CompletedTask;

            DiscordInteraction? interaction = (DiscordInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(this, message.Author), message));
            if (interaction is null) return Task.CompletedTask;

            activeInteractions.Add(interaction);
            FinalizeMessageReceived(new InteractionReceivedEventArgs(interaction));
            return Task.CompletedTask;
        }

        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            DiscordCommandInteraction? interaction = 
                (DiscordCommandInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(this, command.User), command));
            bool isEphemeral = command.Channel.GetChannelType().GetValueOrDefault() != ChannelType.DM;

            if (interaction is null)
            {
                await command.DeleteOriginalResponseAsync();
                return;
            }
            activeInteractions.Add(interaction);

            // if it's admin only, reject if it isn't from a proper source
            if(interaction.BotifexCommand.AdminOnly && 
                ((command.ChannelId != (StatusChannel?.Id ?? 0) && command.ChannelId != (LogChannel?.Id ?? 0))  // it's not in either of the specified channels
                || (adminIds.Count > 0 && !adminIds.Contains(command.User.Id)))) // the admins have been specified and it's not one of these
            {
                bool specificAdmins = (adminIds.Count > 0);
                await command.RespondAsync($"Sorry, only {(specificAdmins ? "specific " : "")}admins in the proper server and channel can use that command", ephemeral: isEphemeral);
                await interaction.End();
                return;
            }

            await command.DeferAsync(ephemeral: isEphemeral);
            FinalizeCommandReceived(new InteractionReceivedEventArgs(interaction));
        }

        private async Task ButtonHandler(SocketMessageComponent component)
        {
            await component.DeferAsync();
            string data = component.Data.CustomId;
            string guid = Regex.Match(data, "^[^|]*").Value;
            string choice = Regex.Match(data, "[^|]*$").Value;
            Interaction? interaction = activeInteractions.SingleOrDefault(i => i.Id.ToString() == guid);
            if (interaction is not null && interaction.Menu is not null)
            {
                ((DiscordInteraction)interaction).MenuComponent = component; 
                interaction.ChooseMenuOption(choice);
            }

        }
        /*

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

        internal override async Task Reply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return;

            SocketMessage message = (SocketMessage)interaction.Source.Message;
            await message.Channel.SendMessageAsync(text, messageReference: new MessageReference(message.Id), components: null);

        }

        internal override async Task ReplyWithOptions(Interaction interaction, string? text = null)
        {
            if (interaction.Source.Message is null) return;
            
            SocketMessage message = (SocketMessage)interaction.Source.Message;
            MessageComponent? buttonComponent = null;
            if (interaction.Menu is not null && interaction.Menu.Options.Count > 0)
            {
                var componentBuilder = new ComponentBuilder();
                buttonComponent = BuildButtonComponent(interaction.Menu, interaction.Id.ToString());
                text += ("\n" + interaction.Menu.MenuText).Trim();
            }

            await message.Channel.SendMessageAsync(text, messageReference: new MessageReference(message.Id), components: buttonComponent);           
        }

        internal async Task CommandReply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return;

            SocketSlashCommand command = (SocketSlashCommand)interaction.Source.Message;

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = Truncate(text);
                m.Components = null;
            });
        }

        internal async Task CommandReplyWithOptions(Interaction interaction, string? text = "")
        {
            if(interaction.Source.Message is null) return;

            SocketSlashCommand command = (SocketSlashCommand)interaction.Source.Message;                   

            await command.ModifyOriginalResponseAsync(m =>
            {
                if (interaction.Menu is not null && interaction.Menu.Options.Count > 0)
                {
                    var componentBuilder = new ComponentBuilder();
                    m.Components = BuildButtonComponent(interaction.Menu, interaction.Id.ToString());
                    text += "\n" + interaction.Menu.MenuText;
                    text = text.Trim();
                }

                if (!String.IsNullOrEmpty(text)) m.Content = Truncate(text);
            });
        }

        private MessageComponent BuildButtonComponent(ReplyMenu optionsMenu, string guid)
        {
            var componentBuilder = new ComponentBuilder();
            Dictionary<string, string> options = optionsMenu.Options;

            for(int i =0; i<options.Count; i++)
            {
                componentBuilder.WithButton(ButtonBuilder.CreatePrimaryButton($"{(optionsMenu.NumberedChoices ? i+1 : options.Keys.ToArray()[i])}", $"{guid}|{options.Keys.ToArray()[i]}"));
            }
            return componentBuilder.Build();
        }


        internal override async Task RemoveInteraction(Interaction i)
        {
            DiscordInteraction interaction = (DiscordInteraction)i;
            activeInteractions.Remove(interaction);

            if(interaction.Menu is not null)
            {
                if(interaction.BotMessage is SocketMessage)
                {
                    SocketMessage message = (SocketMessage)interaction.BotMessage;
                    await message.DeleteAsync();
                }
                else if (interaction.BotMessage is SocketSlashCommand)
                {
                    SocketSlashCommand message = (SocketSlashCommand)interaction.BotMessage!;
                    await message.DeleteOriginalResponseAsync();
                }
            }
        }

        internal override async Task SendMessageToUser(IMessengerUser user, string message)
        {
            if (user is not DiscordUser) throw new ArgumentException();
            
            await ((DiscordUser)user).Account.SendMessageAsync(Truncate(message));
        }

        /// <summary>
        /// Replace the current status message with specific text, then start over with a new status message below it
        /// </summary>
        /// <param name="text">The text to replace the old message with</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal override async Task ReplaceStatus(string text)
        {
            if (StatusMessageId == 0 || StatusChannel is null) return; // nothing to edit

            // save the old message text before replacing it
            string status = (await StatusChannel.GetMessageAsync(StatusMessageId)).Content;
            await CreateOrUpdateStatus(text);

            // make the new message
            StatusMessageId = 0;
            await CreateOrUpdateStatus(status);            
        }
    }
}
