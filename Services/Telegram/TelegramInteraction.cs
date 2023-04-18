using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal abstract class TelegramInteraction : Interaction
    {
        internal TelegramInteraction(InteractionSource source) : base(source) { }

        internal virtual void FollowUp(Update update) { }

        internal override void End()
        {
            
        }
    }
}