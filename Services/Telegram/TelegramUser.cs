using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal class TelegramUser : IMessengerUser
    {
        internal User Account { get; private set; }
        internal TelegramUser(User user)
        {
            Account = user;
        }   
    }
}
