using Discord.WebSocket;

namespace Botifex.Services.Discord
{
    internal class DiscordCommandInteraction : DiscordInteraction, ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }
        private SocketSlashCommand? initialCommand;

        internal DiscordCommandInteraction(InteractionSource source, SlashCommand command) :
            base(source)
        {
            initialCommand = (SocketSlashCommand?)source.Message;
            BotifexCommand = command;
            isTyping = initialCommand?.Channel.EnterTypingState(); // show "Botname is typing" in Discord for immersion

            // populate the interaction's CommandField dictionary with the data coming from Discord
            if (command.Options.Count > 0 && initialCommand is not null && initialCommand.Data.Options.Count > 0)
            {
                foreach (var option in command.Options)
                {
                    // if there's a Discord field matching this Botifex-defined field, get it
                    SocketSlashCommandDataOption? discordField = initialCommand?.Data.Options.FirstOrDefault(o => o.Name.ToLower() == option.Name.ToLower());

                    // Add the data (if any) or blank (if none) to this object's dictionary
                    CommandFields.Add(option.Name.ToLower(), discordField?.Value.ToString() ?? string.Empty);
                }
            }                
        }

        public override async Task Reply(string text)
        {
            await ((DiscordService)Source.User.Messenger).CommandReply(this, text);
            isTyping?.Dispose(); // stop "Botname is typing"
        }

        public override async Task ReplyWithOptions(ReplyMenu menu, string? text = null)
        {
            Menu = menu;
            await ((DiscordService)Source.User.Messenger).CommandReplyWithOptions(this, text);
            isTyping?.Dispose();
        }
    }
}
