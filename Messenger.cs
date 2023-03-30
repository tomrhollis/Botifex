
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex
{
    public abstract class Messenger : IHostedService
    {
        public abstract bool IsReady { get; }

        internal IHostApplicationLifetime appLifetime;
        internal IHost host;
        internal IConfigurationSection config;
        private protected Botifex? botifex;

        internal abstract int MAX_TEXT_LENGTH { get; }

        public Messenger(IHost host, IHostApplicationLifetime lifetime, IConfigurationSection cfg) 
        {
            if (cfg == null)
            {
                Log("no configuration found", LogLevel.Error);
                throw new InvalidOperationException("No configuration found");
            }

            appLifetime = lifetime;
            this.host = host;
            config = cfg;
        }
        internal abstract Task Log(string m, LogLevel i);

        public abstract Task StartAsync(CancellationToken cancellationToken);

        public abstract Task StopAsync(CancellationToken cancellationToken);

        internal abstract void OnStarted();
        internal abstract void OnStopping();
        internal abstract void OnStopped();

        internal abstract Task LoadCommands();

        internal void RegisterSelf(Botifex b)
        {
            botifex = b;
        }
    }
}
