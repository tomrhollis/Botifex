using Microsoft.Extensions.DependencyInjection;
using Botifex.Services.Discord;
using Botifex.Services.TelegramBot;


namespace Botifex
{
    public static class BotifexServiceExtensions
    {
        public static IServiceCollection AddBotifexClasses(this IServiceCollection services)
        {
            services.AddSingleton<IDiscord, DiscordService>()
                    .AddSingleton<ITelegram, TelegramService>()
                    .AddSingleton<IBotifex, Botifex>()
                    .AddSingleton<ICommandLibrary, CommandLibrary>();
            return services;
        }
    }
}
