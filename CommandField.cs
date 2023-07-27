namespace Botifex
{
    /// <summary>
    /// A field within a command
    /// </summary>
    public struct CommandField
    {
        public string Name;
        public string Description;
        public bool Required; // if this field is optional
    }
}