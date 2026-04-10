using System;
using System.Collections.Generic;

namespace Span.Services;

/// <summary>
/// In-memory ISettingsService stub for unit tests.
/// 모든 도메인 인터페이스(IAppearanceSettings/IBrowsingSettings/IToolSettings/IDeveloperSettings)를
/// 최소한의 기본값으로 구현하고, Get/Set은 Dictionary로 대응한다.
/// 실제 SettingsService는 Windows ApplicationData를 쓰므로 테스트에서 사용 불가.
/// </summary>
public class SettingsServiceStub : ISettingsService
{
    private readonly Dictionary<string, object?> _store = new(StringComparer.Ordinal);

    public event Action<string, object?>? SettingChanged;

    public T Get<T>(string key, T defaultValue)
    {
        if (_store.TryGetValue(key, out var v) && v is T t) return t;
        return defaultValue;
    }

    public void Set<T>(string key, T value)
    {
        _store[key] = value;
        SettingChanged?.Invoke(key, value);
    }

    // ── IAppearanceSettings ─────────────────────────
    public string Theme { get; set; } = "Auto";
    public string Density { get; set; } = "Normal";
    public string FontFamily { get; set; } = "Segoe UI";
    public string IconPack { get; set; } = "Remix";

    // ── IBrowsingSettings ───────────────────────────
    public bool ShowHiddenFiles { get; set; }
    public bool ShowFileExtensions { get; set; } = true;
    public bool ShowCheckboxes { get; set; }
    public string MillerClickBehavior { get; set; } = "SingleClick";
    public bool ShowThumbnails { get; set; } = true;
    public bool EnableQuickLook { get; set; } = true;
    public bool EnableWasdNavigation { get; set; }
    public bool ConfirmDelete { get; set; } = true;
    public bool PreviewShowFolderInfo { get; set; } = true;
    public int UndoHistorySize { get; set; } = 50;

    // ── IToolSettings ───────────────────────────────
    public string DefaultTerminal { get; set; } = "wt.exe";
    public bool ShowContextMenu { get; set; } = true;
    public bool MinimizeToTray { get; set; }
    public bool RememberWindowPosition { get; set; } = true;
    public bool ShowFavoritesTree { get; set; } = true;
    public bool ShowWindowsShellExtras { get; set; } = true;
    public bool ShowShellExtensions { get; set; }
    public bool ShowCopilotMenu { get; set; }
    public bool SidebarShowHome { get; set; } = true;
    public bool SidebarShowFavorites { get; set; } = true;
    public bool SidebarShowLocalDrives { get; set; } = true;
    public bool SidebarShowCloud { get; set; } = true;
    public bool SidebarShowNetwork { get; set; } = true;
    public bool SidebarShowRecycleBin { get; set; } = true;

    // ── IDeveloperSettings ──────────────────────────
    public bool ShowDeveloperMenu { get; set; }
    public bool ShowGitIntegration { get; set; }
    public bool ShowHexPreview { get; set; }
    public bool EnableCrashReporting { get; set; }
    public bool ShowFileHash { get; set; }

    // ── ISettingsService ────────────────────────────
    public int StartupBehavior { get; set; }
    public string LastSessionPath { get; set; } = string.Empty;
    public string LastSessionViewMode { get; set; } = "MillerColumns";
    public string Language { get; set; } = "en";
    public string TabsJson { get; set; } = string.Empty;
    public int ActiveTabIndex { get; set; }
    public bool ListShowSize { get; set; } = true;
    public bool ListShowDate { get; set; } = true;
    public int ListColumnWidth { get; set; } = 200;

    public int Tab1StartupBehavior { get; set; }
    public int Tab2StartupBehavior { get; set; }
    public string Tab1StartupPath { get; set; } = string.Empty;
    public string Tab2StartupPath { get; set; } = string.Empty;
    public int Tab1StartupViewMode { get; set; }
    public int Tab2StartupViewMode { get; set; }
    public bool DefaultPreviewEnabled { get; set; } = true;
    public bool ShelfSaveEnabled { get; set; } = true;
    public bool ShelfEnabled { get; set; } = true;

    public string RecentCommandIds { get; set; } = string.Empty;
}
