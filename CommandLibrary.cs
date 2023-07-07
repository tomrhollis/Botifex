using Microsoft.Extensions.Logging;

namespace Botifex
{
    public class CommandLibrary : ICommandLibrary
    {
        private Dictionary<string, SlashCommand> commands = new Dictionary<string, SlashCommand>();
        public List<SlashCommand> Commands { get => commands.Values.ToList(); }

        //private IConfiguration config;
        private ILogger<CommandLibrary> log;

        public CommandLibrary(ILogger<CommandLibrary> log/*, IConfiguration cfg*/) 
        {
            //config = cfg;
            this.log = log;

            //LoadCommands();
        }

        public void RegisterCommand(SlashCommand command)
        {
            command.Name = command.Name.ToLower();
            if (commands.ContainsKey(command.Name)) log.LogWarning($"Attempted to add {command.Name} more than once, ignored");
            else
            {
                log.LogDebug("Command registered: " + command.Name);
                commands.Add(command.Name, command);
            }
        }
        /*
        private void LoadCommands()
        {
            try
            {
                SlashCommand[] incomingCommands = config.GetSection("Commands").Get<SlashCommand[]>();

                foreach (SlashCommand command in incomingCommands)
                {
                    RegisterCommand(command);
                }
            }
            catch (ArgumentException e)
            {
                log.LogError($"{e.Message}");
            }
        }*/

        public SlashCommand GetCommand(string name) => commands[name];

        public bool HasCommand(string name) => commands.ContainsKey(name);
    }
}
