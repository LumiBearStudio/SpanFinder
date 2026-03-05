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
/// partial class: LocalizationData.cs의 Entries 튜플 배열과 결합하여 영문 fallback 제공.
/// </summary>
public partial class LocalizationService
{
    // Lazy 초기화: Entries(LocalizationData.cs)의 static 초기화 순서 문제 방지
    private static readonly Lazy<Dictionary<string, string>> _en = new(() =>
    {
        var dict = new Dictionary<string, string>();
        foreach (var e in Entries)
            dict[e.key] = e.en;
        return dict;
    });

    public string? Get(string key) => _en.Value.TryGetValue(key, out var val) ? val : null;

    /// <summary>
    /// Static L() — LocalizationData.cs의 영문 사전에서 조회, 없으면 key 반환.
    /// FileOperationHelpers 등에서 using static으로 참조됨.
    /// </summary>
    public static string L(string key) => _en.Value.TryGetValue(key, out var val) ? val : key;
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
