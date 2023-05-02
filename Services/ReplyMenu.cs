namespace Botifex.Services
{
    public class ReplyMenu
    {
        public string Name { get; set; }
        public Dictionary<string, string> Options { get; set; }
        public string MenuText { get => this.ToString(); }

        internal event EventHandler<MenuReplyReceivedEventArgs> onReply;

        public ReplyMenu(string name, Dictionary<string, string> options, EventHandler<MenuReplyReceivedEventArgs> handler) 
        {
            this.Name = name;
            this.Options = options;
            onReply = handler;
        }

        internal void PassReply(Interaction i, string reply)
        {
            MenuReplyReceivedEventArgs eventArgs = new MenuReplyReceivedEventArgs(i, reply);
            EventHandler<MenuReplyReceivedEventArgs>? handler = onReply;
            if (handler is not null)
            {
                handler(this, eventArgs);
            }
        }

        internal void PassReplyByIndex(Interaction i, int index)
        {
            string reply = Options.Keys.ToArray()[index - 1];
            PassReply(i, reply);
        }

        public override string ToString()
        {
            string text = "";
            for (int i = 0; i < Options.Count; i++)
                text += $"{i + 1}: {Options[Options.Keys.ToArray()[i]]}\n";

            return text.Trim();
        }
    }

    public class MenuReplyReceivedEventArgs
    {
        public string Reply { get; set; }
        public Interaction Interaction { get; set; }
        public MenuReplyReceivedEventArgs(Interaction interaction, string r)
        {
            Reply = r;
            Interaction = interaction;
        }
    }
}
