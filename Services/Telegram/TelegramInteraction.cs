using Telegram.Bot.Types;

namespace Botifex.Services
{
    public abstract class TelegramInteraction : Interaction
    {
        internal TelegramInteraction(InteractionSource source) : base(source) { }


    }
}