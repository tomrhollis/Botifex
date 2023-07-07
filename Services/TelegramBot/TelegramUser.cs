using Telegram.Bot.Types;

namespace Botifex.Services.TelegramBot
{
    internal class TelegramUser : IMessengerUser
    {
        internal User Account { get; private set; }
        public Messenger Messenger { get; private set; }  
        public string Name { get => (Account.FirstName + " " + Account.LastName); }

        public string At { get => "@"+(Account.Username ?? String.Empty); }
        public string Id { get => Account.Id.ToString(); }
        internal TelegramUser(TelegramService telegram, User user)
        {
            Account = user;
            Messenger = telegram;
        }   
    }
}
