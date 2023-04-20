using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal abstract class TelegramInteraction : Interaction
    {
        internal TelegramInteraction(InteractionSource source) : base(source) { }

        internal override void End()
        {
            
        }
    }
}