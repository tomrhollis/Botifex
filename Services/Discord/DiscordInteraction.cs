
namespace Botifex.Services
{
    internal abstract class DiscordInteraction : Interaction
    {
        internal DiscordInteraction(InteractionSource source) : base(source) { }
        protected IDisposable? isTyping = null;

        public override async Task Reply(string text, Dictionary<string, string>? options = null)
        {
            await base.Reply(text, options);
            isTyping?.Dispose();
        }

        internal override void End()
        {
            isTyping?.Dispose();
        }
    }
}
