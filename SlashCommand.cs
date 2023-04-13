using System.Text.RegularExpressions;

namespace Botifex
{
    public class SlashCommand
    {
        private string name;
        // max length 32, only digits numbers and underscore
        public string Name 
        { 
            get => name; 
            set {
                if ((value.Length > 32) || (Regex.Match(value, "[^0-9a-zA-Z_]").Length != 0))
                {
                    throw new ArgumentException($"Slash command name {value} does not meet requirements of all platforms");
                }
                else name = value;
            }
        }

        private string description;
        // max length 200
        public string Description
        { 
            get => description; 
            set {
                if (value.Length > 200)
                {
                    throw new ArgumentException("A slash command description is not short enough for all platforms (Max: 200)");
                }
                else description = value;
            }
        }

        public SlashCommand()
        {
        }

        public SlashCommand(SlashCommand original)
        {
            Name = original.Name;
            Description = original.Description;
        }
    }
}
