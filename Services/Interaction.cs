using Botifex.Models;

namespace Botifex.Services
{
    public abstract class Interaction
    {
        public Guid Id { get; private set; }
        public InteractionSource Source { get; set; }
        internal BotifexUser? User { get; set; }
        public virtual Dictionary<string, string> CommandFields { get; set; }
        public virtual Dictionary<string, string>? MenuOptions { get; set; }

        internal object? BotMessage;

        internal Interaction(InteractionSource source)
        {
            Id = new Guid();
            Source = source;
            CommandFields = new Dictionary<string, string>();
        }

        public virtual async Task Reply(string text, Dictionary<string, string>? options= null)
        {
            await Source.Messenger.Reply(this, text, options);
        }

        internal abstract void End();
    }
}
