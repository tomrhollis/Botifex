using Botifex.Services;

namespace Botifex.Models
{
    /// <summary>
    /// A user of the Botifex system. Currently one BotifexUser per <see cref="IMessengerUser"/>, but in the future multiple accounts from different services
    /// will be able to be tied together under one BotifexUser
    /// </summary>
    public class BotifexUser
    {
        /// <summary>
        /// The accounts owned by this <see cref="BotifexUser"/>
        /// </summary>
        public List<IMessengerUser> Accounts { get; set; }

        /// <summary>
        /// A unique ID for this user
        /// </summary>
        public Guid Guid { get; private set; }

        /// <summary>
        /// The display name of the primary account
        /// </summary>
        public string UserName { get => Accounts[0].Name; } // more useful functionality is in the wishlist

        /// <summary>
        /// The at-username of the primary account
        /// </summary>
        public string? At { get => (Accounts[0].At == "@" ? null : Accounts[0].At); }

        public BotifexUser(IMessengerUser account) 
        {
            Guid = Guid.NewGuid();
            Accounts = new List<IMessengerUser>
            {
                account
            };
        }
    }
}
