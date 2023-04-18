using Botifex.Services;

namespace Botifex.Models
{
    public class BotifexUser
    {
        public List<IMessengerUser> Accounts { get; set; }

        public BotifexUser(IMessengerUser account) 
        {
            Accounts = new List<IMessengerUser>
            {
                account
            };
        }
    }
}
