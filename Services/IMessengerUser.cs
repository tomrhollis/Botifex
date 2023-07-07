namespace Botifex.Services
{
    public interface IMessengerUser
    {
        string Name { get; }

        string At { get; }

        string Id { get; }

        Messenger Messenger { get; }
    }
}