using System.Collections.Generic;

namespace Span.Helpers
{
    public static class RemixIconHelper
    {
        public static string GetGlyph(string iconName)
        {
            if (string.IsNullOrEmpty(iconName)) return "\uECE0"; // Default icon

            // Remove 'ri-' prefix if present
            var key = iconName.Replace("ri-", "").ToLowerInvariant();

            if (_icons.TryGetValue(key, out var glyph))
            {
                return glyph;
            }

            // Fallback: if it starts with \u, assume it's already unicode
            if (iconName.StartsWith("\\u") || iconName.StartsWith("&#x"))
            {
                return iconName;
            }

            return "\uECE0"; // Default icon
        }

        private static readonly Dictionary<string, string> _icons = new()
        {
            { "folder-fill", "\uED61" },

            { "file-text-line", "\uED0F" },
            { "file-list-3-fill", "\uECEE" },
            { "code-s-slash-fill", "\uEBAC" },
            { "file-settings-fill", "\uED06" },
            { "window-fill", "\uEF57" }, // microsoft-fill (Windows logo)
            { "file-shield-2-fill", "\uED08" },
            { "html5-fill", "\uEE40" },
            { "css3-fill", "\uEC03" },
            { "file-code-fill", "\uECD0" },
            { "javascript-fill", "\uF33A" }, // Verified \f33a
            { "reactjs-fill", "\uF057" }, // Verified \f057
            { "vuejs-fill", "\uF2A5" }, // Verified \f2a5 (was \f263 which is user-heart-line)
            { "java-fill", "\uECD0" }, // file-code-fill fallback (java not found in list)
            { "archive-fill", "\uED1E" }, // file-zip-fill
            { "file-shield-fill", "\uED0A" },
            { "code-box-fill", "\uEBA6" },
            { "code-box-line", "\uEBA7" },
            { "gift-fill", "\uEDD2" },
            { "apple-fill", "\uEA3F" },
            { "braces-fill", "\uEA7C" },
            { "file-settings-line", "\uED07" },
            { "settings-3-fill", "\uF0E5" }, // Verified \f0e5
            { "list-settings-fill", "\uEF5E" },
            { "database-2-fill", "\uEC15" }, // Verified \ec15 (was \ec1a which is delete-back-2)
            { "database-fill", "\uEC1C" },
            { "file-list-2-fill", "\uECEC" },
            { "file-text-fill", "\uED0E" },
            { "markdown-fill", "\uEF1D" }, // Verified \ef1d (was \ef21 which is mastercard-fill)
            { "windows-fill", "\uEF57" }, // microsoft-fill (Windows logo)
            { "terminal-box-fill", "\uF1F5" }, // Verified \f1f5
            { "terminal-window-fill", "\uF1F9" }, // Verified \f1f9
            { "terminal-box-line", "\uF1F6" }, // Verified \f1f6
            { "android-fill", "\uEA35" },
            { "folder-zip-fill", "\uED1E" },
            { "git-branch-fill", "\uEEBA" },
            { "git-merge-fill", "\uEEBD" },
            { "git-commit-fill", "\uEEBB" },
            { "image-fill", "\uEE4A" },
            { "image-2-fill", "\uEE4B" },
            { "image-circle-fill", "\uEE4C" },
            { "image-line", "\uEE4D" },
            { "apps-2-fill", "\uEA41" },
            { "shapes-fill", "\uF123" },
            { "brush-2-fill", "\uEA9D" },
            { "pen-nib-fill", "\uEF01" },
            { "file-pdf-line", "\uECFD" },
            { "palette-fill", "\uEFDC" },
            { "contrast-drop-fill", "\uEBD7" },
            { "music-fill", "\uEF84" },
            { "file-music-fill", "\uECF6" },
            { "music-2-fill", "\uEF82" },
            { "headphone-fill", "\uEE04" },
            { "movie-fill", "\uEF80" },
            { "film-fill", "\uECE4" },
            { "video-fill", "\uF26C" },
            { "file-pdf-fill", "\uECFC" },
            { "file-word-fill", "\uED1C" },
            { "file-excel-fill", "\uECDE" },
            { "table-fill", "\uF1B2" },
            { "file-chart-fill", "\uECCC" },
            { "file-ppt-fill", "\uED00" },
            { "slideshow-fill", "\uF157" },
            { "file-zip-fill", "\uED1E" },
            { "archive-line", "\uEA48" },
            { "disc-fill", "\uECA4" },
            { "font-size", "\uEF44" },
            { "font-sans", "\uEF45" },
            { "font-mono", "\uEF46" },
            { "links-fill", "\uEEF1" },
            { "global-line", "\uEDD4" },
            { "settings-5-fill", "\uF0E9" }, // Verified \f0e9
            { "settings-4-fill", "\uF0E7" },
            { "file-reduce-fill", "\uED02" },
            { "drive-fill", "\uEC65" }, // Verified \ec65

            { "file-copy-2-fill", "\uECD2" },
            { "file-download-fill", "\uECD8" },
            { "file-fill", "\uECE0" },
            { "folder-3-fill", "\uED53" }
        };
    }
}
