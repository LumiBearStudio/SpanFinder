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

/// <summary>
/// Minimal stub for LocalizationService — satisfies compile-time references in ViewModeExtensions.
/// Returns null so the Loc() fallback is always used.
/// </summary>
public class LocalizationService
{
    public string? Get(string key) => null;
}

/// <summary>
/// Minimal stub for App — satisfies compile-time references in ViewModeExtensions.
/// The Loc() method catches all exceptions, so this stub just needs to exist.
/// </summary>
public static class App
{
    public static AppStub Current { get; } = new();

    public class AppStub
    {
        public IServiceProvider Services { get; } = new MinimalServiceProvider();
    }

    private class MinimalServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
