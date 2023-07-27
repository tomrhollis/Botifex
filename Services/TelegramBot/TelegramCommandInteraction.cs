using System.Text.RegularExpressions;
using Telegram.Bot.Types;

namespace Botifex.Services.TelegramBot
{
    /// <summary>
    /// An interaction for a command in Telegram, which requires a lot of checking compared to Discord
    /// </summary>
    internal class TelegramCommandInteraction : TelegramInteraction, ICommandInteraction
    {
        private Message? initialCommand;
        private string waitingField { get; set; } = string.Empty;

        public SlashCommand BotifexCommand { get; set; }        
        public bool IsReady { get; private set; } = false;
        

        internal TelegramCommandInteraction(InteractionSource source, SlashCommand command) : base(source)
        {
            initialCommand = (Message?)source.Message;
            BotifexCommand = command;

            // disallow non-admin commands in group chats
            // TODO: allow them now that keyboards are set to only display to their triggering users
            if(initialCommand?.Chat.Type != Telegram.Bot.Types.Enums.ChatType.Private && !command.AdminOnly)
            {
                source.User.Messenger.Reply(this, "That command is not allowed in group chats, try DMs!").Wait();
                End().Wait();
            } 
            else
            {
                // currently only the required fields are supported for Telegram commands
                List<CommandField> requiredOptions = BotifexCommand.Options.FindAll(o => o.Required);

                string commandText = initialCommand?.Text?.Trim() ?? string.Empty;

                // if it's a command with only one required field, see if they provided it after the command
                if (BotifexCommand.Options.Count != 0 && requiredOptions.Count == 1)
                {
                    Match findData = Regex.Match(commandText, "([\\s])(.*)");

                    // if it's there, use it as the data for the first field
                    if (findData is not null && findData.Groups?.Count > 1)
                        CommandFields.Add(BotifexCommand.Options[0].Name, findData.Groups[2].Value);
                }

                IsReady = CheckReady(requiredOptions);
            }                            
        }

        /// <summary>
        /// Make sure all required fields have user input attached
        /// </summary>
        /// <param name="requiredOptions">A <see cref="List"/> of all the required <see cref="CommandField"/>s</param>
        /// <returns>Whether the fields are all ready</returns>
        private bool CheckReady(List<CommandField> requiredOptions)
        {
            bool tentativeReady = true;
            foreach (CommandField option in requiredOptions)
            {
                if (!CommandFields.ContainsKey(option.Name)) // no user input found
                {
                    tentativeReady = false;
                    waitingField = option.Name;
                    FollowUp($"What is {option.Description}?").Wait();
                    break;
                }
            }
            return tentativeReady;
        }

        /// <summary>
        /// Send the user a follow up question to fill in a required field
        /// </summary>
        /// <param name="text">The question to ask</param>
        /// <returns><see cref="Task.CompletedTask"/></returns>
        internal async Task FollowUp(string text)
        {
            await ((TelegramService)Source.User.Messenger).Reply(this, text);
            IsProcessing = false;
        }

        /// <summary>
        /// Check incoming text from the user to add it to the command field data
        /// </summary>
        /// <param name="response">The user's input for a field</param>
        internal void ReadResponse(Message response)
        {
            if (String.IsNullOrEmpty(response.Text) || String.IsNullOrEmpty(waitingField) || IsProcessing) // not sure how we got here but we shouldn't be here
                return;
            
            CommandFields.Add(waitingField, response.Text);
            IsReady = CheckReady(BotifexCommand.Options.FindAll(o => o.Required)); // check again if this command is ready
        }
    }
}
