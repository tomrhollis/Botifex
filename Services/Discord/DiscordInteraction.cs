
namespace Botifex.Services
{
    internal abstract class DiscordInteraction : Interaction
    {
        internal DiscordInteraction(InteractionSource source) : base(source) { }

        public override async Task Reply(string text)
        {
            await base.Reply(text);
        }
    }
}
