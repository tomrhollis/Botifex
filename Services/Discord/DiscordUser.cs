using Discord.WebSocket;

namespace Botifex.Services
{
    internal class DiscordUser : IMessengerUser
    {
        internal SocketUser Account { get; private set; }
        public string Name { get => Account.Username; }
        internal DiscordUser(SocketUser user)
        {
            Account = user;
        }
    }
}
