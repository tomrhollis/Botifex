namespace Botifex.Services
{
    public interface ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }
        public Dictionary<string, string> Responses { get; }

        public Task Reply(string text);
    }
}
