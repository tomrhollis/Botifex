using Botifex.Services;

namespace Botifex
{
    public interface IBotifex
    {
        public void AddCommand(SlashCommand command);
        public Task LogAll(string message);
        
        public void RegisterCommandHandler(EventHandler<InteractionReceivedEventArgs> handler);
        public void RegisterTextHandler(EventHandler<InteractionReceivedEventArgs> handler);
        public Task SendStatusUpdate(string message);
    }
}