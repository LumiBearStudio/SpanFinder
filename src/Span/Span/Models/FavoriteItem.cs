using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    public class FavoriteItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;
        public string IconColor { get; set; } = "#FFFFFF";
        public int Order { get; set; }

        public SolidColorBrush IconBrush
        {
            get
            {
                try
                {
                    var hex = IconColor.TrimStart('#');
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
                }
                catch
                {
                    return new SolidColorBrush(Colors.White);
                }
            }
        }
    }
}
