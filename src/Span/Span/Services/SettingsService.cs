using System;
using Windows.Storage;

namespace Span.Services;

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
        get => Get("ShowGitIntegration", false);
        set => Set("ShowGitIntegration", value);
    }

    public bool ShowHexPreview
    {
        get => Get("ShowHexPreview", false);
        set => Set("ShowHexPreview", value);
    }

    public bool ShowWindowsShellExtras
    {
        get => Get("ShowWindowsShellExtras", false);
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
