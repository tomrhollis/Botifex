using Discord.WebSocket;

namespace Botifex.Services
{
    /// <summary>
    /// Information about where an <see cref="Interaction"/> originated from
    /// </summary>
    public class InteractionSource
    {
        /// <summary>
        /// The messenger-specific account of the user who originated an interaction
        /// </summary>
        internal IMessengerUser User { get; set; }

        /// <summary>
        /// The messenger-specific message object they sent
        /// </summary>
        internal object? Message {  get; set; }

        /// <summary>
        /// String form of the message ID for the message that originated this interaction
        /// </summary>
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

        /// <summary>
        /// String form of the channel ID where the interaction originated
        /// </summary>
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

        /// <summary>
        /// Username of the user who originated the interaction
        /// </summary>
        public string Username
        {
            get
            {
                if (Message is SocketSlashCommand)
                    return (Message as SocketSlashCommand)!.User.Username;
                if (Message is SocketMessage)
                    return (Message as SocketMessage)!.Author.Username;
                if (Message is Telegram.Bot.Types.Message)
                    return (Message as Telegram.Bot.Types.Message)!.Chat.Username ?? "Unknown"; // telegram doesn't give you usernames outside of DMs
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