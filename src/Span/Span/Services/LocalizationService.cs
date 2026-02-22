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
                ["MillerColumns"] = "Miller Columns",
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
                ["MillerColumns"] = "Miller Columns",
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
                ["MillerColumns"] = "Miller Columns",
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
