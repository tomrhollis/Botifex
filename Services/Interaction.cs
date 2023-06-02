using Botifex.Models;

namespace Botifex.Services
{
    public abstract class Interaction
    {
        public Guid Id { get; private set; }
        public InteractionSource Source { get; set; }
        public BotifexUser? User { get; set; }
        public ReplyMenu? Menu { get; set; }
        public bool IsProcessing { get; protected private set; }

        public virtual Dictionary<string, string> CommandFields { get; set; }

        internal object? BotMessage;

        internal Interaction(InteractionSource source)
        {
            Id = new Guid();
            Source = source;
            CommandFields = new Dictionary<string, string>();
            IsProcessing = true;
        }

        public virtual async Task Reply(string text)
        {
            await Source.User.Messenger.Reply(this, text);
            IsProcessing = false;
        }

        public virtual async Task ReplyWithOptions(ReplyMenu menu, string? text=null)
        {
            Menu = menu;
            await Source.User.Messenger.ReplyWithOptions(this, text);
            IsProcessing = false;

        }

        public virtual void ChooseMenuOption(int index)
        {
            if (Menu is null) return;
            IsProcessing = true;
            Menu.PassReplyByIndex(this, index);
        }

        public virtual void ChooseMenuOption(string text)
        {
            if (Menu is null) return;
            IsProcessing = true;
            Menu.PassReply(this, text);
        }

        public virtual async Task End()
        {
            IsProcessing = false;
            await Source.User.Messenger.RemoveInteraction(this);
        }
    }
}
