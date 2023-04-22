

using Discord.WebSocket;

namespace Botifex.Services
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
            isTyping = initialCommand?.Channel.EnterTypingState();

            if (command.Options.Count > 0 && initialCommand is not null && initialCommand.Data.Options.Count > 0)
                foreach (var option in command.Options)
                    CommandFields.Add(option.Name.ToLower(), initialCommand?.Data.Options.FirstOrDefault(o => o.Name.ToLower() == option.Name.ToLower())?.Value.ToString() ?? string.Empty);

        }

        public override async Task Reply(string text, Dictionary<string, string>? options= null)
        {
            await ((DiscordService)Source.Messenger).CommandReply(this, text, options);
        }
    }
}
