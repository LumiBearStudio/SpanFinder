using System.Collections.Generic;

namespace Span.Helpers
{
    public static class TablerIconHelper
    {
        public static string GetGlyph(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return "\ueaa4"; // Default file icon

            var key = iconName.ToLowerInvariant();

            if (_icons.TryGetValue(key, out var glyph))
            {
                return glyph;
            }

            // Fallback: if it starts with \u, assume it's already unicode
            if (iconName.StartsWith("\\u") || iconName.StartsWith("&#x"))
            {
                return iconName;
            }

            return "\ueaa4"; // Default file icon
        }

        private static readonly Dictionary<string, string> _icons = new()
        {
            // Folders
            { "folder", "\ueaad" },
            { "folder-filled", "\uf749" },
            { "folder-open", "\ufaf7" },

            // Files - Generic
            { "file", "\ueaa4" },
            { "file-filled", "\uf747" },
            { "file-text", "\ueaa2" },
            { "file-code", "\uebd0" },
            { "file-music", "\uea9f" },
            { "file-certificate", "\ued4d" },
            { "file-spreadsheet", "\uf03e" },
            { "file-3d", "\uf032" },

            // Files - Typed
            { "file-type-pdf", "\ufb10" },
            { "file-type-doc", "\ufb0a" },
            { "file-type-docx", "\ufb0b" },
            { "file-type-xls", "\ufb1b" },
            { "file-type-ppt", "\ufb13" },
            { "file-type-ts", "\ufb17" },
            { "file-type-js", "\ufb0e" },
            { "file-type-jsx", "\ufb0f" },
            { "file-type-html", "\ufb0c" },
            { "file-type-css", "\ufb08" },
            { "file-type-svg", "\ufb16" },
            { "file-type-xml", "\ufb1c" },
            { "file-type-zip", "\ufb1d" },
            { "file-type-vue", "\ufb1a" },
            { "file-type-php", "\ufb11" },
            { "file-type-rs", "\ufb14" },
            { "file-type-sql", "\ufb15" },
            { "file-type-bmp", "\ufb07" },
            { "file-zip", "\ued4e" },

            // Code
            { "code", "\uea77" },
            { "source-code", "\uf4a2" },
            { "braces", "\uebcc" },
            { "brackets", "\uebcd" },
            { "code-dots", "\uf61a" },
            { "code-circle", "\uf4ff" },
            { "script", "\uf2da" },

            // Terminal
            { "terminal", "\uebdc" },
            { "terminal-2", "\uebef" },

            // Image / Media
            { "photo", "\ueb0a" },
            { "photo-filled", "\ufa4a" },
            { "music", "\ueafc" },
            { "headphones", "\ueabd" },
            { "volume", "\ueb51" },
            { "video", "\ued22" },
            { "movie", "\ueafa" },
            { "player-play", "\ued46" },

            // Data / Config
            { "database", "\uea88" },
            { "server", "\ueb1f" },
            { "settings", "\ueb20" },
            { "adjustments", "\uea03" },
            { "tool", "\ueb40" },

            // Git
            { "git-branch", "\ueab2" },
            { "git-commit", "\ueab3" },
            { "git-merge", "\ueab5" },
            { "git-compare", "\ueab4" },

            // Brands
            { "brand-windows", "\uecd8" },
            { "brand-android", "\uec16" },
            { "brand-apple", "\uec17" },
            { "brand-javascript", "\uef0c" },
            { "brand-html5", "\ued6c" },
            { "brand-css3", "\ued6b" },
            { "brand-react", "\uf34c" },
            { "brand-python", "\ued01" },
            { "brand-git", "\uef6f" },
            { "brand-vue", "\uf0e0" },
            { "brand-svelte", "\uf0df" },
            { "brand-rust", "\ufa53" },
            { "brand-golang", "\uf78d" },
            { "brand-php", "\uef72" },
            { "brand-kotlin", "\ued6d" },
            { "brand-swift", "\ufa55" },
            { "brand-visual-studio", "\uef76" },
            { "brand-vscode", "\uf3a0" },

            // UI / Misc
            { "app-window", "\uefe6" },
            { "link", "\ueade" },
            { "world", "\ueb54" },
            { "palette", "\ueb01" },
            { "paint", "\ueb00" },
            { "pencil", "\ueb04" },
            { "brush", "\uebb8" },
            { "disc", "\uea90" },
            { "device-floppy", "\ueb62" },
            { "copy", "\uea7a" },
            { "download", "\uea96" },
            { "shield", "\ueb24" },
            { "shield-check", "\ueb22" },
            { "external-link", "\uea99" },
            { "list", "\ueb6b" },
            { "markdown", "\uec41" },
            { "hash", "\uf032" },
            { "typography", "\uebc5" },
            { "letter-case", "\ueea5" },
            { "table", "\ueba1" },
            { "chart-bar", "\uea59" },
            { "presentation", "\ueb70" },
            { "package", "\ueaff" },
            { "gift", "\ueb68" },
            { "share", "\ueb21" },
            { "archive", "\uea0b" },
        };
    }
}
