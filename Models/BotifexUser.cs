using Botifex.Services;

namespace Botifex.Models
{
    public class BotifexUser
    {
        public List<IMessengerUser> Accounts { get; set; }
        public Guid Guid { get; private set; }

        public string UserName { get => Accounts[0].Name; } // more useful functionality is in the wishlist

        public BotifexUser(IMessengerUser account) 
        {
            Guid = new Guid();
            Accounts = new List<IMessengerUser>
            {
                account
            };
        }
    }
}
