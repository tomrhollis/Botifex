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
        private ILogger<DiscordService> log;
        private List<ulong> adminIds = new List<ulong>();
        private List<DiscordInteraction> activeInteractions = new List<DiscordInteraction>();
        private ulong StatusMessageId { get; set; } = 0;

        internal override int MAX_TEXT_LENGTH { get => 2000; }

        public DiscordSocketClient DiscordClient { get; set; }
        public ITextChannel? LogChannel { get; set; }
        public ITextChannel? StatusChannel { get; set; }

        public override bool IsReady
        {
            get=>DiscordClient is not null && DiscordClient.ConnectionState == ConnectionState.Connected;
        }        

        
        public DiscordService(ILogger<DiscordService> log, IConfiguration cfg, IHostApplicationLifetime lifetime, ICommandLibrary lib) 
            : base(lifetime, cfg.GetSection("Discord"), lib)
        {
            this.log = log;
            interactionFactory = new DiscordInteractionFactory(lib);

            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);

            // set up discord client
            DiscordClient = new DiscordSocketClient(new DiscordSocketConfig() { AlwaysDownloadUsers = true });
            DiscordClient.Log += DiscordLog;
            DiscordClient.Ready += OnConnect;

            // start discord client
            if (!String.IsNullOrEmpty(config.GetValue<string>("DiscordBotToken")))
            {
                DiscordClient.LoginAsync(TokenType.Bot, config.GetValue<string>("DiscordBotToken")).Wait();
                DiscordClient.StartAsync().Wait();
            }
        }

        internal override async void OnStopping()
        {
            log.LogDebug("OnStopping has been called.");
            await DiscordClient.StopAsync();
        }

        internal override void OnStopped()
        {
            log.LogDebug("OnStopped has been called.");
        }

        /// <summary>
        /// Set everything up now that the client has connected successfully
        /// </summary>
        /// <returns><see cref="Task.CompletedTask"/></returns>
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
            
            LoadAdminIds(config?.GetSection("DiscordAdminAllowlist").Get<string[]>());
            FinalizeFirstReady(EventArgs.Empty); // trigger the Botifex onReady event

            DiscordClient.MessageReceived += MessageHandler;
            DiscordClient.SlashCommandExecuted += SlashCommandHandler;
            DiscordClient.ButtonExecuted += ButtonHandler;
        }

        /// <summary>
        /// Get numerical IDs for each admin specified in the configuration and store them for later
        /// </summary>
        /// <param name="usernames">An array of usernames from the configuration file</param>
        private void LoadAdminIds(string[]? usernames)
        {
            if (usernames is null || usernames.Length == 0) return;

            List<SocketUser> adminUsers = new List<SocketUser>();

            // get a Discord user object for each username (if they're in the same server as the bot)
            foreach (string username in usernames)
            {
                SocketUser adminUser = DiscordClient.GetUser(username);

                // if discord couldn't find this user, ignore and move on                
                if (adminUser is null)
                    continue;

                adminUsers.Add(adminUser);
            }
            adminIds = (adminUsers.Count == 0) ? new List<ulong>() : adminUsers.Select(u => u.Id).ToList(); // we only need the IDs for later
        }

        // Catch Discord's log event and translate it to our own Log method
        private async Task DiscordLog(LogMessage msg)
        {
            await Log(msg.ToString(), LogLevel.Information);
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        internal override async Task LoadCommands()
        {
            //DiscordClient.GetGlobalApplicationCommandsAsync().Result.ToList().ForEach(c => { c.DeleteAsync(); }); // only uncomment to fix problems during testing
            List<SlashCommandProperties> discordSlashCommands = new List<SlashCommandProperties>();            

            // turn each command into a Discord command object
            foreach(var command in commandLibrary.Commands)
                discordSlashCommands.Add(BuildCommand(command));

            // push them to Discord
            try
            {
                await DiscordClient.BulkOverwriteGlobalApplicationCommandsAsync(discordSlashCommands.ToArray());
            }
            catch (HttpException ex)
            {
                await Log(ex.GetType() + ": " + ex.Message, LogLevel.Warning);
            }
        }

        // create a Discord command object for a Botifex command
        private SlashCommandProperties BuildCommand(SlashCommand botifexCommand)
        {
            var newCommand = new SlashCommandBuilder().WithName(botifexCommand.Name).WithDescription(botifexCommand.Description);

            // add fields to the command
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

        /// <inheritdoc/>
        internal override async Task CreateOrUpdateStatus(string statusText)
        {
            if (!IsReady || StatusChannel is null) return;

            // create
            if (StatusMessageId == 0)
                StatusMessageId = (await StatusChannel.SendMessageAsync(Truncate(statusText))).Id;

            // update
            else
                await StatusChannel.ModifyMessageAsync(StatusMessageId, (m) =>
                {
                    m.Content = statusText;
                });
        }

        /// <inheritdoc/>
        internal override async Task SendOneTimeStatus(string statusText, bool notification = false)
        {
            if (!IsReady || StatusChannel is null) return;

            if (notification) statusText = "@here"+"\n";
            await StatusChannel.SendMessageAsync(Truncate(statusText));
        }
                    
        // process text messages to the bot
        private Task MessageHandler(SocketMessage message)
        {
            // ignore if text message is from this or another bot, or is empty
            if (message.Author.IsBot || String.IsNullOrEmpty(message.Content)) return Task.CompletedTask;

            // if it's not from a DM make sure it has a mention of the bot otherwise abort (requires the intent for bots to see contents of messages in group chats)
            if(message.Channel.GetChannelType() != ChannelType.DM && !message.MentionedUsers.Contains<SocketUser>(DiscordClient.CurrentUser)) return Task.CompletedTask;

            DiscordInteraction? interaction = (DiscordInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(this, message.Author), message));
            if (interaction is null) return Task.CompletedTask;

            activeInteractions.Add(interaction);
            FinalizeMessageReceived(new InteractionReceivedEventArgs(interaction)); // trigger Botifex event for text messages received
            return Task.CompletedTask;
        }

        // process commands to the bot
        private async Task SlashCommandHandler(SocketSlashCommand command)
        {
            DiscordCommandInteraction? interaction = 
                (DiscordCommandInteraction?)interactionFactory?.CreateInteraction(new InteractionSource(new DiscordUser(this, command.User), command));

            if (interaction is null)
            {
                await command.DeleteOriginalResponseAsync();
                return;
            }
            activeInteractions.Add(interaction);

            bool isEphemeral = command.Channel.GetChannelType().GetValueOrDefault() != ChannelType.DM; // if this is in a channel, only show responses to the user who sent the command

            // if it's admin only, reject if it isn't from a proper source
            if (interaction.BotifexCommand.AdminOnly && 
                ((command.ChannelId != (StatusChannel?.Id ?? 0) && command.ChannelId != (LogChannel?.Id ?? 0))  // it's not in either of the specified channels
                || (adminIds.Count > 0 && !adminIds.Contains(command.User.Id)))) // the admins have been specified and it's not one of these
            {
                bool specificAdmins = (adminIds.Count > 0);
                await command.RespondAsync($"Sorry, only {(specificAdmins ? "specific " : "")}admins in the proper server and channel can use that command", ephemeral: isEphemeral);
                await interaction.End();
                return;
            }

            await command.DeferAsync(ephemeral: isEphemeral); // send the standard Discord placeholder that we're working on a response
            FinalizeCommandReceived(new InteractionReceivedEventArgs(interaction)); // trigger Botifex event for command received
        }

        // process menu button selection received
        private async Task ButtonHandler(SocketMessageComponent component)
        {
            await component.DeferAsync(); // send the standard Discord placeholder that we're working on a response

            // find the interaction this relates to
            string data = component.Data.CustomId;
            string guid = Regex.Match(data, "^[^|]*").Value;
            string choice = Regex.Match(data, "[^|]*$").Value;
            Interaction? interaction = activeInteractions.SingleOrDefault(i => i.Id.ToString() == guid);

            // pass along the selection to the interaction object
            if (interaction is not null && interaction.Menu is not null)
            {
                ((DiscordInteraction)interaction).MenuComponent = component; 
                interaction.ChooseMenuOption(choice);
            }
        }

        /// <inheritdoc/>
        internal override async Task Reply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return; // can't reply to nothing

            SocketMessage message = (SocketMessage)interaction.Source.Message;
            await message.Channel.SendMessageAsync(text, messageReference: new MessageReference(message.Id), components: null);
        }

        /// <summary>
        /// Send a reply to a user when the interaction is based on command input
        /// </summary>
        /// <param name="interaction">The <see cref="Interaction"/> currently underway</param>
        /// <param name="text">The text to send to the user as a reply</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal async Task CommandReply(Interaction interaction, string text)
        {
            if (interaction.Source.Message is null) return; // can't reply to nothing

            SocketSlashCommand command = (SocketSlashCommand)interaction.Source.Message;

            await command.ModifyOriginalResponseAsync(m =>
            {
                m.Content = Truncate(text);
                m.Components = null; // clear any menu options that might have been used previously
            });
        }

        /// <inheritdoc/>
        internal override async Task ReplyWithOptions(Interaction interaction, string? text = null)
        {
            if (interaction.Source.Message is null) return; // can't reply to nothing

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

        /// <summary>
        /// Send a menu options to a user when the interaction is based on command input
        /// </summary>
        /// <param name="interaction">The <see cref="Interaction"/> currently underway</param>
        /// <param name="text">Any text to send to the user along with the options, if applicable</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal async Task CommandReplyWithOptions(Interaction interaction, string? text = "")
        {
            if(interaction.Source.Message is null) return; // can't reply to nothing

            SocketSlashCommand command = (SocketSlashCommand)interaction.Source.Message;                   

            await command.ModifyOriginalResponseAsync(m =>
            {
                // build the menu buttons and append the menu text to any optional text
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

        // build the MessageComponent that holds all the buttons for the menu options
        private MessageComponent BuildButtonComponent(ReplyMenu optionsMenu, string guid)
        {
            var componentBuilder = new ComponentBuilder();
            Dictionary<string, string> options = optionsMenu.Options;

            for(int i =0; i<options.Count; i++)
            {
                // the button text will be 1 through n if this is a numbered menu, otherwise the raw key
                // ID is a combination of the interaction guid and the key, so we can find the interaction later and match the key even if it's a numbered choice
                componentBuilder.WithButton(ButtonBuilder.CreatePrimaryButton(label: $"{(optionsMenu.NumberedChoices ? i+1 : options.Keys.ToArray()[i])}", 
                                                                              customId: $"{guid}|{options.Keys.ToArray()[i]}"));
            }
            return componentBuilder.Build();
        }

        /// <inheritdoc/>
        internal override async Task RemoveInteraction(Interaction i)
        {
            DiscordInteraction interaction = (DiscordInteraction)i;
            activeInteractions.Remove(interaction);

            // if a menu exists for this interaction, delete it since it won't work anymore
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

        /// <inheritdoc/>
        internal override async Task SendMessageToUser(IMessengerUser user, string message)
        {
            if (user is not DiscordUser) throw new ArgumentException();
            
            await ((DiscordUser)user).Account.SendMessageAsync(Truncate(message));
        }

        /// <inheritdoc/>
        internal override async Task ReplaceStatus(string text)
        {
            if (StatusMessageId == 0 || StatusChannel is null) return; // nothing to edit

            // save the old message text before replacing it
            string status = (await StatusChannel.GetMessageAsync(StatusMessageId)).Content;
            
            // replace the old message with some text, or delete the old message if text is empty
            // (if text is empty, this method is basically "repost the status as a new message")
            if (!string.IsNullOrEmpty(text))
                await CreateOrUpdateStatus(text);
            else
                await StatusChannel.DeleteMessageAsync(StatusMessageId);

            // make the new message
            StatusMessageId = 0;
            await CreateOrUpdateStatus(status);            
        }
    }
}
