using Botifex.Models;

namespace Botifex.Services
{
    public abstract class Interaction
    {
        public Guid Id { get; private set; }
        public InteractionSource Source { get; set; }
        public BotifexUser? User { get; set; }
        public ReplyMenu? Menu { get; set; }

        public virtual Dictionary<string, string> CommandFields { get; set; }

        internal object? BotMessage;

        internal Interaction(InteractionSource source)
        {
            Id = new Guid();
            Source = source;
            CommandFields = new Dictionary<string, string>();
        }

        public virtual async Task Reply(string text)
        {
            await Source.Messenger.Reply(this, text);
        }

        public virtual async Task ReplyWithOptions(ReplyMenu menu, string? text=null)
        {
            Menu = menu;
            await Source.Messenger.ReplyWithOptions(this, text);
        }

        public virtual void ChooseMenuOption(int index)
        {
            if (Menu is null) return;

            Menu.PassReplyByIndex(this, index);
        }

        public virtual void ChooseMenuOption(string text)
        {
            if (Menu is null) return;

            Menu.PassReply(this, text);
        }

        public virtual void End()
        {
            Source.Messenger.RemoveInteraction(this);
        }
    }
}
