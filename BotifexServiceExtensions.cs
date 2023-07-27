using Microsoft.Extensions.DependencyInjection;
using Botifex.Services.Discord;
using Botifex.Services.TelegramBot;

namespace Botifex
{
    /// <summary>
    /// Set up Botifex's main objects for dependency injection
    /// </summary>
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
