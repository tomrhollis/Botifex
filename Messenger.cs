
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex
{
    public abstract class Messenger
    {
        public virtual bool IsReady { get; protected private set; }

        internal IHostApplicationLifetime appLifetime;
        internal IHost host;
        internal IConfigurationSection config;
        private protected Botifex? botifex;
        internal event EventHandler<MessageReceivedEventArgs> OnMessageReceived;
        internal event EventHandler<CommandReceivedEventArgs> OnCommandReceived;

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

        public abstract Task StartAsync();

        public abstract Task StopAsync();

        internal abstract void OnStarted();
        internal abstract void OnStopping();
        internal abstract void OnStopped();
        
        internal virtual void ClearCommandHandler() => OnCommandReceived = null;

        internal virtual void ClearMessageHandler() => OnMessageReceived = null;

        internal virtual void FinalizeMessageReceived(MessageReceivedEventArgs e)
        {
            EventHandler<MessageReceivedEventArgs> handler = OnMessageReceived;
            if(handler != null)
            {
                handler(this, e);
            }
        }

        internal virtual void FinalizeCommandReceived(CommandReceivedEventArgs e)
        {
            EventHandler<CommandReceivedEventArgs> handler = OnCommandReceived;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        internal abstract Task LoadCommands();

        internal void RegisterSelf(Botifex b)
        {
            botifex = b;
        }
    }

    public class MessageReceivedEventArgs : EventArgs
    {
        public string Message { get; set; }
        public MessageReceivedEventArgs(string message)
        {
            Message = message;
        }
    }

    public class CommandReceivedEventArgs : EventArgs
    {
        public string Command { get; set; }
        public string Options { get; set; }
        public CommandReceivedEventArgs(string command, string options)
        {
            Command = command;
            Options = options;
        }
    }
}
