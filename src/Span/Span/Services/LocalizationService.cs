using System;
using System.Collections.Generic;
using System.Globalization;
using Windows.Globalization;

namespace Span.Services
{
    /// <summary>
    /// Simple dictionary-based localization service.
    /// Supports runtime language switching without app restart.
    /// </summary>
    public class LocalizationService
    {
        private string _language;

        public event Action? LanguageChanged;

        private static readonly Dictionary<string, Dictionary<string, string>> Strings = new()
        {
            ["en"] = new Dictionary<string, string>
            {
                // Context menu items
                ["Open"] = "Open",
                ["OpenWith"] = "Open with...",
                ["Cut"] = "Cut",
                ["Copy"] = "Copy",
                ["Paste"] = "Paste",
                ["Delete"] = "Delete",
                ["Rename"] = "Rename",
                ["CopyPath"] = "Copy path",
                ["OpenInExplorer"] = "Open in Span",
                ["Properties"] = "Properties",
                ["AddToFavorites"] = "Add to favorites",
                ["RemoveFromFavorites"] = "Remove from favorites",
                ["NewFolder"] = "New folder",

                // View submenu
                ["View"] = "View",
                ["MillerColumns"] = "Columns",
                ["Details"] = "Details",
                ["ExtraLargeIcons"] = "Extra large icons",
                ["LargeIcons"] = "Large icons",
                ["MediumIcons"] = "Medium icons",
                ["SmallIcons"] = "Small icons",

                // Sort submenu
                ["Sort"] = "Sort",
                ["Name"] = "Name",
                ["Date"] = "Date",
                ["Size"] = "Size",
                ["Type"] = "Type",
                ["Ascending"] = "Ascending",
                ["Descending"] = "Descending",

                // Group By submenu
                ["GroupBy"] = "Group by",
                ["None"] = "None",

                // Selection submenu
                ["Select"] = "Select",
                ["SelectAll"] = "Select all",
                ["SelectNone"] = "Select none",
                ["InvertSelection"] = "Invert selection",

                // Shell extensions section
                ["ShellExtensions"] = "More options",

                // Dialog strings
                ["DeleteConfirmTitle"] = "Confirm Delete",
                ["DeleteConfirmContent"] = "Move '{0}' to Recycle Bin?",
                ["PermanentDeleteTitle"] = "Confirm Permanent Delete",
                ["PermanentDeleteContent"] = "Permanently delete '{0}'?\n\nThis action cannot be undone.",
                ["PermanentDelete"] = "Permanently Delete",
                ["Cancel"] = "Cancel",
                ["NewFolderBaseName"] = "New folder",
                ["FolderItemCount"] = "{0} items",

                // New file types
                ["New"] = "New",
                ["NewTextDocument"] = "Text Document",
                ["NewWordDocument"] = "Word Document",
                ["NewExcelSpreadsheet"] = "Excel Spreadsheet",
                ["NewPowerPoint"] = "PowerPoint Presentation",
                ["NewBitmapImage"] = "Bitmap Image",
                ["NewRichTextDocument"] = "Rich Text Document",
                ["NewZipArchive"] = "Compressed (zipped) Folder",

                // Edit-with submenu
                ["EditWith"] = "Edit with...",

                // Compress/Extract
                ["CompressToZip"] = "Compress to ZIP",
                ["ExtractHere"] = "Extract here",
                ["ExtractTo"] = "Extract to folder...",

                // Drive actions
                ["Eject"] = "Eject",
                ["DisconnectDrive"] = "Disconnect",
                ["DriveCapacity"] = "{0} free of {1}",
                ["Opening"] = "Opening",

                // Connection management
                ["EditConnection"] = "Edit Connection...",
                ["RemoveConnection"] = "Remove Connection",
                ["RemoveConnectionConfirm"] = "Remove '{0}'?\n\nSaved credentials will also be deleted.",
                ["RemoveConnectionTitle"] = "Remove Connection",
                ["ConnectToServer"] = "Connect to Server",
                ["Protocol"] = "Protocol",
                ["Host"] = "Host",
                ["Port"] = "Port",
                ["Username"] = "Username",
                ["Password"] = "Password",
                ["RemotePath"] = "Remote path",
                ["DisplayNameOptional"] = "Display name (optional)",
                ["SaveConnection"] = "Save this connection",
                ["Connect"] = "Connect",
                ["ConnectionFailed"] = "Connection failed",
                ["Save"] = "Save",
                ["ConnectionRemoved"] = "'{0}' connection has been removed.",

                // Tab context menu
                ["CloseTab"] = "Close Tab",
                ["CloseOtherTabs"] = "Close Other Tabs",
                ["CloseTabsToRight"] = "Close Tabs to Right",
                ["DuplicateTab"] = "Duplicate Tab",

                // Duplicate file
                ["DuplicateSuffix"] = " - Copy",
                ["Duplicated"] = "duplicated",

                // Drag-drop
                ["Move"] = "Move",

                // View mode menu
                ["Icons"] = "Icons",
                ["ViewModeSwitch"] = "Switch view",

                // Home
                ["Home"] = "Home",
                ["HomeSearch"] = "Search Home",
                ["DevicesAndDrives"] = "Devices and drives",
                ["NetworkLocations"] = "Network locations",
                ["Favorites"] = "Favorites",

                // Network browse dialog
                ["Register"] = "Register",
                ["NetworkBrowse"] = "Browse Network",
                ["UncPathInput"] = "Enter UNC path",
                ["SearchNetwork"] = "Or search network:",
                ["SearchingComputers"] = "Searching network computers...",
                ["ComputersFound"] = "{0} computers found.",
                ["NoComputersFound"] = "No network computers found. Enter UNC path directly.",
                ["SearchingShares"] = "Searching shares on {0}...",
                ["SharesFound"] = "{0} shares found.",
                ["NoSharesFound"] = "No shares found.",

                // File operations
                ["FileOperations"] = "File Operations",
                ["CancelAll"] = "Cancel All",

                // File conflict dialog
                ["FileAlreadyExists"] = "File Already Exists",
                ["OK"] = "OK",
                ["FileConflictMessage"] = "A file with the same name already exists in this location:",
                ["SourceFile"] = "Source File",
                ["ExistingFile"] = "Existing File",
                ["FileSize"] = "Size:",
                ["FileModified"] = "Modified:",
                ["ChooseAction"] = "Choose what to do:",
                ["ReplaceFile"] = "Replace the file in the destination",
                ["ReplaceFileDesc"] = "The existing file will be overwritten",
                ["KeepBothFiles"] = "Keep both files",
                ["KeepBothFilesDesc"] = "The new file will be renamed automatically",
                ["SkipFile"] = "Skip this file",
                ["SkipFileDesc"] = "No changes will be made",
                ["ApplyToAllConflicts"] = "Apply this action to all conflicts",

                // Help overlay
                ["Help_Title"] = "Keyboard Shortcuts",
                ["Help_Navigation"] = "Navigation",
                ["Help_ColumnNav"] = "Move between columns",
                ["Help_OpenFolder"] = "Open folder / Run file",
                ["Help_ParentFolder"] = "Parent folder",
                ["Help_HomeEnd"] = "First / Last item",
                ["Help_BackForward"] = "Back / Forward",
                ["Help_AddressBar"] = "Focus address bar",
                ["Help_Search"] = "Search",
                ["Help_QuickLook"] = "Quick Look preview",
                ["Help_Edit"] = "Edit",
                ["Help_Copy"] = "Copy",
                ["Help_Cut"] = "Cut",
                ["Help_Paste"] = "Paste",
                ["Help_PasteShortcut"] = "Paste as shortcut",
                ["Help_Duplicate"] = "Duplicate",
                ["Help_Rename"] = "Rename",
                ["Help_DeleteTrash"] = "Delete (Recycle Bin)",
                ["Help_PermanentDelete"] = "Permanently delete",
                ["Help_NewFolder"] = "New folder",
                ["Help_UndoRedo"] = "Undo / Redo",
                ["Help_Selection"] = "Selection",
                ["Help_SelectAll"] = "Select all",
                ["Help_DeselectAll"] = "Deselect all",
                ["Help_InvertSelection"] = "Invert selection",
                ["Help_View"] = "View",
                ["Help_MillerColumns"] = "Columns",
                ["Help_DetailList"] = "Detail list",
                ["Help_ListView"] = "List",
                ["Help_Icons"] = "Icons",
                ["Help_SplitView"] = "Toggle split view",
                ["Help_PreviewPanel"] = "Preview panel",
                ["Help_SwitchPanel"] = "Switch panel (split view)",
                ["Help_EqualizeColumns"] = "Equalize column widths",
                ["Help_AutoFitColumns"] = "Auto-fit columns",
                ["Help_Refresh"] = "Refresh",
                ["Help_WindowTab"] = "Window / Tab",
                ["Help_NewTab"] = "New tab",
                ["Help_CloseTab"] = "Close tab",
                ["Help_NewWindow"] = "New window",
                ["Help_OpenTerminal"] = "Open terminal",
                ["Help_Settings"] = "Settings",
                ["Help_Properties"] = "Properties",
                ["Help_Help"] = "Help",
                ["Help_CloseHint"] = "Press Esc or click anywhere to close",

                // Settings
                ["Settings"] = "Settings",
                ["Settings_Back"] = "Back",
                ["Settings_SearchPlaceholder"] = "Search settings...",
                ["Settings_General"] = "General",
                ["Settings_Appearance"] = "Appearance",
                ["Settings_Browsing"] = "Browsing",
                ["Settings_Tools"] = "Tools",
                ["Settings_Advanced"] = "Advanced",
                ["Settings_About"] = "License & About",
                ["Settings_AboutNav"] = "About",
                ["Settings_OpenSourceNav"] = "Open Source",
                ["Settings_OpenSourceDesc"] = "Open source libraries and resources used in this project.",
                ["Settings_FullLicenseLink"] = "View full license text on GitHub",
                ["Settings_PlanFree"] = "Free",
                ["Settings_PlanFreeDesc"] = "You are using the free version",
                ["Settings_Language"] = "Language",
                ["Settings_LanguageDesc"] = "App interface display language",
                ["Settings_SystemDefault"] = "System Default (Recommended)",
                ["Settings_RestartNotice"] = "Restart the app for full effect.",
                ["Settings_StartupBehavior"] = "Startup behavior",
                ["Settings_StartupBehaviorDesc"] = "Screen to show when the app launches",
                ["Settings_RestoreSession"] = "Restore last session",
                ["Settings_RestoreSessionDesc"] = "Keeps previously opened tabs and paths",
                ["Settings_OpenHome"] = "Open My PC (Home)",
                ["Settings_OpenSpecificFolder"] = "Open specific folder...",
                ["Settings_CustomPath"] = "Custom path",
                ["Settings_FavoritesTree"] = "Favorites tree view",
                ["Settings_FavoritesTreeDesc"] = "Display favorites as a tree (sub-folder browsing)",
                ["Settings_SystemTray"] = "System tray",
                ["Settings_SystemTrayDesc"] = "Minimize to tray when close button is pressed",
                ["Settings_WindowPosition"] = "Remember window position",
                ["Settings_WindowPositionDesc"] = "Save window position and size on exit, restore on next launch",
                ["Settings_AppTheme"] = "App theme",
                ["Settings_ThemeDesc"] = "Overall color theme for the app",
                ["Settings_System"] = "System",
                ["Settings_Light"] = "Light",
                ["Settings_Dark"] = "Dark",
                ["Settings_ProThemes"] = "Pro-only themes",
                ["Settings_ProThemesDesc"] = "Unlock premium themes for developers",
                ["Settings_MidnightGoldDesc"] = "Black & Gold accent",
                ["Settings_CyberpunkDesc"] = "Neon purple & cyan",
                ["Settings_NordicDesc"] = "Calm blue-gray",
                ["Settings_UpgradeProThemes"] = "Upgrade to Pro and unlock",
                ["Settings_LayoutDensity"] = "Layout density",
                ["Settings_LayoutDensityDesc"] = "Adjust spacing between list items",
                ["Settings_IconPack"] = "Icon pack",
                ["Settings_IconPackDesc"] = "File/folder icon style",
                ["Settings_IconPackRestart"] = "Icon pack change requires app restart",
                ["Settings_Font"] = "Font",
                ["Settings_FontDesc"] = "Font for displaying file names",
                ["Settings_ViewOptions"] = "View options",
                ["Settings_ViewOptionsDesc"] = "File display settings",
                ["Settings_ShowHidden"] = "Show hidden items",
                ["Settings_ShowExtensions"] = "Show file extensions",
                ["Settings_CheckboxSelection"] = "Select items using checkboxes",
                ["Settings_MillerBehavior"] = "Column behavior",
                ["Settings_MillerBehaviorDesc"] = "How to open folders",
                ["Settings_SingleClick"] = "Single click",
                ["Settings_DoubleClick"] = "Double click",
                ["Settings_Thumbnails"] = "Image preview (thumbnails)",
                ["Settings_ThumbnailsDesc"] = "Thumbnails are disabled on network drives for performance",
                ["Settings_QuickLook"] = "Quick Look (preview)",
                ["Settings_QuickLookDesc"] = "Preview files quickly with the Space key",
                ["Settings_DeleteConfirm"] = "Delete confirmation dialog",
                ["Settings_DeleteConfirmDesc"] = "Ask for confirmation before deleting files",
                ["Settings_UndoHistory"] = "Undo history retention",
                ["Settings_UndoHistoryDesc"] = "Number of undo operations to keep (0 = disabled)",
                ["Settings_Developer"] = "Developer",
                ["Settings_TerminalApp"] = "Default terminal app",
                ["Settings_TerminalAppDesc"] = "Launched with Ctrl+` or right-click 'Open terminal'",
                ["Settings_SmartRun"] = "Smart Run (quick commands)",
                ["Settings_SmartRunDesc"] = "Manage frequently used command shortcuts",
                ["Settings_AddShortcut"] = "Add shortcut",
                ["Settings_ShellExtras"] = "Windows shell extension menu",
                ["Settings_ShellExtrasDesc"] = "Show Share, Send to, Include in library, Pin to Start, etc. in context menu",
                ["Settings_DeveloperMenu"] = "Developer context menu",
                ["Settings_DeveloperMenuDesc"] = "Show Git, Visual Studio, VS Code, etc. in context menu",
                ["Settings_CopilotMenu"] = "Copilot menu",
                ["Settings_CopilotMenuDesc"] = "Show Microsoft Copilot items in context menu",
                ["Settings_ContextMenu"] = "Context menu",
                ["Settings_ContextMenuDesc"] = "Add 'Open in Span' to Windows right-click menu",
                ["Settings_CheckUpdate"] = "Check for updates",
                ["Settings_Checking"] = "Checking...",
                ["Settings_UpToDate"] = "You're up to date",
                ["Settings_UpgradePro"] = "Upgrade to Pro",
                ["Settings_UpgradeProDesc"] = "Get the Pro badge and support the developer.",
                ["Settings_UnlockThemes"] = "Unlock premium themes",
                ["Settings_UnlimitedSmartRun"] = "Unlimited Smart Run shortcuts",
                ["Settings_AllPremiumFeatures"] = "All future premium features",
                ["Settings_BuyMeCoffee"] = "Buy me a coffee",
                ["Settings_BuyMeCoffeeDesc"] = "If you enjoy Span, support with a coffee!",
                ["Settings_Links"] = "Links",
                ["Settings_GitHub"] = "GitHub Repository",
                ["Settings_BugReport"] = "Report a Bug",
                ["Settings_Privacy"] = "Privacy Policy",
                ["Settings_EvalCopy"] = "Evaluation Copy (Unregistered)",
            },
            ["ko"] = new Dictionary<string, string>
            {
                // Context menu items
                ["Open"] = "\uc5f4\uae30",
                ["OpenWith"] = "\uc5f0\uacb0 \ud504\ub85c\uadf8\ub7a8...",
                ["Cut"] = "\uc798\ub77c\ub0b4\uae30",
                ["Copy"] = "\ubcf5\uc0ac",
                ["Paste"] = "\ubd99\uc5ec\ub123\uae30",
                ["Delete"] = "\uc0ad\uc81c",
                ["Rename"] = "\uc774\ub984 \ubc14\uafb8\uae30",
                ["CopyPath"] = "\uacbd\ub85c \ubcf5\uc0ac",
                ["OpenInExplorer"] = "Span\uc73c\ub85c \uc5f4\uae30",
                ["Properties"] = "\uc18d\uc131",
                ["AddToFavorites"] = "\uc990\uaca8\ucc3e\uae30\uc5d0 \ucd94\uac00",
                ["RemoveFromFavorites"] = "\uc990\uaca8\ucc3e\uae30\uc5d0\uc11c \uc81c\uac70",
                ["NewFolder"] = "\uc0c8 \ud3f4\ub354",

                // View submenu
                ["View"] = "\ubcf4\uae30",
                ["MillerColumns"] = "\ucee8\ub7fc",
                ["Details"] = "\uc790\uc138\ud788",
                ["ExtraLargeIcons"] = "\uc544\uc8fc \ud070 \uc544\uc774\ucf58",
                ["LargeIcons"] = "\ud070 \uc544\uc774\ucf58",
                ["MediumIcons"] = "\ubcf4\ud1b5 \uc544\uc774\ucf58",
                ["SmallIcons"] = "\uc791\uc740 \uc544\uc774\ucf58",

                // Sort submenu
                ["Sort"] = "\uc815\ub82c",
                ["Name"] = "\uc774\ub984",
                ["Date"] = "\ub0a0\uc9dc",
                ["Size"] = "\ud06c\uae30",
                ["Type"] = "\uc885\ub958",
                ["Ascending"] = "\uc624\ub984\ucc28\uc21c",
                ["Descending"] = "\ub0b4\ub9bc\ucc28\uc21c",

                // Group By submenu
                ["GroupBy"] = "\uadf8\ub8f9\ud654",
                ["None"] = "\uc5c6\uc74c",

                // Selection submenu
                ["Select"] = "\uc120\ud0dd",
                ["SelectAll"] = "\ubaa8\ub450 \uc120\ud0dd",
                ["SelectNone"] = "\uc120\ud0dd \ud574\uc81c",
                ["InvertSelection"] = "\uc120\ud0dd \ubc18\uc804",

                // Shell extensions section
                ["ShellExtensions"] = "\ucd94\uac00 \uc635\uc158",

                // Dialog strings
                ["DeleteConfirmTitle"] = "\uc0ad\uc81c \ud655\uc778",
                ["DeleteConfirmContent"] = "'{0}'\uc744(\ub97c) \ud734\uc9c0\ud1b5\uc73c\ub85c \uc774\ub3d9\ud558\uc2dc\uaca0\uc2b5\ub2c8\uae4c?",
                ["PermanentDeleteTitle"] = "\uc601\uad6c \uc0ad\uc81c \ud655\uc778",
                ["PermanentDeleteContent"] = "'{0}'\uc744(\ub97c) \uc601\uad6c\uc801\uc73c\ub85c \uc0ad\uc81c\ud558\uc2dc\uaca0\uc2b5\ub2c8\uae4c?\n\n\uc774 \uc791\uc5c5\uc740 \ub418\ub3cc\ub9b4 \uc218 \uc5c6\uc2b5\ub2c8\ub2e4.",
                ["PermanentDelete"] = "\uc601\uad6c \uc0ad\uc81c",
                ["Cancel"] = "\ucde8\uc18c",
                ["NewFolderBaseName"] = "\uc0c8 \ud3f4\ub354",
                ["FolderItemCount"] = "{0}\uac1c \ud56d\ubaa9",

                // New file types
                ["New"] = "\uc0c8\ub85c \ub9cc\ub4e4\uae30",
                ["NewTextDocument"] = "\ud14d\uc2a4\ud2b8 \ubb38\uc11c",
                ["NewWordDocument"] = "Word \ubb38\uc11c",
                ["NewExcelSpreadsheet"] = "Excel \uc2a4\ud504\ub808\ub4dc\uc2dc\ud2b8",
                ["NewPowerPoint"] = "PowerPoint \ud504\ub808\uc820\ud14c\uc774\uc158",
                ["NewBitmapImage"] = "\ube44\ud2b8\ub9f5 \uc774\ubbf8\uc9c0",
                ["NewRichTextDocument"] = "\uc11c\uc2dd \uc788\ub294 \ud14d\uc2a4\ud2b8 \ubb38\uc11c",
                ["NewZipArchive"] = "\uc555\ucd95(zip) \ud3f4\ub354",

                // Edit-with submenu
                ["EditWith"] = "\ud3b8\uc9d1 \ud504\ub85c\uadf8\ub7a8",

                // Compress/Extract
                ["CompressToZip"] = "ZIP\uc73c\ub85c \uc555\ucd95",
                ["ExtractHere"] = "\uc5ec\uae30\uc5d0 \uc555\ucd95 \ud480\uae30",
                ["ExtractTo"] = "\ud3f4\ub354\uc5d0 \uc555\ucd95 \ud480\uae30...",

                // Drive actions
                ["Eject"] = "꺼내기",
                ["DisconnectDrive"] = "연결 끊기",
                ["DriveCapacity"] = "{1} 중 {0} 사용 가능",
                ["Opening"] = "여는 중",

                // Connection management
                ["EditConnection"] = "\uc5f0\uacb0 \ud3b8\uc9d1...",
                ["RemoveConnection"] = "\uc800\uc7a5\ub41c \uc5f0\uacb0 \uc81c\uac70",
                ["RemoveConnectionConfirm"] = "'{0}' \uc5f0\uacb0\uc744 \uc81c\uac70\ud558\uc2dc\uaca0\uc2b5\ub2c8\uae4c?\n\n\uc800\uc7a5\ub41c \uc790\uaca9 \uc99d\uba85\ub3c4 \ud568\uaed8 \uc0ad\uc81c\ub429\ub2c8\ub2e4.",
                ["RemoveConnectionTitle"] = "\uc5f0\uacb0 \uc81c\uac70",
                ["ConnectToServer"] = "\uc11c\ubc84\uc5d0 \uc5f0\uacb0",
                ["Protocol"] = "\ud504\ub85c\ud1a0\ucf5c",
                ["Host"] = "\ud638\uc2a4\ud2b8",
                ["Port"] = "\ud3ec\ud2b8",
                ["Username"] = "\uc0ac\uc6a9\uc790\uba85",
                ["Password"] = "\ube44\ubc00\ubc88\ud638",
                ["RemotePath"] = "\uc6d0\uaca9 \uacbd\ub85c",
                ["DisplayNameOptional"] = "\ud45c\uc2dc \uc774\ub984 (\uc120\ud0dd)",
                ["SaveConnection"] = "\uc774 \uc5f0\uacb0 \uc800\uc7a5",
                ["Connect"] = "\uc5f0\uacb0",
                ["ConnectionFailed"] = "\uc5f0\uacb0 \uc2e4\ud328",
                ["Save"] = "\uc800\uc7a5",
                ["ConnectionRemoved"] = "'{0}' \uc5f0\uacb0\uc774 \uc81c\uac70\ub418\uc5c8\uc2b5\ub2c8\ub2e4.",

                // Tab context menu
                ["CloseTab"] = "\ud0ed \ub2eb\uae30",
                ["CloseOtherTabs"] = "\ub2e4\ub978 \ud0ed \ubaa8\ub450 \ub2eb\uae30",
                ["CloseTabsToRight"] = "\uc624\ub978\ucabd \ud0ed \ub2eb\uae30",
                ["DuplicateTab"] = "\ud0ed \ubcf5\uc81c",

                // Duplicate file
                ["DuplicateSuffix"] = " - \ubcf5\uc0ac\ubcf8",
                ["Duplicated"] = "\ubcf5\uc81c\ub428",

                // Drag-drop
                ["Move"] = "\uc774\ub3d9",

                // View mode menu
                ["Icons"] = "아이콘",
                ["ViewModeSwitch"] = "보기 전환",

                // Home
                ["Home"] = "홈",
                ["HomeSearch"] = "홈 검색",
                ["DevicesAndDrives"] = "장치 및 드라이브",
                ["NetworkLocations"] = "네트워크 위치",
                ["Favorites"] = "즐겨찾기",

                // Network browse dialog
                ["Register"] = "등록",
                ["NetworkBrowse"] = "네트워크 찾아보기",
                ["UncPathInput"] = "UNC 경로 직접 입력",
                ["SearchNetwork"] = "또는 네트워크에서 찾기:",
                ["SearchingComputers"] = "네트워크 컴퓨터를 검색 중...",
                ["ComputersFound"] = "{0}개의 컴퓨터를 찾았습니다.",
                ["NoComputersFound"] = "네트워크 컴퓨터를 찾을 수 없습니다. UNC 경로를 직접 입력하세요.",
                ["SearchingShares"] = "{0} 서버의 공유 폴더를 검색 중...",
                ["SharesFound"] = "{0}개의 공유 폴더를 찾았습니다.",
                ["NoSharesFound"] = "공유 폴더를 찾을 수 없습니다.",

                // File operations
                ["FileOperations"] = "파일 작업",
                ["CancelAll"] = "모두 취소",

                // File conflict dialog
                ["FileAlreadyExists"] = "파일이 이미 존재합니다",
                ["OK"] = "확인",
                ["FileConflictMessage"] = "같은 이름의 파일이 이 위치에 이미 있습니다:",
                ["SourceFile"] = "원본 파일",
                ["ExistingFile"] = "기존 파일",
                ["FileSize"] = "크기:",
                ["FileModified"] = "수정일:",
                ["ChooseAction"] = "수행할 작업을 선택하세요:",
                ["ReplaceFile"] = "대상 파일 바꾸기",
                ["ReplaceFileDesc"] = "기존 파일을 덮어씁니다",
                ["KeepBothFiles"] = "두 파일 모두 유지",
                ["KeepBothFilesDesc"] = "새 파일의 이름이 자동으로 변경됩니다",
                ["SkipFile"] = "이 파일 건너뛰기",
                ["SkipFileDesc"] = "변경 사항 없음",
                ["ApplyToAllConflicts"] = "모든 충돌에 이 작업 적용",

                // Help overlay
                ["Help_Title"] = "키보드 단축키",
                ["Help_Navigation"] = "탐색",
                ["Help_ColumnNav"] = "컬럼 간 이동",
                ["Help_OpenFolder"] = "폴더 열기 / 파일 실행",
                ["Help_ParentFolder"] = "상위 폴더",
                ["Help_HomeEnd"] = "목록 처음 / 끝",
                ["Help_BackForward"] = "뒤로 / 앞으로",
                ["Help_AddressBar"] = "주소 표시줄 포커스",
                ["Help_Search"] = "검색",
                ["Help_QuickLook"] = "Quick Look 미리보기",
                ["Help_Edit"] = "편집",
                ["Help_Copy"] = "복사",
                ["Help_Cut"] = "잘라내기",
                ["Help_Paste"] = "붙여넣기",
                ["Help_PasteShortcut"] = "바로가기로 붙여넣기",
                ["Help_Duplicate"] = "복제",
                ["Help_Rename"] = "이름 변경",
                ["Help_DeleteTrash"] = "삭제 (휴지통)",
                ["Help_PermanentDelete"] = "영구 삭제",
                ["Help_NewFolder"] = "새 폴더",
                ["Help_UndoRedo"] = "실행 취소 / 다시 실행",
                ["Help_Selection"] = "선택",
                ["Help_SelectAll"] = "전체 선택",
                ["Help_DeselectAll"] = "선택 해제",
                ["Help_InvertSelection"] = "선택 반전",
                ["Help_View"] = "보기",
                ["Help_MillerColumns"] = "컬럼",
                ["Help_DetailList"] = "상세 목록",
                ["Help_ListView"] = "리스트",
                ["Help_Icons"] = "아이콘",
                ["Help_SplitView"] = "분할 뷰 토글",
                ["Help_PreviewPanel"] = "미리보기 패널",
                ["Help_SwitchPanel"] = "패널 전환 (분할 뷰)",
                ["Help_EqualizeColumns"] = "컬럼 너비 통일",
                ["Help_AutoFitColumns"] = "컬럼 자동 맞춤",
                ["Help_Refresh"] = "새로고침",
                ["Help_WindowTab"] = "창 / 탭",
                ["Help_NewTab"] = "새 탭",
                ["Help_CloseTab"] = "탭 닫기",
                ["Help_NewWindow"] = "새 창",
                ["Help_OpenTerminal"] = "터미널 열기",
                ["Help_Settings"] = "설정",
                ["Help_Properties"] = "속성",
                ["Help_Help"] = "도움말",
                ["Help_CloseHint"] = "Esc 또는 아무 곳이나 클릭하여 닫기",

                // Settings
                ["Settings"] = "설정",
                ["Settings_Back"] = "뒤로",
                ["Settings_SearchPlaceholder"] = "설정 검색...",
                ["Settings_General"] = "일반",
                ["Settings_Appearance"] = "모양",
                ["Settings_Browsing"] = "탐색",
                ["Settings_Tools"] = "도구",
                ["Settings_Advanced"] = "고급",
                ["Settings_About"] = "라이선스 및 정보",
                ["Settings_AboutNav"] = "정보",
                ["Settings_OpenSourceNav"] = "오픈소스",
                ["Settings_OpenSourceDesc"] = "이 프로젝트에서 사용된 오픈소스 라이브러리 및 리소스 목록입니다.",
                ["Settings_FullLicenseLink"] = "GitHub에서 전체 라이선스 보기",
                ["Settings_PlanFree"] = "Free",
                ["Settings_PlanFreeDesc"] = "기본 기능을 사용 중입니다",
                ["Settings_Language"] = "언어 (Language)",
                ["Settings_LanguageDesc"] = "앱 인터페이스 표시 언어",
                ["Settings_SystemDefault"] = "시스템 기본값 (권장)",
                ["Settings_RestartNotice"] = "앱을 재시작해야 완벽하게 적용됩니다.",
                ["Settings_StartupBehavior"] = "시작 시 동작",
                ["Settings_StartupBehaviorDesc"] = "앱을 실행할 때 보여줄 화면",
                ["Settings_RestoreSession"] = "마지막 세션 복원",
                ["Settings_RestoreSessionDesc"] = "이전에 열어둔 탭과 경로를 유지합니다",
                ["Settings_OpenHome"] = "내 PC (홈) 열기",
                ["Settings_OpenSpecificFolder"] = "특정 폴더 열기...",
                ["Settings_CustomPath"] = "사용자 지정 경로",
                ["Settings_FavoritesTree"] = "즐겨찾기 트리뷰",
                ["Settings_FavoritesTreeDesc"] = "즐겨찾기를 트리뷰로 표시 (하위 폴더 탐색 가능)",
                ["Settings_SystemTray"] = "시스템 트레이",
                ["Settings_SystemTrayDesc"] = "닫기 버튼을 누르면 트레이로 최소화",
                ["Settings_WindowPosition"] = "창 위치 기억",
                ["Settings_WindowPositionDesc"] = "앱 종료 시 창 위치와 크기를 저장하여 다음 실행 시 복원",
                ["Settings_AppTheme"] = "앱 테마",
                ["Settings_ThemeDesc"] = "앱의 전체 색상 테마 설정",
                ["Settings_System"] = "시스템",
                ["Settings_Light"] = "라이트",
                ["Settings_Dark"] = "다크",
                ["Settings_ProThemes"] = "Pro 전용 테마",
                ["Settings_ProThemesDesc"] = "개발자를 위한 프리미엄 테마 잠금 해제",
                ["Settings_MidnightGoldDesc"] = "블랙 & 골드 포인트",
                ["Settings_CyberpunkDesc"] = "네온 퍼플 & 사이언",
                ["Settings_NordicDesc"] = "차분한 블루 그레이",
                ["Settings_UpgradeProThemes"] = "Pro로 업그레이드하고 잠금 해제",
                ["Settings_LayoutDensity"] = "레이아웃 밀도",
                ["Settings_LayoutDensityDesc"] = "목록 항목 간격 조절",
                ["Settings_IconPack"] = "아이콘 팩",
                ["Settings_IconPackDesc"] = "파일/폴더 아이콘 스타일",
                ["Settings_IconPackRestart"] = "아이콘 팩 변경은 앱 재시작 후 적용됩니다",
                ["Settings_Font"] = "폰트",
                ["Settings_FontDesc"] = "파일 이름 표시에 사용할 폰트",
                ["Settings_ViewOptions"] = "보기 옵션",
                ["Settings_ViewOptionsDesc"] = "파일 표시 관련 설정",
                ["Settings_ShowHidden"] = "숨김 항목 표시",
                ["Settings_ShowExtensions"] = "파일 확장자 표시",
                ["Settings_CheckboxSelection"] = "체크박스 사용하여 항목 선택",
                ["Settings_MillerBehavior"] = "컬럼 동작",
                ["Settings_MillerBehaviorDesc"] = "폴더 열기 방식",
                ["Settings_SingleClick"] = "한 번 클릭",
                ["Settings_DoubleClick"] = "더블 클릭",
                ["Settings_Thumbnails"] = "이미지 미리보기 (썸네일)",
                ["Settings_ThumbnailsDesc"] = "성능을 위해 네트워크 드라이브에서는 썸네일을 끕니다",
                ["Settings_QuickLook"] = "Quick Look (빠른 미리보기)",
                ["Settings_QuickLookDesc"] = "Space 키로 파일을 빠르게 미리봅니다",
                ["Settings_DeleteConfirm"] = "삭제 확인 대화상자",
                ["Settings_DeleteConfirmDesc"] = "파일 삭제 전 확인을 요청합니다",
                ["Settings_UndoHistory"] = "실행 취소 (Undo) 기록 보관",
                ["Settings_UndoHistoryDesc"] = "보관할 실행 취소 작업 수 (0 = 비활성)",
                ["Settings_Developer"] = "개발자",
                ["Settings_TerminalApp"] = "기본 터미널 앱",
                ["Settings_TerminalAppDesc"] = "Ctrl+` 또는 우클릭 '터미널 열기' 시 실행",
                ["Settings_SmartRun"] = "Smart Run (빠른 명령 실행)",
                ["Settings_SmartRunDesc"] = "자주 쓰는 명령어 단축키 관리",
                ["Settings_AddShortcut"] = "단축키 추가",
                ["Settings_ShellExtras"] = "Windows 셸 확장 메뉴",
                ["Settings_ShellExtrasDesc"] = "우클릭 메뉴에 공유, 보내기, 라이브러리 포함, 시작 화면 고정 등 표시",
                ["Settings_DeveloperMenu"] = "개발자 컨텍스트 메뉴",
                ["Settings_DeveloperMenuDesc"] = "우클릭 메뉴에 Git, Visual Studio, VS Code 등 개발 도구 항목 표시",
                ["Settings_CopilotMenu"] = "Copilot 메뉴",
                ["Settings_CopilotMenuDesc"] = "우클릭 메뉴에 Microsoft Copilot 관련 항목 표시",
                ["Settings_ContextMenu"] = "컨텍스트 메뉴",
                ["Settings_ContextMenuDesc"] = "윈도우 우클릭 메뉴에 'Open in Span' 추가",
                ["Settings_CheckUpdate"] = "업데이트 확인",
                ["Settings_Checking"] = "확인 중...",
                ["Settings_UpToDate"] = "최신 버전입니다",
                ["Settings_UpgradePro"] = "Upgrade to Pro",
                ["Settings_UpgradeProDesc"] = "Pro 배지를 획득하고 개발자를 응원해주세요.",
                ["Settings_UnlockThemes"] = "프리미엄 테마 잠금 해제",
                ["Settings_UnlimitedSmartRun"] = "Smart Run 무제한 단축키",
                ["Settings_AllPremiumFeatures"] = "향후 모든 프리미엄 기능",
                ["Settings_BuyMeCoffee"] = "Buy me a coffee",
                ["Settings_BuyMeCoffeeDesc"] = "Span이 마음에 드셨다면, 커피 한 잔으로 응원해 주세요!",
                ["Settings_Links"] = "관련 링크",
                ["Settings_GitHub"] = "GitHub 저장소",
                ["Settings_BugReport"] = "버그 제보",
                ["Settings_Privacy"] = "개인정보 처리방침",
                ["Settings_EvalCopy"] = "Evaluation Copy (미등록)",
            },
            ["ja"] = new Dictionary<string, string>
            {
                ["Open"] = "\u958b\u304f",
                ["OpenWith"] = "\u30d7\u30ed\u30b0\u30e9\u30e0\u304b\u3089\u958b\u304f...",
                ["Cut"] = "\u5207\u308a\u53d6\u308a",
                ["Copy"] = "\u30b3\u30d4\u30fc",
                ["Paste"] = "\u8cbc\u308a\u4ed8\u3051",
                ["Delete"] = "\u524a\u9664",
                ["Rename"] = "\u540d\u524d\u306e\u5909\u66f4",
                ["CopyPath"] = "\u30d1\u30b9\u3092\u30b3\u30d4\u30fc",
                ["OpenInExplorer"] = "Span\u3067\u958b\u304f",
                ["Properties"] = "\u30d7\u30ed\u30d1\u30c6\u30a3",
                ["AddToFavorites"] = "\u304a\u6c17\u306b\u5165\u308a\u306b\u8ffd\u52a0",
                ["RemoveFromFavorites"] = "\u304a\u6c17\u306b\u5165\u308a\u304b\u3089\u524a\u9664",
                ["NewFolder"] = "\u65b0\u3057\u3044\u30d5\u30a9\u30eb\u30c0\u30fc",
                ["View"] = "\u8868\u793a",
                ["MillerColumns"] = "\u30ab\u30e9\u30e0",
                ["Details"] = "\u8a73\u7d30",
                ["ExtraLargeIcons"] = "\u7279\u5927\u30a2\u30a4\u30b3\u30f3",
                ["LargeIcons"] = "\u5927\u30a2\u30a4\u30b3\u30f3",
                ["MediumIcons"] = "\u4e2d\u30a2\u30a4\u30b3\u30f3",
                ["SmallIcons"] = "\u5c0f\u30a2\u30a4\u30b3\u30f3",
                ["Sort"] = "\u4e26\u3079\u66ff\u3048",
                ["Name"] = "\u540d\u524d",
                ["Date"] = "\u65e5\u4ed8",
                ["Size"] = "\u30b5\u30a4\u30ba",
                ["Type"] = "\u7a2e\u985e",
                ["Ascending"] = "\u6607\u9806",
                ["Descending"] = "\u964d\u9806",

                // Group By submenu
                ["GroupBy"] = "\u30b0\u30eb\u30fc\u30d7\u5316",
                ["None"] = "\u306a\u3057",

                ["ShellExtensions"] = "\u305d\u306e\u4ed6\u306e\u30aa\u30d7\u30b7\u30e7\u30f3",

                // Selection submenu
                ["Select"] = "\u9078\u629e",
                ["SelectAll"] = "\u3059\u3079\u3066\u9078\u629e",
                ["SelectNone"] = "\u9078\u629e\u89e3\u9664",
                ["InvertSelection"] = "\u9078\u629e\u306e\u53cd\u8ee2",

                // Dialog strings
                ["DeleteConfirmTitle"] = "\u524a\u9664\u306e\u78ba\u8a8d",
                ["DeleteConfirmContent"] = "'{0}'\u3092\u3054\u307f\u7bb1\u306b\u79fb\u52d5\u3057\u307e\u3059\u304b\uff1f",
                ["PermanentDeleteTitle"] = "\u5b8c\u5168\u524a\u9664\u306e\u78ba\u8a8d",
                ["PermanentDeleteContent"] = "'{0}'\u3092\u5b8c\u5168\u306b\u524a\u9664\u3057\u307e\u3059\u304b\uff1f\n\n\u3053\u306e\u64cd\u4f5c\u306f\u5143\u306b\u623b\u305b\u307e\u305b\u3093\u3002",
                ["PermanentDelete"] = "\u5b8c\u5168\u306b\u524a\u9664",
                ["Cancel"] = "\u30ad\u30e3\u30f3\u30bb\u30eb",
                ["NewFolderBaseName"] = "\u65b0\u3057\u3044\u30d5\u30a9\u30eb\u30c0\u30fc",
                ["FolderItemCount"] = "{0}\u500b\u306e\u9805\u76ee",

                // New file types
                ["New"] = "\u65b0\u898f\u4f5c\u6210",
                ["NewTextDocument"] = "\u30c6\u30ad\u30b9\u30c8 \u30c9\u30ad\u30e5\u30e1\u30f3\u30c8",
                ["NewWordDocument"] = "Word \u30c9\u30ad\u30e5\u30e1\u30f3\u30c8",
                ["NewExcelSpreadsheet"] = "Excel \u30b9\u30d7\u30ec\u30c3\u30c9\u30b7\u30fc\u30c8",
                ["NewPowerPoint"] = "PowerPoint \u30d7\u30ec\u30bc\u30f3\u30c6\u30fc\u30b7\u30e7\u30f3",
                ["NewBitmapImage"] = "\u30d3\u30c3\u30c8\u30de\u30c3\u30d7 \u30a4\u30e1\u30fc\u30b8",
                ["NewRichTextDocument"] = "\u30ea\u30c3\u30c1\u30c6\u30ad\u30b9\u30c8 \u30c9\u30ad\u30e5\u30e1\u30f3\u30c8",
                ["NewZipArchive"] = "\u5727\u7e2e(zip)\u30d5\u30a9\u30eb\u30c0\u30fc",

                // Edit-with submenu
                ["EditWith"] = "\u7de8\u96c6\u30d7\u30ed\u30b0\u30e9\u30e0",

                // Compress/Extract
                ["CompressToZip"] = "ZIP\u306b\u5727\u7e2e",
                ["ExtractHere"] = "\u3053\u3053\u306b\u5c55\u958b",
                ["ExtractTo"] = "\u30d5\u30a9\u30eb\u30c0\u30fc\u306b\u5c55\u958b...",

                // Drive actions
                ["Eject"] = "\u53d6\u308a\u51fa\u3059",
                ["DisconnectDrive"] = "\u5207\u65ad",
                ["DriveCapacity"] = "{1}\u306e\u3046\u3061{0}\u7a7a\u304d",
                ["Opening"] = "\u958b\u3044\u3066\u3044\u307e\u3059",

                // Connection management
                ["EditConnection"] = "\u63a5\u7d9a\u3092\u7de8\u96c6...",
                ["RemoveConnection"] = "\u63a5\u7d9a\u3092\u524a\u9664",
                ["RemoveConnectionConfirm"] = "'{0}'\u306e\u63a5\u7d9a\u3092\u524a\u9664\u3057\u307e\u3059\u304b\uff1f\n\n\u4fdd\u5b58\u3055\u308c\u305f\u8a8d\u8a3c\u60c5\u5831\u3082\u524a\u9664\u3055\u308c\u307e\u3059\u3002",
                ["RemoveConnectionTitle"] = "\u63a5\u7d9a\u306e\u524a\u9664",
                ["ConnectToServer"] = "\u30b5\u30fc\u30d0\u30fc\u306b\u63a5\u7d9a",
                ["Protocol"] = "\u30d7\u30ed\u30c8\u30b3\u30eb",
                ["Host"] = "\u30db\u30b9\u30c8",
                ["Port"] = "\u30dd\u30fc\u30c8",
                ["Username"] = "\u30e6\u30fc\u30b6\u30fc\u540d",
                ["Password"] = "\u30d1\u30b9\u30ef\u30fc\u30c9",
                ["RemotePath"] = "\u30ea\u30e2\u30fc\u30c8\u30d1\u30b9",
                ["DisplayNameOptional"] = "\u8868\u793a\u540d\uff08\u4efb\u610f\uff09",
                ["SaveConnection"] = "\u3053\u306e\u63a5\u7d9a\u3092\u4fdd\u5b58",
                ["Connect"] = "\u63a5\u7d9a",
                ["ConnectionFailed"] = "\u63a5\u7d9a\u5931\u6557",
                ["Save"] = "\u4fdd\u5b58",
                ["ConnectionRemoved"] = "'{0}'\u306e\u63a5\u7d9a\u3092\u524a\u9664\u3057\u307e\u3057\u305f\u3002",

                // Tab context menu
                ["CloseTab"] = "\u30bf\u30d6\u3092\u9589\u3058\u308b",
                ["CloseOtherTabs"] = "\u4ed6\u306e\u30bf\u30d6\u3092\u3059\u3079\u3066\u9589\u3058\u308b",
                ["CloseTabsToRight"] = "\u53f3\u5074\u306e\u30bf\u30d6\u3092\u9589\u3058\u308b",
                ["DuplicateTab"] = "\u30bf\u30d6\u3092\u8907\u88fd",

                // Duplicate file
                ["DuplicateSuffix"] = " - \u30b3\u30d4\u30fc",
                ["Duplicated"] = "\u8907\u88fd\u3057\u307e\u3057\u305f",

                // Drag-drop
                ["Move"] = "\u79fb\u52d5",

                // View mode menu
                ["Icons"] = "アイコン",
                ["ViewModeSwitch"] = "表示切替",

                // Home
                ["Home"] = "ホーム",
                ["HomeSearch"] = "ホーム検索",
                ["DevicesAndDrives"] = "デバイスとドライブ",
                ["NetworkLocations"] = "ネットワークの場所",
                ["Favorites"] = "お気に入り",

                // Network browse dialog
                ["Register"] = "登録",
                ["NetworkBrowse"] = "ネットワーク参照",
                ["UncPathInput"] = "UNCパスを入力",
                ["SearchNetwork"] = "ネットワークから検索:",
                ["SearchingComputers"] = "ネットワークコンピュータを検索中...",
                ["ComputersFound"] = "{0}台のコンピュータが見つかりました。",
                ["NoComputersFound"] = "ネットワークが見つかりません。UNCパスを入力してください。",
                ["SearchingShares"] = "{0}の共有フォルダを検索中...",
                ["SharesFound"] = "{0}個の共有フォルダが見つかりました。",
                ["NoSharesFound"] = "共有フォルダが見つかりません。",

                // File operations
                ["FileOperations"] = "ファイル操作",
                ["CancelAll"] = "すべてキャンセル",

                // File conflict dialog
                ["FileAlreadyExists"] = "ファイルが既に存在します",
                ["OK"] = "OK",
                ["FileConflictMessage"] = "同じ名前のファイルがこの場所に既に存在します：",
                ["SourceFile"] = "元のファイル",
                ["ExistingFile"] = "既存のファイル",
                ["FileSize"] = "サイズ:",
                ["FileModified"] = "更新日:",
                ["ChooseAction"] = "実行する操作を選択してください：",
                ["ReplaceFile"] = "ファイルを置き換える",
                ["ReplaceFileDesc"] = "既存のファイルが上書きされます",
                ["KeepBothFiles"] = "両方のファイルを保持",
                ["KeepBothFilesDesc"] = "新しいファイルの名前が自動的に変更されます",
                ["SkipFile"] = "このファイルをスキップ",
                ["SkipFileDesc"] = "変更は行われません",
                ["ApplyToAllConflicts"] = "すべての競合にこの操作を適用",

                // Help overlay
                ["Help_Title"] = "キーボードショートカット",
                ["Help_Navigation"] = "ナビゲーション",
                ["Help_ColumnNav"] = "カラム間の移動",
                ["Help_OpenFolder"] = "フォルダーを開く / ファイルを実行",
                ["Help_ParentFolder"] = "親フォルダー",
                ["Help_HomeEnd"] = "リストの先頭 / 末尾",
                ["Help_BackForward"] = "戻る / 進む",
                ["Help_AddressBar"] = "アドレスバーにフォーカス",
                ["Help_Search"] = "検索",
                ["Help_QuickLook"] = "Quick Look プレビュー",
                ["Help_Edit"] = "編集",
                ["Help_Copy"] = "コピー",
                ["Help_Cut"] = "切り取り",
                ["Help_Paste"] = "貼り付け",
                ["Help_PasteShortcut"] = "ショートカットとして貼り付け",
                ["Help_Duplicate"] = "複製",
                ["Help_Rename"] = "名前の変更",
                ["Help_DeleteTrash"] = "削除 (ごみ箱)",
                ["Help_PermanentDelete"] = "完全に削除",
                ["Help_NewFolder"] = "新しいフォルダー",
                ["Help_UndoRedo"] = "元に戻す / やり直し",
                ["Help_Selection"] = "選択",
                ["Help_SelectAll"] = "すべて選択",
                ["Help_DeselectAll"] = "選択解除",
                ["Help_InvertSelection"] = "選択の反転",
                ["Help_View"] = "表示",
                ["Help_MillerColumns"] = "カラム",
                ["Help_DetailList"] = "詳細リスト",
                ["Help_ListView"] = "リスト",
                ["Help_Icons"] = "アイコン",
                ["Help_SplitView"] = "分割ビュー切替",
                ["Help_PreviewPanel"] = "プレビューパネル",
                ["Help_SwitchPanel"] = "パネル切替 (分割ビュー)",
                ["Help_EqualizeColumns"] = "カラム幅を均等化",
                ["Help_AutoFitColumns"] = "カラム自動調整",
                ["Help_Refresh"] = "更新",
                ["Help_WindowTab"] = "ウィンドウ / タブ",
                ["Help_NewTab"] = "新しいタブ",
                ["Help_CloseTab"] = "タブを閉じる",
                ["Help_NewWindow"] = "新しいウィンドウ",
                ["Help_OpenTerminal"] = "ターミナルを開く",
                ["Help_Settings"] = "設定",
                ["Help_Properties"] = "プロパティ",
                ["Help_Help"] = "ヘルプ",
                ["Help_CloseHint"] = "Escまたは任意の場所をクリックして閉じる",

                // Settings
                ["Settings"] = "設定",
                ["Settings_Back"] = "戻る",
                ["Settings_SearchPlaceholder"] = "設定を検索...",
                ["Settings_General"] = "一般",
                ["Settings_Appearance"] = "外観",
                ["Settings_Browsing"] = "ブラウズ",
                ["Settings_Tools"] = "ツール",
                ["Settings_Advanced"] = "詳細設定",
                ["Settings_About"] = "ライセンスと情報",
                ["Settings_AboutNav"] = "情報",
                ["Settings_OpenSourceNav"] = "オープンソース",
                ["Settings_OpenSourceDesc"] = "このプロジェクトで使用されているオープンソースライブラリとリソースの一覧です。",
                ["Settings_FullLicenseLink"] = "GitHubで全ライセンスを表示",
                ["Settings_PlanFree"] = "Free",
                ["Settings_PlanFreeDesc"] = "無料版をご利用中です",
                ["Settings_Language"] = "言語 (Language)",
                ["Settings_LanguageDesc"] = "アプリのインターフェース表示言語",
                ["Settings_SystemDefault"] = "システムデフォルト（推奨）",
                ["Settings_RestartNotice"] = "完全に適用するにはアプリの再起動が必要です。",
                ["Settings_StartupBehavior"] = "起動時の動作",
                ["Settings_StartupBehaviorDesc"] = "アプリ起動時に表示する画面",
                ["Settings_RestoreSession"] = "前回のセッションを復元",
                ["Settings_RestoreSessionDesc"] = "以前開いていたタブとパスを維持します",
                ["Settings_OpenHome"] = "マイPC（ホーム）を開く",
                ["Settings_OpenSpecificFolder"] = "特定のフォルダーを開く...",
                ["Settings_CustomPath"] = "カスタムパス",
                ["Settings_FavoritesTree"] = "お気に入りツリー表示",
                ["Settings_FavoritesTreeDesc"] = "お気に入りをツリー表示（サブフォルダー参照可能）",
                ["Settings_SystemTray"] = "システムトレイ",
                ["Settings_SystemTrayDesc"] = "閉じるボタンでトレイに最小化",
                ["Settings_WindowPosition"] = "ウィンドウ位置を記憶",
                ["Settings_WindowPositionDesc"] = "終了時にウィンドウの位置とサイズを保存し、次回起動時に復元",
                ["Settings_AppTheme"] = "アプリテーマ",
                ["Settings_ThemeDesc"] = "アプリ全体のカラーテーマ設定",
                ["Settings_System"] = "システム",
                ["Settings_Light"] = "ライト",
                ["Settings_Dark"] = "ダーク",
                ["Settings_ProThemes"] = "Pro専用テーマ",
                ["Settings_ProThemesDesc"] = "開発者向けプレミアムテーマのロック解除",
                ["Settings_MidnightGoldDesc"] = "ブラック＆ゴールドアクセント",
                ["Settings_CyberpunkDesc"] = "ネオンパープル＆シアン",
                ["Settings_NordicDesc"] = "落ち着いたブルーグレー",
                ["Settings_UpgradeProThemes"] = "Proにアップグレードしてロック解除",
                ["Settings_LayoutDensity"] = "レイアウト密度",
                ["Settings_LayoutDensityDesc"] = "リスト項目の間隔調整",
                ["Settings_IconPack"] = "アイコンパック",
                ["Settings_IconPackDesc"] = "ファイル/フォルダーアイコンスタイル",
                ["Settings_IconPackRestart"] = "アイコンパック変更にはアプリの再起動が必要です",
                ["Settings_Font"] = "フォント",
                ["Settings_FontDesc"] = "ファイル名表示用フォント",
                ["Settings_ViewOptions"] = "表示オプション",
                ["Settings_ViewOptionsDesc"] = "ファイル表示設定",
                ["Settings_ShowHidden"] = "隠し項目を表示",
                ["Settings_ShowExtensions"] = "ファイル拡張子を表示",
                ["Settings_CheckboxSelection"] = "チェックボックスで項目を選択",
                ["Settings_MillerBehavior"] = "カラムの動作",
                ["Settings_MillerBehaviorDesc"] = "フォルダーの開き方",
                ["Settings_SingleClick"] = "シングルクリック",
                ["Settings_DoubleClick"] = "ダブルクリック",
                ["Settings_Thumbnails"] = "画像プレビュー（サムネイル）",
                ["Settings_ThumbnailsDesc"] = "パフォーマンスのためネットワークドライブではサムネイルを無効化",
                ["Settings_QuickLook"] = "Quick Look（プレビュー）",
                ["Settings_QuickLookDesc"] = "Spaceキーでファイルをすばやくプレビュー",
                ["Settings_DeleteConfirm"] = "削除確認ダイアログ",
                ["Settings_DeleteConfirmDesc"] = "ファイル削除前に確認を求めます",
                ["Settings_UndoHistory"] = "元に戻す履歴の保持",
                ["Settings_UndoHistoryDesc"] = "保持する元に戻す操作の数（0 = 無効）",
                ["Settings_Developer"] = "開発者",
                ["Settings_TerminalApp"] = "デフォルトターミナルアプリ",
                ["Settings_TerminalAppDesc"] = "Ctrl+`または右クリック「ターミナルを開く」で起動",
                ["Settings_SmartRun"] = "Smart Run（クイックコマンド）",
                ["Settings_SmartRunDesc"] = "よく使うコマンドのショートカット管理",
                ["Settings_AddShortcut"] = "ショートカットを追加",
                ["Settings_ShellExtras"] = "Windowsシェル拡張メニュー",
                ["Settings_ShellExtrasDesc"] = "右クリックメニューに共有、送る、ライブラリ、スタートにピン留め等を表示",
                ["Settings_DeveloperMenu"] = "開発者コンテキストメニュー",
                ["Settings_DeveloperMenuDesc"] = "右クリックメニューにGit、Visual Studio、VS Code等を表示",
                ["Settings_CopilotMenu"] = "Copilotメニュー",
                ["Settings_CopilotMenuDesc"] = "右クリックメニューにMicrosoft Copilot項目を表示",
                ["Settings_ContextMenu"] = "コンテキストメニュー",
                ["Settings_ContextMenuDesc"] = "Windowsの右クリックメニューに「Open in Span」を追加",
                ["Settings_CheckUpdate"] = "更新を確認",
                ["Settings_Checking"] = "確認中...",
                ["Settings_UpToDate"] = "最新バージョンです",
                ["Settings_UpgradePro"] = "Proにアップグレード",
                ["Settings_UpgradeProDesc"] = "Proバッジを取得して開発者を応援しましょう。",
                ["Settings_UnlockThemes"] = "プレミアムテーマのロック解除",
                ["Settings_UnlimitedSmartRun"] = "Smart Run無制限ショートカット",
                ["Settings_AllPremiumFeatures"] = "今後のすべてのプレミアム機能",
                ["Settings_BuyMeCoffee"] = "Buy me a coffee",
                ["Settings_BuyMeCoffeeDesc"] = "Spanを気に入っていただけたら、コーヒー1杯で応援してください！",
                ["Settings_Links"] = "関連リンク",
                ["Settings_GitHub"] = "GitHubリポジトリ",
                ["Settings_BugReport"] = "バグ報告",
                ["Settings_Privacy"] = "プライバシーポリシー",
                ["Settings_EvalCopy"] = "評価版（未登録）",
            }
        };

        public LocalizationService()
        {
            var culture = CultureInfo.CurrentUICulture;
            _language = ResolveLanguage(culture.TwoLetterISOLanguageName);
            ApplyPrimaryLanguageOverride(_language);
        }

        public string Language
        {
            get => _language;
            set
            {
                var resolved = ResolveLanguage(value);
                if (_language != resolved)
                {
                    _language = resolved;
                    ApplyPrimaryLanguageOverride(resolved);
                    LanguageChanged?.Invoke();
                }
            }
        }

        public IReadOnlyList<string> AvailableLanguages => new[] { "en", "ko", "ja" };

        public string Get(string key)
        {
            if (Strings.TryGetValue(_language, out var dict) && dict.TryGetValue(key, out var value))
                return value;
            if (Strings["en"].TryGetValue(key, out var fallback))
                return fallback;
            return key;
        }

        private static string ResolveLanguage(string lang)
        {
            return lang switch
            {
                "ko" => "ko",
                "ja" => "ja",
                _ => "en"
            };
        }

        /// <summary>
        /// Set Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride
        /// so that system dialogs (e.g. Properties) called from this app
        /// respect the app's configured language instead of defaulting to English.
        /// </summary>
        private static void ApplyPrimaryLanguageOverride(string lang)
        {
            try
            {
                ApplicationLanguages.PrimaryLanguageOverride = lang switch
                {
                    "ko" => "ko-KR",
                    "ja" => "ja-JP",
                    _ => "" // empty = use system default
                };
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[LocalizationService] PrimaryLanguageOverride failed: {ex.Message}");
            }
        }
    }
}
