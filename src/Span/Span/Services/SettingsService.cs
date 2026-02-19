using Windows.Storage;

namespace Span.Services;

public class SettingsService
{
    private readonly ApplicationDataContainer _localSettings;

    public event Action<string, object?>? SettingChanged;

    public SettingsService()
    {
        _localSettings = ApplicationData.Current.LocalSettings;
    }

    // ── Generic Get/Set ──

    public T Get<T>(string key, T defaultValue)
    {
        if (_localSettings.Values.TryGetValue(key, out var value) && value is T typed)
            return typed;
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        var old = _localSettings.Values.ContainsKey(key) ? _localSettings.Values[key] : null;
        _localSettings.Values[key] = value;

        if (!Equals(old, value))
            SettingChanged?.Invoke(key, value);
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
}
