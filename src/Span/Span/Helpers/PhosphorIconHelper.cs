using System.Collections.Generic;

namespace Span.Helpers
{
    public static class PhosphorIconHelper
    {
        public static string GetGlyph(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return "\ue230"; // Default file icon

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

            return "\ue230"; // Default file icon
        }

        private static readonly Dictionary<string, string> _icons = new()
        {
            // Folders
            { "folder", "\ue24a" },
            { "folder-simple", "\ue25a" },
            { "folder-notch-open", "\ue256" },

            // Files - Generic
            { "file", "\ue230" },
            { "file-text", "\ue23a" },
            { "file-code", "\ue914" },

            // Files - Language-specific
            { "file-css", "\ueb34" },
            { "file-html", "\ueb38" },
            { "file-js", "\ueb24" },
            { "file-jsx", "\ueb3a" },
            { "file-ts", "\ueb26" },
            { "file-py", "\ueb2c" },
            { "file-c", "\ueb32" },
            { "file-c-sharp", "\ueb30" },
            { "file-cpp", "\ueb2e" },
            { "file-rs", "\ueb28" },
            { "file-sql", "\ued4e" },
            { "file-vue", "\ueb3e" },
            { "file-svg", "\ued08" },

            // Files - Media
            { "file-image", "\uea24" },
            { "file-jpg", "\ueb1a" },
            { "file-png", "\ueb18" },
            { "file-audio", "\uea20" },
            { "file-video", "\uea22" },
            { "file-archive", "\ueb2a" },

            // Files - Office / Document
            { "file-pdf", "\ue702" },
            { "file-doc", "\ueb1e" },
            { "file-xls", "\ueb22" },
            { "file-ppt", "\ueb20" },
            { "file-zip", "\ue958" },

            // Code
            { "code", "\ue1bc" },
            { "code-simple", "\ue1be" },
            { "code-block", "\ueafe" },

            // Terminal
            { "terminal", "\ue47e" },
            { "terminal-window", "\ueae8" },

            // Image / Media
            { "image", "\ue2ca" },
            { "image-square", "\ue2cc" },
            { "music-notes", "\ue340" },
            { "music-note", "\ue33c" },
            { "headphones", "\ue2a6" },
            { "speaker-high", "\ue44a" },
            { "video-camera", "\ue4da" },
            { "film-strip", "\ue792" },
            { "monitor-play", "\ue58c" },

            // Data / Config
            { "database", "\ue1de" },
            { "hard-drives", "\ue2a0" },
            { "gear", "\ue270" },
            { "gear-six", "\ue272" },
            { "sliders", "\ue432" },
            { "wrench", "\ue5d4" },

            // Git
            { "git-branch", "\ue278" },
            { "git-commit", "\ue27a" },
            { "git-merge", "\ue280" },
            { "git-diff", "\ue27c" },

            // Brands / Logos
            { "windows-logo", "\ue692" },
            { "android-logo", "\ue008" },
            { "apple-logo", "\ue516" },
            { "markdown-logo", "\ue508" },

            // UI / Misc
            { "app-window", "\ue5da" },
            { "link", "\ue2e2" },
            { "link-simple", "\ue2e6" },
            { "globe", "\ue288" },
            { "globe-simple", "\ue28e" },
            { "palette", "\ue6c8" },
            { "paint-brush", "\ue6f0" },
            { "pen-nib", "\ue3ac" },
            { "paint-roller", "\ue6f4" },
            { "disc", "\ue564" },
            { "floppy-disk", "\ue248" },
            { "copy", "\ue1ca" },
            { "download", "\ue20a" },
            { "shield", "\ue40a" },
            { "shield-check", "\ue40c" },
            { "list-bullets", "\ue2f2" },
            { "brackets-curly", "\ue860" },
            { "brackets-square", "\ue85e" },
            { "hash", "\ue2a2" },
            { "text-t", "\ue48a" },
            { "text-aa", "\ue6ee" },
            { "table", "\ue476" },
            { "slideshow", "\ued32" },
            { "chart-bar", "\ue150" },
            { "note-pencil", "\ue34c" },
            { "package", "\ue390" },
            { "gift", "\ue276" },
            { "share", "\ue406" },
            { "stack", "\ue466" },
            { "archive", "\ue00c" },
            { "archive-box", "\ue00e" },
        };
    }
}
