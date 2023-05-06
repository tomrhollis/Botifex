namespace Botifex.Services
{
    public interface ITextInteraction
    {
        public string Text { get; }

        public Task Reply(string text);
        public Task ReplyWithOptions(ReplyMenu menu, string? text);

        public Task End();
    }
}
