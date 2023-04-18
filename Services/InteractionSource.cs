namespace Botifex.Services
{
    internal class InteractionSource
    {
        internal IMessengerUser User { get; set; }
        internal object? Message {  get; set; }
        internal Messenger Messenger { get; set; }

        internal InteractionSource (IMessengerUser user, Messenger messenger, object? message = null)
        {
            User = user;
            Message = message;
            Messenger = messenger;
        }
    }
}