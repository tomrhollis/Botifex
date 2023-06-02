

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

        public override async Task Reply(string text)
        {
            await ((DiscordService)Source.User.Messenger).CommandReply(this, text);
            isTyping?.Dispose();
        }

        public override async Task ReplyWithOptions(ReplyMenu menu, string? text = null)
        {
            Menu = menu;
            await ((DiscordService)Source.User.Messenger).CommandReplyWithOptions(this, text);
            isTyping?.Dispose();
        }
    }
}
