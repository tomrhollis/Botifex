using Discord;
using Discord.WebSocket;

namespace Botifex
{
    public interface IDiscord
    {
        internal DiscordSocketClient DiscordClient { get; }
        internal ITextChannel LogChannel { get; }


        internal Task OnConnect();/*
        internal Task SlashCommandHandler(SocketSlashCommand command);

        internal Task ButtonHandler(SocketMessageComponent component);

        internal Task SelectMenuHandler(SocketMessageComponent component);
        */

    }
}
