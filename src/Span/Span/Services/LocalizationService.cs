using System;
using System.Collections.Generic;
using System.Globalization;

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
                ["OpenInExplorer"] = "Open in Explorer",
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

                // Shell extensions section
                ["ShellExtensions"] = "More options",
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
                ["OpenInExplorer"] = "\ud30c\uc77c \ud0d0\uc0c9\uae30\uc5d0\uc11c \uc5f4\uae30",
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

                // Shell extensions section
                ["ShellExtensions"] = "\ucd94\uac00 \uc635\uc158",
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
                ["OpenInExplorer"] = "\u30a8\u30af\u30b9\u30d7\u30ed\u30fc\u30e9\u30fc\u3067\u958b\u304f",
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
            }
        };

        public LocalizationService()
        {
            var culture = CultureInfo.CurrentUICulture;
            _language = ResolveLanguage(culture.TwoLetterISOLanguageName);
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
    }
}
