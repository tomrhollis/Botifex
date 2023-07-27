using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Botifex.Services
{
    /// <summary>
    /// Abstraction covering any kind of messenger the Botifex system will interact with
    /// </summary>
    public abstract class Messenger
    {
        internal IHostApplicationLifetime appLifetime;
        internal IConfigurationSection config;
        internal ICommandLibrary commandLibrary;
        internal event EventHandler<EventArgs>? OnFirstReady;
        internal event EventHandler<InteractionReceivedEventArgs>? OnMessageReceived;
        internal event EventHandler<InteractionReceivedEventArgs>? OnCommandReceived;
        protected private IInteractionFactory? interactionFactory;

        /// <summary>
        /// The maximum length of a text message for this kind of messenger
        /// </summary>
        internal abstract int MAX_TEXT_LENGTH { get; }

        /// <summary>
        /// Whether this messenger is ready for use
        /// </summary>
        public virtual bool IsReady { get; protected private set; }

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

        /// <summary>
        /// Send a message to the baked-in logging system and to the log channel for this messenger if one was specified in configuration
        /// </summary>
        /// <param name="m">The log message to send</param>
        /// <param name="i">The <see cref="LogLevel"/> of importance</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task Log(string m, LogLevel i);

        internal abstract void OnStopping();
        internal abstract void OnStopped();

        internal virtual void ClearCommandHandler() => OnCommandReceived = null;

        internal virtual void ClearMessageHandler() => OnMessageReceived = null;

        /// <summary>
        /// Create the updateable status message in the status channel, or update it if it already exists
        /// </summary>
        /// <param name="s">The text that the message should contain</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task CreateOrUpdateStatus(string s);

        /// <summary>
        /// Send a one-off message to the status channel
        /// </summary>
        /// <param name="s">The message to send</param>
        /// <param name="n">Whether it should trigger a notification for users</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task SendOneTimeStatus(string s, bool n);

        /// <summary>
        /// Recreate the updateable status message as the latest message and replace the old one with the specified text. Delete the old message if the string is empty.
        /// </summary>
        /// <param name="s">The text to replace the old status message with</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task ReplaceStatus(string s);

        /// <summary>
        /// Shorten text to within the maximum length the messenger can send
        /// </summary>
        /// <param name="text">The text to shorten</param>
        /// <returns>The text minus anything after the character limit minus 5</returns>
        internal virtual string Truncate(string text) => (text.Length > (MAX_TEXT_LENGTH - 5) ? text.Substring(0, MAX_TEXT_LENGTH - 5) + "..." : text);

        /// <summary>
        /// Send a message directly to a user
        /// </summary>
        /// <param name="messengerUser">The <see cref="IMessengerUser"/> object representing a particular messenger account</param>
        /// <param name="message">The text to send</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task SendMessageToUser(IMessengerUser messengerUser, string message);

        /// <summary>
        /// Send a reply in an interaction
        /// </summary>
        /// <param name="interaction">The <see cref="Interaction"/> related to the reply</param>
        /// <param name="text">The text of the reply</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task Reply(Interaction interaction, string text);

        /// <summary>
        /// Send a reply that contains menu options
        /// </summary>
        /// <param name="interaction">The <see cref="Interaction"/> the reply is part of</param>
        /// <param name="text">Text to append before the menu, if any</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task ReplyWithOptions(Interaction interaction, string? text);

        /// <summary>
        /// Remove an interaction from this messenger's interaction history
        /// </summary>
        /// <param name="i">The <see cref="Interaction"/> to remove</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal abstract Task RemoveInteraction(Interaction i);

        /// <summary>
        /// Trigger the event that a message was received (should be triggered after any processing)
        /// </summary>
        /// <param name="e">The <see cref="InteractionReceivedEventArgs"/> containing the <see cref="Interaction"/> that this message is now part of</param>
        internal virtual void FinalizeMessageReceived(InteractionReceivedEventArgs e)
        {
            EventHandler<InteractionReceivedEventArgs>? handler = OnMessageReceived;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Trigger the event that a command was received (should be triggered after any processing)
        /// </summary>
        /// <param name="e">The <see cref="InteractionReceivedEventArgs"/> containing the <see cref="Interaction"/> that this command is now part of</param>
        internal virtual void FinalizeCommandReceived(InteractionReceivedEventArgs e)
        {
            EventHandler<InteractionReceivedEventArgs>? handler = OnCommandReceived;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Trigger the event that this messenger is ready to go
        /// </summary>
        /// <param name="e"></param>
        internal virtual void FinalizeFirstReady(EventArgs e)
        {
            EventHandler<EventArgs>? handler = OnFirstReady;
            if (handler is not null)
            {
                handler(this, e);
            }
        }

        /// <summary>
        /// Register all the commands in the <see cref="commandLibrary"/> with the messenger service
        /// </summary>
        /// <returns><see cref="Task.CompletedTask"/></returns>
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

