using Botifex.Models;

namespace Botifex.Services
{
    /// <summary>
    /// An interaction between a user and the application using the Botifex system
    /// </summary>
    public abstract class Interaction
    {
        /// <summary>
        /// Unique ID of this interaction
        /// </summary>
        public Guid Id { get; private set; }

        /// <summary>
        /// Information about the source of this interaction
        /// </summary>
        public InteractionSource Source { get; set; }

        /// <summary>
        /// The <see cref="BotifexUser"/> who owns the source account
        /// </summary>
        public BotifexUser? User { get; set; }

        /// <summary>
        /// A <see cref="ReplyMenu"/> of options the user can select from, if the interaction calls for one
        /// </summary>
        public ReplyMenu? Menu { get; set; }

        /// <summary>
        /// Whether the interaction is still doing work and hasn't replied to the user yet
        /// </summary>
        public bool IsProcessing { get; protected private set; }

        /// <summary>
        /// Command fields and their associated data, if there are any in this interaction
        /// </summary>
        public virtual Dictionary<string, string> CommandFields { get; set; }

        /// <summary>
        /// The last message from the bot in this interaction
        /// </summary>
        internal object? BotMessage;

        internal Interaction(InteractionSource source)
        {
            Id = new Guid();
            Source = source;
            CommandFields = new Dictionary<string, string>();
            IsProcessing = true;
        }

        /// <summary>
        /// Send a reply to the <see cref="IMessengerUser"/> who initiated this interaction
        /// </summary>
        /// <param name="text">The text of the reply</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public virtual async Task Reply(string text)
        {
            await Source.User.Messenger.Reply(this, text);
            IsProcessing = false;
        }

        /// <summary>
        /// Send a reply to the <see cref="IMessengerUser"/> who initiated this interaction, with an attached <see cref="ReplyMenu"/> of options to choose from
        /// </summary>
        /// <param name="menu">The menu of options for the user to select, built and passed in by the calling application</param>
        /// <param name="text">Any text to put before the menu (optional)</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public virtual async Task ReplyWithOptions(ReplyMenu menu, string? text=null)
        {
            Menu = menu;
            await Source.User.Messenger.ReplyWithOptions(this, text);
            IsProcessing = false;

        }

        /// <summary>
        /// Indicate that the user has selected the option at a particular index of the menu
        /// </summary>
        /// <param name="index">The index (starting at 1) that the user selected</param>
        public virtual void ChooseMenuOption(int index)
        {
            if (Menu is null) return;
            IsProcessing = true;
            Menu.PassReplyByIndex(this, index);
        }

        /// <summary>
        /// Indicate that the user has selected an option by its text name
        /// </summary>
        /// <param name="text">The name of the option selected</param>
        public virtual void ChooseMenuOption(string text)
        {
            if (Menu is null) return;
            IsProcessing = true;
            Menu.PassReply(this, text);
        }

        /// <summary>
        /// Conclude this interaction. Any further messages from the user will start a new interaction
        /// TODO: Make interactions implement <see cref="IDisposable"/>
        /// </summary>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        public virtual async Task End()
        {
            IsProcessing = false;
            await Source.User.Messenger.RemoveInteraction(this);
        }
    }
}
