using Telegram.Bot.Types;

namespace Botifex.Services.TelegramBot
{
    public abstract class TelegramInteraction : Interaction
    {
        internal TelegramInteraction(InteractionSource source) : base(source) { }


    }
}