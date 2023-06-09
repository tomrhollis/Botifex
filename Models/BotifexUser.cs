﻿using Botifex.Services;

namespace Botifex.Models
{
    public class BotifexUser
    {
        public List<IMessengerUser> Accounts { get; set; }
        public Guid Guid { get; private set; }

        public string UserName { get => Accounts[0].Name; } // more useful functionality is in the wishlist

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
