using Discord.WebSocket;

namespace Botifex.Services.Discord
{
    internal class DiscordTextInteraction : DiscordInteraction, ITextInteraction
    {
        public SocketMessage? initialMessage { get; set; }
        public string Text { get => initialMessage?.Content ?? ""; }
        
        internal DiscordTextInteraction(InteractionSource source) : base(source)
        {
            initialMessage = (SocketMessage?)source.Message;
            isTyping = initialMessage?.Channel.EnterTypingState();
        }
    }
}