namespace Botifex.Services
{
    public interface ITextInteraction
    {
        public string Text { get; }

        public Task Reply(string text);

    }
}
