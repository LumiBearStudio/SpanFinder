using System;

namespace Span.Services
{
    /// <summary>
    /// Appearance-related settings (Theme, Density, Font, Icons).
    /// </summary>
    public interface IAppearanceSettings
    {
        string Theme { get; set; }
        string Density { get; set; }
        string FontFamily { get; set; }
        string IconPack { get; set; }
    }

    /// <summary>
    /// Browsing behavior settings.
    /// </summary>
    public interface IBrowsingSettings
    {
        bool ShowHiddenFiles { get; set; }
        bool ShowFileExtensions { get; set; }
        bool ShowCheckboxes { get; set; }
        string MillerClickBehavior { get; set; }
        bool ShowThumbnails { get; set; }
        bool EnableQuickLook { get; set; }
        bool ConfirmDelete { get; set; }
        int UndoHistorySize { get; set; }
    }

    /// <summary>
    /// Tool and integration settings.
    /// </summary>
    public interface IToolSettings
    {
        string DefaultTerminal { get; set; }
        bool ShowContextMenu { get; set; }
        bool MinimizeToTray { get; set; }
        bool ShowFavoritesTree { get; set; }
        bool ShowDeveloperMenu { get; set; }
        bool ShowWindowsShellExtras { get; set; }
        bool ShowCopilotMenu { get; set; }
    }

    /// <summary>
    /// Full settings service combining all domain interfaces.
    /// </summary>
    public interface ISettingsService : IAppearanceSettings, IBrowsingSettings, IToolSettings
    {
        event Action<string, object?>? SettingChanged;

        T Get<T>(string key, T defaultValue);
        void Set<T>(string key, T value);

        int StartupBehavior { get; set; }
        string LastSessionPath { get; set; }
        string LastSessionViewMode { get; set; }
        string Language { get; set; }
        string TabsJson { get; set; }
        int ActiveTabIndex { get; set; }
        bool ListShowSize { get; set; }
        bool ListShowDate { get; set; }
        int ListColumnWidth { get; set; }
    }
}
