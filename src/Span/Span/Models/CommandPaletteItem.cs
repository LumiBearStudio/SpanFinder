namespace Span.Models
{
    public enum CommandPaletteItemType
    {
        Command,
        Tab,
        Navigation,
    }

    /// <summary>
    /// Command Palette에 표시되는 개별 항목.
    /// </summary>
    public class CommandPaletteItem
    {
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;
        public string Shortcut { get; set; } = string.Empty;
        public string CommandId { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public int TabIndex { get; set; } = -1;
        public CommandPaletteItemType Type { get; set; }
    }
}
