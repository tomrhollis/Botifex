using Discord.WebSocket;

namespace Botifex.Services
{
    public class InteractionSource
    {
        internal IMessengerUser User { get; set; }
        internal object? Message {  get; set; }

        public string MessageId
        {
            get
            {
                if (Message is SocketSlashCommand)
                    return (Message as SocketSlashCommand)!.Id.ToString();
                if (Message is SocketMessage)
                    return (Message as SocketMessage)!.Id.ToString();
                if(Message is Telegram.Bot.Types.Message)
                    return (Message as Telegram.Bot.Types.Message)!.MessageId.ToString();
                return "0";
            }
        }

        public string ChannelId
        {
            get
            {
                if (Message is SocketSlashCommand)
                    return (Message as SocketSlashCommand)!.ChannelId?.ToString() ?? "0";
                if (Message is SocketMessage)
                    return (Message as SocketMessage)!.Channel.Id.ToString();
                if (Message is Telegram.Bot.Types.Message)
                    return (Message as Telegram.Bot.Types.Message)!.Chat.Id.ToString();
                return "0";
            }
        }

        public string Username
        {
            get
            {
                if (Message is SocketSlashCommand)
                    return (Message as SocketSlashCommand)!.User.Username;
                if (Message is SocketMessage)
                    return (Message as SocketMessage)!.Author.Username;
                if (Message is Telegram.Bot.Types.Message)
                    return (Message as Telegram.Bot.Types.Message)!.Chat.Username ?? "Unknown";
                return "Unknown";
            }
        }

        internal InteractionSource (IMessengerUser user, object? message = null)
        {
            User = user;
            Message = message;
        }
    }
}