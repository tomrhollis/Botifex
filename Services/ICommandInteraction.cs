namespace Botifex.Services
{
    public interface ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }
        public Dictionary<string, string> CommandFields { get; }

        public Task Reply(string text, Dictionary<string, string>? options = null);
    }
}
