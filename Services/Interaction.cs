namespace Botifex.Services
{
    public abstract class Interaction
    {
        public Guid Id { get; private set; }
        internal InteractionSource Source { get; set; }
        public virtual Dictionary<string, string> Responses { get; set; }
        internal event EventHandler<EventArgs>? OnMoreInfo;

        internal Interaction(InteractionSource source)
        {
            Id = new Guid();
            Source = source;
            Responses = new Dictionary<string, string>();
        }

        public virtual async Task Reply(string text)
        {
            await Source.Messenger.Reply(this, text);
        }

        internal abstract void End();
    }
}
