

using System.Text.RegularExpressions;
using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal class TelegramCommandInteraction : TelegramInteraction, ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }        

        private Message? initialCommand;
        internal bool IsReady { get; private set; } = false;

        internal TelegramCommandInteraction(InteractionSource source, SlashCommand command) : base(source)
        {
            initialCommand = (Message?)source.Message;
            BotifexCommand = command;
            List<InteractionOption> requiredOptions = BotifexCommand.Options.FindAll(o => o.Required);

            string commandText = initialCommand?.Text?.Trim() ?? string.Empty;

            // if it's a command with only one required field, see if they provided it after the command
            if(BotifexCommand.Options.Count != 0 && requiredOptions.Count == 1){
                Match findData = Regex.Match(commandText, "([\\s])(.*)");

                // if it's there, use it as the command data
                if (findData is not null && findData.Groups?.Count > 1)
                    Responses.Add(BotifexCommand.Options[0].Name, findData.Groups[2].Value);
            }

            bool tentativeReady = true;
            foreach(InteractionOption option in requiredOptions)
            {
                if (!Responses.ContainsKey(option.Name))
                {
                    tentativeReady = false;
                    break;
                }
            }
            IsReady = tentativeReady;
        }
    }
}
