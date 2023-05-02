using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal class TelegramUser : IMessengerUser
    {
        internal User Account { get; private set; }
        public string Name { get => (Account.FirstName + " " + Account.LastName); }
        public string Id { get => Account.Id.ToString(); }
        internal TelegramUser(User user)
        {
            Account = user;
        }   
    }
}
