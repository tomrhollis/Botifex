using Discord.WebSocket;

namespace Botifex.Services

{
    internal class DiscordTextInteraction : DiscordInteraction, ITextInteraction
    {
        public SocketMessage? initialMessage { get; set; }
        public string Text { get => initialMessage?.Content ?? ""; }
        private IDisposable? isTyping = null;

        internal DiscordTextInteraction(InteractionSource source) : base(source)
        {
            initialMessage = (SocketMessage?)source.Message;
            isTyping = initialMessage?.Channel.EnterTypingState();
        }

        public override async Task Reply(string text)
        {
            await base.Reply(text);
            isTyping?.Dispose();
        }

        internal override void End()
        {
            throw new NotImplementedException();
        }
    }
}