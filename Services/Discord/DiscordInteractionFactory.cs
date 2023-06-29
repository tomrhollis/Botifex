using Discord.WebSocket;


namespace Botifex.Services.Discord
{
    internal class DiscordInteractionFactory : IInteractionFactory
    {
        private ICommandLibrary commandLibrary;
        internal DiscordInteractionFactory(ICommandLibrary commandLibrary) => this.commandLibrary = commandLibrary;

        public Interaction CreateInteraction(InteractionSource source)
        {
            if (source.User.Messenger is not DiscordService) throw new ArgumentException();

            if (source.Message is SocketSlashCommand)
            {
                string commandName = ((SocketSlashCommand)source.Message).CommandName;
                if (commandLibrary.HasCommand(commandName))
                    return new DiscordCommandInteraction(source, commandLibrary.GetCommand(commandName));
                else throw new ArgumentException($"Botifex doesn't know the command /{commandName}");
            }
                
            if (source.Message is SocketMessage) return new DiscordTextInteraction(source);

            throw new ArgumentException($"Discord interaction type {source?.Message?.GetType()} not implemented");            
        }
    }
}
