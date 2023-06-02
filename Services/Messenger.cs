using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex.Services
{
    public abstract class Messenger
    {
        public virtual bool IsReady { get; protected private set; }

        internal IHostApplicationLifetime appLifetime;
        internal IConfigurationSection config;
        internal ICommandLibrary commandLibrary;
        internal event EventHandler<EventArgs>? OnFirstReady;
        internal event EventHandler<InteractionReceivedEventArgs>? OnMessageReceived;
        internal event EventHandler<InteractionReceivedEventArgs>? OnCommandReceived;
        protected private IInteractionFactory? interactionFactory;

        internal abstract int MAX_TEXT_LENGTH { get; }

        public Messenger(IHostApplicationLifetime lifetime, IConfigurationSection cfg, ICommandLibrary lib)
        {
            if (cfg == null)
            {
                Log("no configuration found", LogLevel.Error);
                throw new InvalidOperationException("No configuration found");
            }

            appLifetime = lifetime;
            config = cfg;
            commandLibrary = lib;
        }
        internal abstract Task Log(string m, LogLevel i);

        internal abstract void OnStarted();
        internal abstract void OnStopping();
        internal abstract void OnStopped();

        internal virtual void ClearCommandHandler() => OnCommandReceived = null;

        internal virtual void ClearMessageHandler() => OnMessageReceived = null;

        internal abstract Task CreateOrUpdateStatus(string s);

        internal abstract Task SendOneTimeStatus(string s, bool n);

        internal virtual string Truncate(string text) => (text.Length > (MAX_TEXT_LENGTH - 5) ? text.Substring(0, MAX_TEXT_LENGTH - 5) + "..." : text);
        internal abstract Task SendMessageToUser(IMessengerUser messengerUser, string message);

        internal abstract Task Reply(Interaction interaction, string text);
        internal abstract Task ReplyWithOptions(Interaction interaction, string? text);
        internal abstract Task RemoveInteraction(Interaction i);

        internal virtual void FinalizeMessageReceived(InteractionReceivedEventArgs e)
        {
            EventHandler<InteractionReceivedEventArgs>? handler = OnMessageReceived;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        internal virtual void FinalizeCommandReceived(InteractionReceivedEventArgs e)
        {
            EventHandler<InteractionReceivedEventArgs>? handler = OnCommandReceived;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        internal virtual void FinalizeFirstReady(EventArgs e)
        {
            EventHandler<EventArgs>? handler = OnFirstReady;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        internal abstract Task LoadCommands();

    }

    public class InteractionReceivedEventArgs : EventArgs
    {
        public Interaction Interaction { get; set; }
        public InteractionReceivedEventArgs(Interaction i)
        {
            Interaction = i;
        }
    }
}

