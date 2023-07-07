namespace Botifex.Services
{
    internal interface IInteractionFactory
    {
        internal Interaction? CreateInteraction(InteractionSource source);
    }
}
