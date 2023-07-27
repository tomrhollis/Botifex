namespace Botifex.Services
{
    /// <summary>
    /// A menu of options returned back to a user for them to select from
    /// </summary>
    public class ReplyMenu
    {
        internal event EventHandler<MenuReplyReceivedEventArgs> onReply; // what to do when a reply is received

        /// <summary>
        /// An identifier for the menu to direct processing of the result
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A dictionary of the options for this menu. Key: A unique identifier for the option; Value: The text for the user
        /// </summary>
        public Dictionary<string, string> Options { get; set; }

        /// <summary>
        /// Get a text representation of this menu
        /// </summary>
        public string MenuText { get => this.ToString(); }

        /// <summary>
        /// Whether to use the unique identifier from the dictionary as the menu options (false), or replace them with numbers 1 through n (true)
        /// </summary>
        public bool NumberedChoices { get; set; } = true;

        public ReplyMenu(string name, Dictionary<string, string> options, EventHandler<MenuReplyReceivedEventArgs> handler) 
        {
            Name = name;
            Options = options;
            onReply = handler;
        }

        /// <summary>
        /// Trigger the reply received event and pass it relevant information
        /// </summary>
        /// <param name="i">The <see cref="Interaction"/> associated with this reply</param>
        /// <param name="reply">The reply text which should match an id
        internal void PassReply(Interaction i, string reply)
        {
            MenuReplyReceivedEventArgs eventArgs = new MenuReplyReceivedEventArgs(i, reply);
            EventHandler<MenuReplyReceivedEventArgs>? handler = onReply;
            if (handler is not null)
            {
                handler(this, eventArgs);
            }
        }

        /// <summary>
        /// Translate a numbered index reply to an id before triggering the reply received event
        /// </summary>
        /// <param name="i">The <see cref="Interaction"/> associated with this reply</param>
        /// <param name="index">The 1-indexed position of the chosen option</param>
        internal void PassReplyByIndex(Interaction i, int index)
        {
            string reply = Options.Keys.ToArray()[index - 1];
            PassReply(i, reply);
        }

        /// <summary>
        /// Create a list of options for display to the user
        /// </summary>
        /// <returns>A text list of options</returns>
        public override string ToString()
        {
            string text = "";
            for (int i = 0; i < Options.Count; i++)
                text += $"{(NumberedChoices ? i + 1 : Options.Keys.ToArray()[i])}: {Options[Options.Keys.ToArray()[i]]}\n";

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
