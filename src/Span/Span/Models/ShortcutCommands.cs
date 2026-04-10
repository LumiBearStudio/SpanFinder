using Span.Services;
using System.Collections.Generic;
using System.Linq;

namespace Span.Models
{
    /// <summary>
    /// 커스텀 키보드 단축키 리매핑을 위한 Command ID 상수 클래스.
    /// 각 상수는 "span.카테고리.액션" 형식의 문자열.
    /// </summary>
    public static class ShortcutCommands
    {
        // ── Navigation ──────────────────────────────────────────
        public const string NavigateBack = "span.navigate.back";
        public const string NavigateForward = "span.navigate.forward";
        public const string NavigateUp = "span.navigate.up";
        public const string AddressBarFocus = "span.navigate.addressBar";
        public const string Search = "span.navigate.search";
        public const string FilterBar = "span.navigate.filterBar";

        // ── Edit ────────────────────────────────────────────────
        public const string Copy = "span.edit.copy";
        public const string Cut = "span.edit.cut";
        public const string Paste = "span.edit.paste";
        public const string PasteAsShortcut = "span.edit.pasteAsShortcut";
        public const string Delete = "span.edit.delete";
        public const string PermanentDelete = "span.edit.permanentDelete";
        public const string Rename = "span.edit.rename";
        public const string Duplicate = "span.edit.duplicate";
        public const string NewFolder = "span.edit.newFolder";
        public const string Undo = "span.edit.undo";
        public const string Redo = "span.edit.redo";

        // ── Selection ───────────────────────────────────────────
        public const string SelectAll = "span.selection.selectAll";
        public const string SelectNone = "span.selection.selectNone";
        public const string InvertSelection = "span.selection.invert";

        // ── View ────────────────────────────────────────────────
        public const string ViewMiller = "span.view.miller";
        public const string ViewDetails = "span.view.details";
        public const string ViewList = "span.view.list";
        public const string ViewIcon = "span.view.icon";
        public const string ToggleSplitView = "span.view.splitView";
        public const string TogglePreview = "span.view.preview";
        public const string EqualizeColumns = "span.view.equalizeColumns";
        public const string AutoFitColumns = "span.view.autoFitColumns";
        public const string Refresh = "span.view.refresh";
        public const string ToggleHidden = "span.view.toggleHidden";
        public const string ToggleExtensions = "span.view.toggleExtensions";
        public const string Fullscreen = "span.view.fullscreen";

        // ── Tab ─────────────────────────────────────────────────
        public const string NewTab = "span.tab.new";
        public const string CloseTab = "span.tab.close";
        public const string NextTab = "span.tab.next";
        public const string PrevTab = "span.tab.prev";
        public const string OpenInNewTab = "span.tab.openSelectedInNew";
        public const string SwitchPane = "span.view.switchPane";

        // ── Window ──────────────────────────────────────────────
        public const string NewWindow = "span.window.new";
        public const string OpenTerminal = "span.window.terminal";
        public const string OpenSettings = "span.window.settings";
        public const string ShowProperties = "span.window.properties";
        public const string ShowHelp = "span.window.help";
        public const string OpenHelp = "span.help.open";
        public const string QuickLook = "span.quickLook.toggle";

        // ── Workspace ──────────────────────────────────────────
        public const string SaveWorkspace = "span.workspace.save";
        public const string OpenWorkspacePalette = "span.workspace.open";

        // ── Shelf ───────────────────────────────────────────────
        public const string ShelfAdd = "span.shelf.add";
        public const string ShelfToggle = "span.shelf.toggle";
        public const string ShelfMoveHere = "span.shelf.moveHere";
        public const string ShelfCopyHere = "span.shelf.copyHere";
        public const string ShelfClear = "span.shelf.clear";

        // ── Command Palette (HIDDEN) ────────────────────────────
        // 2026-04-10: 파일 탐색기 워크플로우와 부합하지 않아 숨김 처리됨.
        // 상수와 카테고리 등록은 모두 유지 (사용자가 키 재할당 시 즉시 동작).
        // 자세한 사유는 MainWindow.CommandPaletteHandler.cs 상단 주석 참조.
        public const string OpenCommandPalette = "span.commandPalette.open";

        // ── Settings: Toggle (즉시 적용 boolean) ────────────────
        public const string SettingsToggleHidden = "span.settings.toggle.hidden";
        public const string SettingsToggleExtensions = "span.settings.toggle.extensions";
        public const string SettingsToggleCheckboxes = "span.settings.toggle.checkboxes";
        public const string SettingsToggleThumbnails = "span.settings.toggle.thumbnails";
        public const string SettingsToggleQuickLook = "span.settings.toggle.quickLook";
        public const string SettingsToggleWasd = "span.settings.toggle.wasd";
        public const string SettingsToggleConfirmDelete = "span.settings.toggle.confirmDelete";
        public const string SettingsTogglePreviewFolderInfo = "span.settings.toggle.previewFolderInfo";
        public const string SettingsToggleDefaultPreview = "span.settings.toggle.defaultPreview";
        public const string SettingsToggleFavoritesTree = "span.settings.toggle.favoritesTree";
        public const string SettingsToggleShelf = "span.settings.toggle.shelf";
        public const string SettingsToggleShelfSave = "span.settings.toggle.shelfSave";
        public const string SettingsToggleContextMenu = "span.settings.toggle.contextMenu";
        public const string SettingsToggleTray = "span.settings.toggle.tray";
        public const string SettingsToggleWindowPosition = "span.settings.toggle.windowPosition";
        public const string SettingsToggleGitIntegration = "span.settings.toggle.git";
        public const string SettingsToggleHexPreview = "span.settings.toggle.hexPreview";
        public const string SettingsToggleFileHash = "span.settings.toggle.fileHash";
        public const string SettingsToggleShellExtensions = "span.settings.toggle.shellExt";
        public const string SettingsToggleWindowsShellExtras = "span.settings.toggle.shellExtras";
        public const string SettingsToggleCopilotMenu = "span.settings.toggle.copilot";

        // Sidebar sections
        public const string SettingsSidebarHome = "span.settings.sidebar.home";
        public const string SettingsSidebarFavorites = "span.settings.sidebar.favorites";
        public const string SettingsSidebarDrives = "span.settings.sidebar.drives";
        public const string SettingsSidebarCloud = "span.settings.sidebar.cloud";
        public const string SettingsSidebarNetwork = "span.settings.sidebar.network";
        public const string SettingsSidebarRecycleBin = "span.settings.sidebar.recycleBin";

        // ── Settings: Select (선택형) ───────────────────────────
        public const string SettingsThemeSystem = "span.settings.theme.system";
        public const string SettingsThemeLight = "span.settings.theme.light";
        public const string SettingsThemeDark = "span.settings.theme.dark";

        public const string SettingsDensityCompact = "span.settings.density.compact";
        public const string SettingsDensityComfortable = "span.settings.density.comfortable";
        public const string SettingsDensitySpacious = "span.settings.density.spacious";

        public const string SettingsLanguageSystem = "span.settings.language.system";
        public const string SettingsLanguageEn = "span.settings.language.en";
        public const string SettingsLanguageKo = "span.settings.language.ko";
        public const string SettingsLanguageJa = "span.settings.language.ja";
        public const string SettingsLanguageZhHans = "span.settings.language.zh-Hans";
        public const string SettingsLanguageZhHant = "span.settings.language.zh-Hant";
        public const string SettingsLanguageDe = "span.settings.language.de";
        public const string SettingsLanguageEs = "span.settings.language.es";
        public const string SettingsLanguageFr = "span.settings.language.fr";
        public const string SettingsLanguagePtBr = "span.settings.language.pt-BR";

        public const string SettingsIconPackRemix = "span.settings.iconPack.remix";
        public const string SettingsIconPackPhosphor = "span.settings.iconPack.phosphor";
        public const string SettingsIconPackTabler = "span.settings.iconPack.tabler";

        // ── Settings: Open Section (점프) ───────────────────────
        public const string SettingsOpenGeneral = "span.settings.open.general";
        public const string SettingsOpenAppearance = "span.settings.open.appearance";
        public const string SettingsOpenBrowsing = "span.settings.open.browsing";
        public const string SettingsOpenSidebar = "span.settings.open.sidebar";
        public const string SettingsOpenTools = "span.settings.open.tools";
        public const string SettingsOpenShortcuts = "span.settings.open.shortcuts";
        public const string SettingsOpenAdvanced = "span.settings.open.advanced";

        // ── 내부 레지스트리 ─────────────────────────────────────

        private static readonly Dictionary<string, string> _categories = new()
        {
            // Navigation
            { NavigateBack, "Navigation" },
            { NavigateForward, "Navigation" },
            { NavigateUp, "Navigation" },
            { AddressBarFocus, "Navigation" },
            { Search, "Navigation" },
            { FilterBar, "Navigation" },
            // Edit
            { Copy, "Edit" },
            { Cut, "Edit" },
            { Paste, "Edit" },
            { PasteAsShortcut, "Edit" },
            { Delete, "Edit" },
            { PermanentDelete, "Edit" },
            { Rename, "Edit" },
            { Duplicate, "Edit" },
            { NewFolder, "Edit" },
            { Undo, "Edit" },
            { Redo, "Edit" },
            // Selection
            { SelectAll, "Selection" },
            { SelectNone, "Selection" },
            { InvertSelection, "Selection" },
            // View
            { ViewMiller, "View" },
            { ViewDetails, "View" },
            { ViewList, "View" },
            { ViewIcon, "View" },
            { ToggleSplitView, "View" },
            { TogglePreview, "View" },
            { EqualizeColumns, "View" },
            { AutoFitColumns, "View" },
            { Refresh, "View" },
            { ToggleHidden, "View" },
            { ToggleExtensions, "View" },
            { Fullscreen, "View" },
            // Tab
            { NewTab, "Tab" },
            { CloseTab, "Tab" },
            { NextTab, "Tab" },
            { PrevTab, "Tab" },
            { OpenInNewTab, "Tab" },
            { SwitchPane, "View" },
            // Window
            { NewWindow, "Window" },
            { OpenTerminal, "Window" },
            { OpenSettings, "Window" },
            { ShowProperties, "Window" },
            { ShowHelp, "Window" },
            { OpenHelp, "Help" },
            // Quick Look
            { QuickLook, "QuickLook" },
            // Workspace
            { SaveWorkspace, "Workspace" },
            { OpenWorkspacePalette, "Workspace" },
            // Shelf
            { ShelfAdd, "Shelf" },
            { ShelfToggle, "Shelf" },
            { ShelfMoveHere, "Shelf" },
            { ShelfCopyHere, "Shelf" },
            { ShelfClear, "Shelf" },
            // Command Palette
            { OpenCommandPalette, "CommandPalette" },
            // Settings: Toggle
            { SettingsToggleHidden, "Settings" },
            { SettingsToggleExtensions, "Settings" },
            { SettingsToggleCheckboxes, "Settings" },
            { SettingsToggleThumbnails, "Settings" },
            { SettingsToggleQuickLook, "Settings" },
            { SettingsToggleWasd, "Settings" },
            { SettingsToggleConfirmDelete, "Settings" },
            { SettingsTogglePreviewFolderInfo, "Settings" },
            { SettingsToggleDefaultPreview, "Settings" },
            { SettingsToggleFavoritesTree, "Settings" },
            { SettingsToggleShelf, "Settings" },
            { SettingsToggleShelfSave, "Settings" },
            { SettingsToggleContextMenu, "Settings" },
            { SettingsToggleTray, "Settings" },
            { SettingsToggleWindowPosition, "Settings" },
            { SettingsToggleGitIntegration, "Settings" },
            { SettingsToggleHexPreview, "Settings" },
            { SettingsToggleFileHash, "Settings" },
            { SettingsToggleShellExtensions, "Settings" },
            { SettingsToggleWindowsShellExtras, "Settings" },
            { SettingsToggleCopilotMenu, "Settings" },
            // Sidebar
            { SettingsSidebarHome, "Sidebar" },
            { SettingsSidebarFavorites, "Sidebar" },
            { SettingsSidebarDrives, "Sidebar" },
            { SettingsSidebarCloud, "Sidebar" },
            { SettingsSidebarNetwork, "Sidebar" },
            { SettingsSidebarRecycleBin, "Sidebar" },
            // Theme
            { SettingsThemeSystem, "Theme" },
            { SettingsThemeLight, "Theme" },
            { SettingsThemeDark, "Theme" },
            // Density
            { SettingsDensityCompact, "Density" },
            { SettingsDensityComfortable, "Density" },
            { SettingsDensitySpacious, "Density" },
            // Language
            { SettingsLanguageSystem, "Language" },
            { SettingsLanguageEn, "Language" },
            { SettingsLanguageKo, "Language" },
            { SettingsLanguageJa, "Language" },
            { SettingsLanguageZhHans, "Language" },
            { SettingsLanguageZhHant, "Language" },
            { SettingsLanguageDe, "Language" },
            { SettingsLanguageEs, "Language" },
            { SettingsLanguageFr, "Language" },
            { SettingsLanguagePtBr, "Language" },
            // Icon Pack
            { SettingsIconPackRemix, "IconPack" },
            { SettingsIconPackPhosphor, "IconPack" },
            { SettingsIconPackTabler, "IconPack" },
            // Settings open sections
            { SettingsOpenGeneral, "SettingsSection" },
            { SettingsOpenAppearance, "SettingsSection" },
            { SettingsOpenBrowsing, "SettingsSection" },
            { SettingsOpenSidebar, "SettingsSection" },
            { SettingsOpenTools, "SettingsSection" },
            { SettingsOpenShortcuts, "SettingsSection" },
            { SettingsOpenAdvanced, "SettingsSection" },
        };

        /// <summary>
        /// 로컬라이즈된 표시 이름을 가져옵니다. LocalizationService에서 키를 찾지 못하면 commandId를 그대로 반환합니다.
        /// 로컬라이즈 키 형식: "Shortcut_span_category_action" (점을 밑줄로 치환)
        /// </summary>
        private static readonly Dictionary<string, string> _displayNameKeys = new()
        {
            // Navigation
            { NavigateBack, "Shortcut_NavigateBack" },
            { NavigateForward, "Shortcut_NavigateForward" },
            { NavigateUp, "Shortcut_NavigateUp" },
            { AddressBarFocus, "Shortcut_AddressBarFocus" },
            { Search, "Shortcut_Search" },
            { FilterBar, "Shortcut_FilterBar" },
            // Edit
            { Copy, "Shortcut_Copy" },
            { Cut, "Shortcut_Cut" },
            { Paste, "Shortcut_Paste" },
            { PasteAsShortcut, "Shortcut_PasteAsShortcut" },
            { Delete, "Shortcut_Delete" },
            { PermanentDelete, "Shortcut_PermanentDelete" },
            { Rename, "Shortcut_Rename" },
            { Duplicate, "Shortcut_Duplicate" },
            { NewFolder, "Shortcut_NewFolder" },
            { Undo, "Shortcut_Undo" },
            { Redo, "Shortcut_Redo" },
            // Selection
            { SelectAll, "Shortcut_SelectAll" },
            { SelectNone, "Shortcut_SelectNone" },
            { InvertSelection, "Shortcut_InvertSelection" },
            // View
            { ViewMiller, "Shortcut_ViewMiller" },
            { ViewDetails, "Shortcut_ViewDetails" },
            { ViewList, "Shortcut_ViewList" },
            { ViewIcon, "Shortcut_ViewIcon" },
            { ToggleSplitView, "Shortcut_ToggleSplitView" },
            { TogglePreview, "Shortcut_TogglePreview" },
            { EqualizeColumns, "Shortcut_EqualizeColumns" },
            { AutoFitColumns, "Shortcut_AutoFitColumns" },
            { Refresh, "Shortcut_Refresh" },
            { ToggleHidden, "Shortcut_ToggleHidden" },
            { ToggleExtensions, "Shortcut_ToggleExtensions" },
            { Fullscreen, "Shortcut_Fullscreen" },
            // Window
            { NewTab, "Shortcut_NewTab" },
            { CloseTab, "Shortcut_CloseTab" },
            { NextTab, "Shortcut_NextTab" },
            { PrevTab, "Shortcut_PrevTab" },
            { SwitchPane, "Shortcut_SwitchPane" },
            { NewWindow, "Shortcut_NewWindow" },
            { OpenTerminal, "Shortcut_OpenTerminal" },
            { OpenSettings, "Shortcut_OpenSettings" },
            { ShowProperties, "Shortcut_ShowProperties" },
            { ShowHelp, "Shortcut_ShowHelp" },
            { OpenHelp, "Shortcut_OpenHelp" },
            { OpenInNewTab, "Shortcut_OpenInNewTab" },
            { QuickLook, "Shortcut_QuickLook" },
            // Workspace
            { SaveWorkspace, "Shortcut_SaveWorkspace" },
            { OpenWorkspacePalette, "Shortcut_OpenWorkspacePalette" },
            // Shelf
            { ShelfAdd, "Shortcut_ShelfAdd" },
            { ShelfToggle, "Shortcut_ShelfToggle" },
            { ShelfMoveHere, "Shortcut_ShelfMoveHere" },
            { ShelfCopyHere, "Shortcut_ShelfCopyHere" },
            { ShelfClear, "Shortcut_ShelfClear" },
            // Command Palette
            { OpenCommandPalette, "Shortcut_OpenCommandPalette" },
        };

        /// <summary>
        /// 리매핑 불가능한 시스템 커맨드 목록.
        /// 이 커맨드들은 OS 또는 WinUI 프레임워크 수준에서 처리되므로 사용자가 변경할 수 없습니다.
        /// </summary>
        private static readonly HashSet<string> _nonRemappable = new()
        {
            Fullscreen,  // F11 — OS 수준
        };

        /// <summary>
        /// 사용자에게 보여줄 로컬라이즈된 커맨드 표시 이름을 반환합니다.
        /// </summary>
        public static string GetDisplayName(string commandId)
        {
            if (_displayNameKeys.TryGetValue(commandId, out var key))
            {
                var localized = LocalizationService.L(key);
                // LocalizationService가 키를 찾지 못하면 키 자체를 반환하므로, 그 경우 fallback 사용
                if (!string.IsNullOrEmpty(localized) && localized != key)
                    return localized;
            }

            // Fallback: commandId에서 마지막 세그먼트를 PascalCase로 변환
            var lastDot = commandId.LastIndexOf('.');
            return lastDot >= 0 ? commandId.Substring(lastDot + 1) : commandId;
        }

        /// <summary>
        /// 커맨드가 속한 카테고리를 반환합니다.
        /// </summary>
        public static string GetCategory(string commandId)
        {
            return _categories.TryGetValue(commandId, out var category) ? category : "Unknown";
        }

        /// <summary>
        /// 등록된 모든 커맨드 ID 목록을 반환합니다.
        /// </summary>
        public static IReadOnlyList<string> GetAllCommands()
        {
            return _categories.Keys.ToList().AsReadOnly();
        }

        /// <summary>
        /// 해당 커맨드가 사용자에 의해 리매핑 가능한지 여부를 반환합니다.
        /// </summary>
        public static bool IsRemappable(string commandId)
        {
            return _categories.ContainsKey(commandId) && !_nonRemappable.Contains(commandId);
        }

        /// <summary>
        /// 특정 카테고리에 속한 커맨드 목록을 반환합니다.
        /// </summary>
        public static IReadOnlyList<string> GetCommandsByCategory(string category)
        {
            return _categories
                .Where(kvp => kvp.Value == category)
                .Select(kvp => kvp.Key)
                .ToList()
                .AsReadOnly();
        }

        /// <summary>
        /// 모든 카테고리 이름 목록을 반환합니다.
        /// </summary>
        public static IReadOnlyList<string> GetAllCategories()
        {
            return _categories.Values.Distinct().ToList().AsReadOnly();
        }
    }
}
