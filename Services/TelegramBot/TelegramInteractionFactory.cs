using System.Text.RegularExpressions;
using Telegram.Bot.Types;

namespace Botifex.Services.TelegramBot
{
    /// <summary>
    /// Figure out if this is a command interaction or a text interaction and create the proper kind of Interaction object
    /// </summary>
    internal class TelegramInteractionFactory : IInteractionFactory
    {
        private ICommandLibrary commandLibrary;

        internal TelegramInteractionFactory(ICommandLibrary commandLibrary) => this.commandLibrary = commandLibrary;

        public Interaction? CreateInteraction(InteractionSource source)
        {
            if (source.User.Messenger is not TelegramService || source.Message is not Message) throw new ArgumentException();

            string text = (((Message?)source.Message)?.Text ?? "").Trim();
            if (String.IsNullOrEmpty(text)) return null; // nothing to work with

            // see if it's a command
            Match command = Regex.Match(text, "^\\/([^@\\s]*)");
            string commandName = (command.Groups.Count > 1) ? command.Groups[1].Value : "";
                        
            if (!String.IsNullOrEmpty(commandName))
            {
                // see if it's a command with a bot name that isn't this one
                Match bot = Regex.Match(text, $"\\/{commandName}@([\\S]*)");
                string botName = bot.Groups.Count > 1 ? bot.Groups[1].Value : "";
                string selfName = ((TelegramService)source.User.Messenger).BotUsername;

                // if it's not this one, abort
                if (!String.IsNullOrEmpty(botName) && botName.ToLower() != selfName.ToLower()) return null;

                //finally meets all requirements to be a command, so make sure it's one of the ones actually defined and it's not coming from a group chat and go
                if (commandLibrary.HasCommand(commandName.ToLower()))
                    return new TelegramCommandInteraction(source, commandLibrary.GetCommand(commandName.ToLower()));   
                
                // don't throw exception on unknown command like in the discord interaction factory, because in telegram users can just type whatever slash commands
                // invalid commands will fall out of the if statement here and we'll treat it as text
            }

            // otherwise it's text
            return new TelegramTextInteraction(source);
        }
    }
}
