using Botifex.Properties;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Botifex
{
    public class Botifex : IBotifex, IHostedService
    {
        private IHost host;
        private IHostApplicationLifetime appLifetime;
        private IConfiguration config;
        private ILogger<Botifex> log;

        private Dictionary<string, SlashCommand> commands = new Dictionary<string, SlashCommand>();
        public List<SlashCommand> Commands { get => commands.Values.ToList(); }

        private Discord discord;
        private Telegram telegram;

        public Botifex(IHost host, IHostApplicationLifetime lifetime, IConfiguration cfg, ILogger<Botifex> log, IDiscord dc, ITelegram tg)
        {
            this.host = host;
            appLifetime = lifetime;
            config = cfg;
            this.log = log;
            discord = (Discord)dc;
            telegram = (Telegram)tg;

            LoadCommands();

            appLifetime.ApplicationStarted.Register(OnStarted);
            appLifetime.ApplicationStopping.Register(OnStopping);
            appLifetime.ApplicationStopped.Register(OnStopped);            
        }

        private void OnStarted()
        {
            log.LogDebug("OnStarted has been called.");
            discord.RegisterSelf(this);
            telegram.RegisterSelf(this);
        }

        private void OnStopping()
        {
            LogAll("Botifex is shutting down").Wait();
            log.LogDebug("OnStopping has been called.");            
        }

        private void OnStopped()
        {
            log.LogDebug("OnStopped has been called.");
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            log.LogDebug("StartAsync has been called.");
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            log.LogDebug("StopAsync has been called.");
        }

        private async Task LogAll(string message)
        {
            
            await discord.Log(message);
            await telegram.Log(message);
        }

        private void LoadCommands()
        {
            try
            {
                SlashCommand[] incomingCommands = config.GetSection("Botifex").GetSection("Commands").Get<SlashCommand[]>();

                foreach (SlashCommand command in incomingCommands)
                {
                    if (commands.ContainsKey(command.Name)) log.LogWarning($"Attempted to add {command.Name} more than once, ignored");
                    else
                    {
                        log.LogDebug("Command found: " + command.Name);
                        commands.Add(command.Name, command);
                    }
                }
            }
            catch (ArgumentException e)
            {
                log.LogError($"{e.Message}");
            }
        }
    }
}