namespace Botifex.Services
{
    public interface ICommandInteraction
    {
        public SlashCommand BotifexCommand { get; set; }
        public Dictionary<string, string> CommandFields { get; }

        public Task Reply(string text);
        public Task ReplyWithOptions(ReplyMenu menu, string? text);
        public void End();
    }
}
