using Botifex.Models;
using Botifex.Services;

namespace Botifex
{
    public interface IBotifex
    {
        public void AddCommand(SlashCommand command);
        public Task LogAll(string message);
        
        public void RegisterCommandHandler(EventHandler<InteractionReceivedEventArgs> handler);
        public void RegisterTextHandler(EventHandler<InteractionReceivedEventArgs> handler);
        Task ReplaceStatusMessage(string newMessage);
        Task SendOneTimeStatusUpdate(string message, bool notification = false);
        public Task SendStatusUpdate(string message);
        Task SendToUser(BotifexUser user, string message);
    }
}