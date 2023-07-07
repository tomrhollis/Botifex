using Discord.WebSocket;

namespace Botifex.Services.Discord
{
    internal abstract class DiscordInteraction : Interaction
    {
        internal DiscordInteraction(InteractionSource source) : base(source) { }
        protected IDisposable? isTyping = null;
        internal SocketMessageComponent? MenuComponent { get; set; } = null;

        public override async Task Reply(string text)
        {
            await base.Reply(text);
            isTyping?.Dispose();
        }

        public override async Task ReplyWithOptions(ReplyMenu menu, string? text)
        {
            await base.ReplyWithOptions(menu, text);
            isTyping?.Dispose();
        }

        public override async Task End()
        {
            isTyping?.Dispose();
            await base.End();
        }
    }
}
