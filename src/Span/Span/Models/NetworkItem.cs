namespace Span.Models
{
    public enum NetworkItemType
    {
        Server,
        Share
    }

    public class NetworkItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public NetworkItemType Type { get; set; }
        public string IconGlyph { get; set; } = "\uEDD4"; // RemixIcon: ri-global-line (server default)
        public string Comment { get; set; } = string.Empty;
    }
}
