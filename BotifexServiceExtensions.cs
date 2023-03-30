using Microsoft.Extensions.DependencyInjection;

namespace Botifex
{
    public static class BotifexServiceExtensions
    {
        public static IServiceCollection AddMyClasses(this IServiceCollection services)
        {
            services.AddSingleton<IDiscord, Discord>()
                    .AddSingleton<ITelegram, Telegram>()
                    .AddSingleton<IBotifex, Botifex>();
            return services;
        }
    }
}
