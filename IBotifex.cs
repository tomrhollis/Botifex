namespace Botifex
{
    public interface IBotifex
    {
        public List<SlashCommand> Commands { get; }

        void RegisterCommandHandler(EventHandler<CommandReceivedEventArgs> handler);
        void RegisterTextHandler(EventHandler<MessageReceivedEventArgs> handler);
    }
}