using Discord.WebSocket;

namespace Botifex.Services.Discord
{
    internal class DiscordUser : IMessengerUser
    {
        internal SocketUser Account { get; private set; }
        public Messenger Messenger { get; private set; }
        public string Name { get => Account.Username; }
        public string At { get => "#"+Account.Username+Account.Discriminator; }
        public string Id { get => Account.Id.ToString(); }
        internal DiscordUser(DiscordService discord, SocketUser user)
        {
            Account = user;
            Messenger = discord;
        }
    }
}
