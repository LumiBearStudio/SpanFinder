using System;
using System.Collections.Generic;
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

    /// <summary>
    /// v1.5.2 (Discussion #30): LocalSettings corrupt 감지 후 Values.Clear()로 wipe 할 때
    /// 손실되면 안 되는 핵심 사용자 키 화이트리스트. Clear 직전 백업 → 직후 복원.
    /// 이전: corrupt → Wipe → OnboardingCompleted=false → 매 실행마다 온보딩 재표시.
    /// </summary>
    private static readonly string[] _criticalKeysToPreserve = new[]
    {
        "OnboardingCompleted",
        "OnboardingDisabled",
        "Theme",
        "Language",
    };

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
            Helpers.DebugLogger.Log($"[SettingsService] LocalSettings corrupted, attempting selective recovery: {ex.Message}");
            try
            {
                _localSettings = ApplicationData.Current.LocalSettings;

                // v1.5.2 (Discussion #30): 핵심 키 백업 → Clear → 복원.
                // 이전 동작은 Wipe 후 모든 사용자 설정(온보딩 완료 플래그 포함)을 잃어
                // 다음 실행에서 OnboardingCompleted=false → 온보딩 무한 재표시 유발.
                var preserved = new Dictionary<string, object?>();
                foreach (var key in _criticalKeysToPreserve)
                {
                    try
                    {
                        if (_localSettings.Values.TryGetValue(key, out var v) && v != null)
                            preserved[key] = v;
                    }
                    catch { /* 키 read 실패 — 해당 키만 건너뜀 */ }
                }

                _localSettings.Values.Clear();

                foreach (var kvp in preserved)
                {
                    try { _localSettings.Values[kvp.Key] = kvp.Value; } catch { }
                }
                Helpers.DebugLogger.Log($"[SettingsService] Restored {preserved.Count}/{_criticalKeysToPreserve.Length} critical keys after wipe");
            }
            catch (Exception innerEx)
            {
                // Last resort — settings will be empty but app won't crash
                Helpers.DebugLogger.Log($"[SettingsService] Selective recovery failed: {innerEx.Message}");
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

    public bool UseCustomAccent
    {
        get => Get("UseCustomAccent", false);
        set => Set("UseCustomAccent", value);
    }

    // "#RRGGBB" format. Empty = not set (falls back to theme accent).
    public string CustomAccentColor
    {
        get => Get("CustomAccentColor", "");
        set => Set("CustomAccentColor", value);
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

    /// <summary>
    /// 폴더 커스텀 아이콘(desktop.ini) 표시 여부. 기본 OFF.
    /// ON 시 Read-Only 속성을 가진 폴더에 한해 Windows Shell API로 커스텀 아이콘을 추출하여 글리프 대신 표시.
    /// 아이콘 로드 실패 시 자동으로 기본 글리프 폴백.
    /// </summary>
    public bool FolderCustomIconsEnabled
    {
        get => Get("FolderCustomIconsEnabled", false);
        set => Set("FolderCustomIconsEnabled", value);
    }

    /// <summary>
    /// 심미적 애니메이션(새 Miller 컬럼 슬라이드-인, on-path 인디케이터 이동) 사용 여부.
    /// 기본 ON. OFF 시 최종 상태로 즉시 전환되어 빠른 탐색 시 snappy 동작 (macOS Finder 유사).
    /// </summary>
    public bool AnimationsEnabled
    {
        get => Get("AnimationsEnabled", true);
        set => Set("AnimationsEnabled", value);
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

    /// <summary>
    /// 썸네일 격리 워커 사용 여부 (v1.3.10부터 기본 ON).
    /// Microsoft.ui.xaml.dll STATUS_STOWED_EXCEPTION (이슈 #23) 우회.
    /// true면 Span.Thumbs.exe 워커 프로세스 경유로 썸네일 생성.
    /// 워커 spawn 실패 시 자동으로 인프로세스 폴백.
    /// </summary>
    public bool UseIsolatedThumbnails
    {
        get => Get("UseIsolatedThumbnails", true);
        set => Set("UseIsolatedThumbnails", value);
    }

    public bool EnableQuickLook
    {
        get => Get("EnableQuickLook", true);
        set => Set("EnableQuickLook", value);
    }

    public bool EnableWasdNavigation
    {
        get => Get("EnableWasdNavigation", false);
        set => Set("EnableWasdNavigation", value);
    }

    public bool ConfirmDelete
    {
        get => Get("ConfirmDelete", true);
        set => Set("ConfirmDelete", value);
    }

    /// <summary>
    /// 미리보기 패널에서 폴더 정보(아이콘, 항목 수 등)를 표시할지 여부.
    /// false(기본값): 파일만 미리보기 표시, 폴더 선택 시 미리보기 비움.
    /// true: 폴더 선택 시에도 폴더 정보를 미리보기에 표시.
    /// </summary>
    public bool PreviewShowFolderInfo
    {
        get => Get("PreviewShowFolderInfo", false);
        set => Set("PreviewShowFolderInfo", value);
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

    // ── Sidebar section visibility ──
    public bool SidebarShowHome { get => Get("SidebarShowHome", true); set => Set("SidebarShowHome", value); }
    public bool SidebarShowFavorites { get => Get("SidebarShowFavorites", true); set => Set("SidebarShowFavorites", value); }
    public bool SidebarShowLocalDrives { get => Get("SidebarShowLocalDrives", true); set => Set("SidebarShowLocalDrives", value); }
    public bool SidebarShowCloud { get => Get("SidebarShowCloud", true); set => Set("SidebarShowCloud", value); }
    public bool SidebarShowNetwork { get => Get("SidebarShowNetwork", true); set => Set("SidebarShowNetwork", value); }
    public bool SidebarShowRecycleBin { get => Get("SidebarShowRecycleBin", true); set => Set("SidebarShowRecycleBin", value); }

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

    public bool ShowFileHash
    {
        get => Get("ShowFileHash", false);
        set => Set("ShowFileHash", value);
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

    public bool ShelfSaveEnabled
    {
        get => Get("ShelfSaveEnabled", true);
        set => Set("ShelfSaveEnabled", value);
    }

    public bool ShelfEnabled
    {
        get => Get("ShelfEnabled", true);
        set => Set("ShelfEnabled", value);
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

    // ── Store Rating ──

    public int AppLaunchCount
    {
        get => Get("AppLaunchCount", 0);
        set => Set("AppLaunchCount", value);
    }

    public bool RatingCompleted
    {
        get => Get("RatingCompleted", false);
        set => Set("RatingCompleted", value);
    }

    // ── Onboarding ──

    public bool OnboardingCompleted
    {
        get => Get("OnboardingCompleted", false);
        set => Set("OnboardingCompleted", value);
    }

    /// <summary>
    /// v1.5.2 (Discussion #30): 사용자가 온보딩을 영구 비활성화.
    /// true면 첫 실행에도 OnboardingWindow가 자동으로 열리지 않음.
    /// "온보딩 다시 보기" 버튼은 이 토글과 무관하게 동작 (수동 호출).
    /// </summary>
    public bool OnboardingDisabled
    {
        get => Get("OnboardingDisabled", false);
        set => Set("OnboardingDisabled", value);
    }

    // ── Command Palette ──

    /// <summary>
    /// Command Palette에서 최근 실행한 커맨드 ID 목록 ("|" 구분, 최대 8개).
    /// </summary>
    public string RecentCommandIds
    {
        get => Get("RecentCommandIds", "");
        set => Set("RecentCommandIds", value);
    }
}
