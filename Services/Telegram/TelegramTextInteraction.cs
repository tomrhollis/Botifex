using Telegram.Bot.Types;

namespace Botifex.Services
{
    public class TelegramTextInteraction : TelegramInteraction, ITextInteraction
    {
        private Message? initialMessage;
        public string Text { get => initialMessage?.Text ?? ""; }

        internal TelegramTextInteraction(InteractionSource source) : base(source)
        {
            initialMessage = (Message?)source.Message;
            
        }


    }
}