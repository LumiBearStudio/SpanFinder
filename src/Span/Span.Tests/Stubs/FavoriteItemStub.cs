// UI-only surface for the real FavoritesService; persisted DTOs are linked unchanged.
namespace Span.Models
{
    public class FavoriteItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#FFFFFF";
        public int Order { get; set; }
        public bool IsFile { get; set; }
        public string? CustomName { get; set; }
        public string? CustomColor { get; set; }
    }
}

namespace Microsoft.UI.Xaml.Media
{
    public class SolidColorBrush
    {
        public Windows.UI.Color Color { get; set; }
    }
}
