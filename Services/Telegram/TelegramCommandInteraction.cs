

using System.Text.RegularExpressions;
using Telegram.Bot.Types;

namespace Botifex.Services
{
    internal class TelegramCommandInteraction : TelegramInteraction, ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }        

        private Message? initialCommand;
        public bool IsReady { get; private set; } = false;
        private string WaitingField { get; set; } = string.Empty;

        internal TelegramCommandInteraction(InteractionSource source, SlashCommand command) : base(source)
        {
            initialCommand = (Message?)source.Message;
            BotifexCommand = command;
            List<CommandField> requiredOptions = BotifexCommand.Options.FindAll(o => o.Required);

            string commandText = initialCommand?.Text?.Trim() ?? string.Empty;

            // if it's a command with only one required field, see if they provided it after the command
            if(BotifexCommand.Options.Count != 0 && requiredOptions.Count == 1){
                Match findData = Regex.Match(commandText, "([\\s])(.*)");

                // if it's there, use it as the command data
                if (findData is not null && findData.Groups?.Count > 1)
                    CommandFields.Add(BotifexCommand.Options[0].Name, findData.Groups[2].Value);
            }

            IsReady = CheckReady(requiredOptions);                      
        }

        private bool CheckReady(List<CommandField> requiredOptions)
        {
            bool tentativeReady = true;
            foreach (CommandField option in requiredOptions)
            {
                if (!CommandFields.ContainsKey(option.Name))
                {
                    tentativeReady = false;
                    WaitingField = option.Name;
                    FollowUp($"What is {option.Description}?").Wait();
                    break;
                }
            }
            return tentativeReady;
        }

        internal async Task FollowUp(string text)
        {
            await ((TelegramService)Source.Messenger).Reply(this, text);
        }

        internal void ReadResponse(Message response)
        {
            if (String.IsNullOrEmpty(response.Text) || String.IsNullOrEmpty(WaitingField))
                throw new ArgumentNullException();

            CommandFields.Add(WaitingField, response.Text);
            
            IsReady = CheckReady(BotifexCommand.Options.FindAll(o => o.Required));
        }
    }
}
