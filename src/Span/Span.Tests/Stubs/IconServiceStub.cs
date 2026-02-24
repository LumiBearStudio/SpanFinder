namespace Span.Services;

/// <summary>
/// Minimal stub for IconService — satisfies compile-time references in FileItem, FolderItem, DriveItem.
/// Real IconService depends on WinUI (Microsoft.UI.Xaml.Media) and can't be linked into the test project.
/// All properties return defaults; Models use null-conditional + fallback values anyway.
/// </summary>
public class IconService
{
    public static IconService? Current { get; set; }

    public string FileDefaultGlyph { get; } = "\uECE0";
    public string FolderGlyph { get; } = "\uED53";
    public string NetworkGlyph { get; } = "\uEDD4";
    public string ServerGlyph { get; } = "\uEE71";
}
