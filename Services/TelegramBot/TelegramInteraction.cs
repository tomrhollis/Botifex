using Telegram.Bot.Types;

namespace Botifex.Services.TelegramBot
{
    public abstract class TelegramInteraction : Interaction
    {
        Channel Channel { get; set; }
        internal TelegramInteraction(InteractionSource source) : base(source) { }


    }
}