using Discord.WebSocket;

namespace Botifex.Services
{
    internal class DiscordUser : IMessengerUser
    {
        internal SocketUser Account { get; private set; }
        internal DiscordUser(SocketUser user)
        {
            Account = user;
        }
    }
}
