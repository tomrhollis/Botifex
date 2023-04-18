namespace Botifex
{
    public interface ICommandLibrary
    {
        List<SlashCommand> Commands { get; }

        SlashCommand GetCommand(string name);
        bool HasCommand(string name);
        void RegisterCommand(SlashCommand command);
    }
}