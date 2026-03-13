using System;
using Windows.Storage;

namespace Span.Services;

/// <summary>
/// 앱 설정 서비스 구현. Windows ApplicationData.LocalSettings를 래핑하여
/// 테마, 뷰 모드, 탭, 개발자 옵션 등 모든 앱 설정을 관리한다.
/// 설정 변경 시 SettingChanged 이벤트를 발행하여 실시간 반영을 지원.
/// </summary>
public class SettingsService : ISettingsService
{
    private readonly ApplicationDataContainer _localSettings;

    public event Action<string, object?>? SettingChanged;

    public SettingsService()
    {
        try
        {
            _localSettings = ApplicationData.Current.LocalSettings;

            // Probe read to detect corrupted container early
            _ = _localSettings.Values.Count;
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[SettingsService] LocalSettings corrupted, clearing: {ex.Message}");
            try
            {
                _localSettings = ApplicationData.Current.LocalSettings;
                _localSettings.Values.Clear();
            }
            catch
            {
                // Last resort — settings will be empty but app won't crash
            }
        }
    }

    // ── Generic Get/Set ──

    public T Get<T>(string key, T defaultValue)
    {
        try
        {
            if (_localSettings.Values.TryGetValue(key, out var value) && value is T typed)
                return typed;
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[SettingsService] Error reading '{key}': {ex.Message}");
            // Remove corrupted key
            try { _localSettings.Values.Remove(key); } catch { }
        }
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        try
        {
            var old = _localSettings.Values.ContainsKey(key) ? _localSettings.Values[key] : null;
            _localSettings.Values[key] = value;

            if (!Equals(old, value))
                SettingChanged?.Invoke(key, value);
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[SettingsService] Error writing '{key}': {ex.Message}");
        }
    }

    // ── Appearance ──

    public string Theme
    {
        get => Get("Theme", "system");
        set => Set("Theme", value);
    }

    public string Density
    {
        get => Get("Density", "comfortable");
        set => Set("Density", value);
    }

    public string FontFamily
    {
        get => Get("FontFamily", "Segoe UI Variable");
        set => Set("FontFamily", value);
    }

    public string IconPack
    {
        get => Get("IconPack", "remix");   // "remix" | "phosphor" | "tabler"
        set => Set("IconPack", value);
    }

    public string IconFontScale
    {
        get => Get("IconFontScale", "0");  // "0"~"5" (0=기본, 각 단계 +1px)
        set => Set("IconFontScale", value);
    }

    // ── Browsing ──

    public bool ShowHiddenFiles
    {
        get => Get("ShowHiddenFiles", false);
        set => Set("ShowHiddenFiles", value);
    }

    public bool ShowFileExtensions
    {
        get => Get("ShowFileExtensions", true);
        set => Set("ShowFileExtensions", value);
    }

    public bool ShowCheckboxes
    {
        get => Get("ShowCheckboxes", false);
        set => Set("ShowCheckboxes", value);
    }

    public string MillerClickBehavior
    {
        get => Get("MillerClickBehavior", "single");
        set => Set("MillerClickBehavior", value);
    }

    public bool ShowThumbnails
    {
        get => Get("ShowThumbnails", true);
        set => Set("ShowThumbnails", value);
    }

    public bool EnableQuickLook
    {
        get => Get("EnableQuickLook", true);
        set => Set("EnableQuickLook", value);
    }

    public bool ConfirmDelete
    {
        get => Get("ConfirmDelete", true);
        set => Set("ConfirmDelete", value);
    }

    public bool MillerInlinePreviewEnabled
    {
        get => Get("MillerInlinePreviewEnabled", true);
        set => Set("MillerInlinePreviewEnabled", value);
    }

    public int UndoHistorySize
    {
        get => Get("UndoHistorySize", 50);
        set => Set("UndoHistorySize", value);
    }

    // ── Tools ──

    public string DefaultTerminal
    {
        get => Get("DefaultTerminal", "wt");
        set => Set("DefaultTerminal", value);
    }

    public bool ShowContextMenu
    {
        get => Get("ShowContextMenu", true);
        set => Set("ShowContextMenu", value);
    }

    public bool MinimizeToTray
    {
        get => Get("MinimizeToTray", false);
        set => Set("MinimizeToTray", value);
    }

    public bool RememberWindowPosition
    {
        get => Get("RememberWindowPosition", true);
        set => Set("RememberWindowPosition", value);
    }

    public bool ShowFavoritesTree
    {
        get => Get("ShowFavoritesTree", false);
        set => Set("ShowFavoritesTree", value);
    }

    public bool ShowDeveloperMenu
    {
        get => Get("ShowDeveloperMenu", false);
        set => Set("ShowDeveloperMenu", value);
    }

    public bool ShowGitIntegration
    {
        get => Get("ShowGitIntegration", true);
        set => Set("ShowGitIntegration", value);
    }

    public bool ShowHexPreview
    {
        get => Get("ShowHexPreview", false);
        set => Set("ShowHexPreview", value);
    }

    public bool EnableCrashReporting
    {
        get => Get("EnableCrashReporting", true);   // 기본값 ON
        set => Set("EnableCrashReporting", value);
    }

    public bool ShowShellExtensions
    {
        get => Get("ShowShellExtensions", false);
        set => Set("ShowShellExtensions", value);
    }

    public bool ShowWindowsShellExtras
    {
        get => Get("ShowWindowsShellExtras", true);
        set => Set("ShowWindowsShellExtras", value);
    }

    public bool ShowCopilotMenu
    {
        get => Get("ShowCopilotMenu", false);
        set => Set("ShowCopilotMenu", value);
    }

    // ── General ──

    public int StartupBehavior
    {
        get => Get("StartupBehavior", 0);
        set => Set("StartupBehavior", value);
    }

    // ── Per-tab startup settings ──

    public int Tab1StartupBehavior
    {
        get => Get("Tab1StartupBehavior", 0);  // 0=Home, 1=RestoreSession, 2=CustomPath
        set => Set("Tab1StartupBehavior", value);
    }

    public int Tab2StartupBehavior
    {
        get => Get("Tab2StartupBehavior", 0);
        set => Set("Tab2StartupBehavior", value);
    }

    public string Tab1StartupPath
    {
        get => Get("Tab1StartupPath", "");
        set => Set("Tab1StartupPath", value);
    }

    public string Tab2StartupPath
    {
        get => Get("Tab2StartupPath", "");
        set => Set("Tab2StartupPath", value);
    }

    public int Tab1StartupViewMode
    {
        get => Get("Tab1StartupViewMode", 0);  // ViewMode enum int
        set => Set("Tab1StartupViewMode", value);
    }

    public int Tab2StartupViewMode
    {
        get => Get("Tab2StartupViewMode", 0);
        set => Set("Tab2StartupViewMode", value);
    }

    public bool DefaultPreviewEnabled
    {
        get => Get("DefaultPreviewEnabled", true);
        set => Set("DefaultPreviewEnabled", value);
    }

    public string LastSessionPath
    {
        get => Get("LastSessionPath", "");
        set => Set("LastSessionPath", value);
    }

    public string LastSessionViewMode
    {
        get => Get("LastSessionViewMode", "");
        set => Set("LastSessionViewMode", value);
    }

    public string Language
    {
        get => Get("Language", "system");
        set => Set("Language", value);
    }

    // ── Tabs ──

    public string TabsJson
    {
        get => Get("TabsJson", "");
        set => Set("TabsJson", value);
    }

    public int ActiveTabIndex
    {
        get => Get("ActiveTabIndex", 0);
        set => Set("ActiveTabIndex", value);
    }

    // ── List View Settings ──

    public bool ListShowSize
    {
        get => Get("ListShowSize", true);
        set => Set("ListShowSize", value);
    }

    public bool ListShowDate
    {
        get => Get("ListShowDate", false);
        set => Set("ListShowDate", value);
    }

    public int ListColumnWidth
    {
        get => Get("ListColumnWidth", 250);
        set => Set("ListColumnWidth", value);
    }
}
