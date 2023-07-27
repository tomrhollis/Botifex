using System.Text.RegularExpressions;

namespace Botifex
{
    /// <summary>
    /// Represents a bot command that will be registered with all supported messengers
    /// </summary>
    public class SlashCommand
    {
        private string name = String.Empty;
        /// <summary>
        /// The name of the command. Max length 32, only digits, numbers and underscore
        /// </summary>
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

        private string description = String.Empty;
        /// <summary>
        /// The description of the command to help users. Max length 200
        /// </summary>
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

        /// <summary>
        /// Whether only admins should be able to use this command
        /// </summary>
        public bool AdminOnly {get; private set;}

        /// <summary>
        /// The parameters that can be used with this command, as a <see cref="List"/> of <see cref="CommandField"/>s
        /// </summary>
        public List<CommandField> Options { get; set; } = new List<CommandField>();

        public SlashCommand(bool adminOnly=false)
        {
            AdminOnly = adminOnly;
        }
    }
}
