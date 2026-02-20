using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// Represents a subfolder node in the sidebar favorites tree.
    /// Used as TreeViewNode.Content for lazily-loaded child folders.
    /// </summary>
    public class SidebarFolderNode
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uEEA7"; // RemixIcon: FolderFill (same as FolderItem)
        public string IconColor { get; set; } = "#FFC857"; // Folder yellow

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
