using Microsoft.Extensions.DependencyInjection;
using Botifex.Services;

namespace Botifex
{
    public static class BotifexServiceExtensions
    {
        public static IServiceCollection AddMyClasses(this IServiceCollection services)
        {
            services.AddSingleton<IDiscord, DiscordService>()
                    .AddSingleton<ITelegram, TelegramService>()
                    .AddSingleton<IBotifex, Botifex>()
                    .AddSingleton<ICommandLibrary, CommandLibrary>();
            return services;
        }
    }
}
