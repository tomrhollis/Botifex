using Microsoft.Extensions.Logging;

namespace Botifex
{
    /// <summary>
    /// Holds all the commands that have been registered for Botifex to set up with its messengers
    /// </summary>
    public class CommandLibrary : ICommandLibrary
    {
        private ILogger<CommandLibrary> log;
        private Dictionary<string, SlashCommand> commands = new Dictionary<string, SlashCommand>();

        public List<SlashCommand> Commands { get => commands.Values.ToList(); } // return all the command objects

        public CommandLibrary(ILogger<CommandLibrary> log) 
        {
            this.log = log;
        }

        public void RegisterCommand(SlashCommand command)
        {
            command.Name = command.Name.ToLower();

            if (commands.ContainsKey(command.Name))
                log.LogWarning($"Attempted to add {command.Name} more than once, ignored");

            else // good to go
            {
#if DEBUG
                log.LogDebug("Command registered: " + command.Name);
#endif
                commands.Add(command.Name, command);
            }
        }

        public SlashCommand GetCommand(string name) => commands[name];

        public bool HasCommand(string name) => commands.ContainsKey(name);
    }
}
