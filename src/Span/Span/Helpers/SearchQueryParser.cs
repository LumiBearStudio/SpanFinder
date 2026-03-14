using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Span.Models;

namespace Span.Helpers
{
    /// <summary>
    /// Parses search query strings with Windows Explorer-style Advanced Query Syntax.
    ///
    /// Supported syntax:
    ///   kind:image, kind:document, kind:video, kind:audio, kind:archive, kind:code, kind:exe, kind:font
    ///   size:>1MB, size:&lt;100KB, size:>500B, size:large, size:small, size:empty
    ///   date:today, date:yesterday, date:thisweek, date:thismonth, date:thisyear, date:>2024-01-01
    ///   ext:.pdf, ext:.txt, ext:pdf
    ///   Plain text = name filter (case-insensitive contains)
    ///   Multiple terms combined with AND logic.
    /// </summary>
    public static class SearchQueryParser
    {
        // Extension sets for each FileKind
        private static readonly Dictionary<FileKind, HashSet<string>> KindExtensions = new()
        {
            [FileKind.Image] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
                ".svg", ".raw", ".cr2", ".nef", ".heic", ".heif", ".avif", ".jxl"
            },
            [FileKind.Video] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v",
                ".mpg", ".mpeg", ".3gp", ".ogv", ".ts"
            },
            [FileKind.Audio] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".opus",
                ".aiff", ".ape", ".alac"
            },
            [FileKind.Document] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
                ".txt", ".rtf", ".odt", ".ods", ".odp", ".csv", ".md", ".epub"
            },
            [FileKind.Archive] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".zst",
                ".cab", ".dmg", ".tgz"
            },
            [FileKind.Code] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".cs", ".js", ".ts", ".py", ".java", ".cpp", ".c", ".h", ".hpp",
                ".go", ".rs", ".rb", ".php", ".swift", ".kt", ".scala", ".lua",
                ".html", ".htm", ".css", ".scss", ".less", ".json", ".xml", ".yaml",
                ".yml", ".toml", ".ini", ".cfg", ".sh", ".bat", ".ps1", ".sql"
            },
            [FileKind.Executable] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".exe", ".msi", ".dll", ".sys", ".com", ".bat", ".cmd", ".ps1",
                ".sh", ".app", ".apk", ".deb", ".rpm"
            },
            [FileKind.Font] = new(StringComparer.OrdinalIgnoreCase)
            {
                ".ttf", ".otf", ".woff", ".woff2", ".eot", ".fon"
            }
        };

        // Kind name aliases
        private static readonly Dictionary<string, FileKind> KindAliases = new(StringComparer.OrdinalIgnoreCase)
        {
            // Image
            ["image"] = FileKind.Image,
            ["images"] = FileKind.Image,
            ["photo"] = FileKind.Image,
            ["photos"] = FileKind.Image,
            ["picture"] = FileKind.Image,
            ["pictures"] = FileKind.Image,
            ["pic"] = FileKind.Image,
            ["img"] = FileKind.Image,
            // Video
            ["video"] = FileKind.Video,
            ["videos"] = FileKind.Video,
            ["movie"] = FileKind.Video,
            ["movies"] = FileKind.Video,
            ["film"] = FileKind.Video,
            // Audio
            ["audio"] = FileKind.Audio,
            ["music"] = FileKind.Audio,
            ["sound"] = FileKind.Audio,
            ["sounds"] = FileKind.Audio,
            ["song"] = FileKind.Audio,
            ["songs"] = FileKind.Audio,
            // Document
            ["document"] = FileKind.Document,
            ["documents"] = FileKind.Document,
            ["doc"] = FileKind.Document,
            ["docs"] = FileKind.Document,
            ["text"] = FileKind.Document,
            // Archive
            ["archive"] = FileKind.Archive,
            ["archives"] = FileKind.Archive,
            ["zip"] = FileKind.Archive,
            ["compressed"] = FileKind.Archive,
            // Code
            ["code"] = FileKind.Code,
            ["source"] = FileKind.Code,
            ["script"] = FileKind.Code,
            ["scripts"] = FileKind.Code,
            ["program"] = FileKind.Code,
            // Executable
            ["executable"] = FileKind.Executable,
            ["executables"] = FileKind.Executable,
            ["exe"] = FileKind.Executable,
            ["app"] = FileKind.Executable,
            ["application"] = FileKind.Executable,
            // Font
            ["font"] = FileKind.Font,
            ["fonts"] = FileKind.Font
        };

        // Regex for size filter: size:>1MB, size:<100KB, size:>=500B, size:1GB
        private static readonly Regex SizePattern = new(
            @"^(>=?|<=?|=)?(\d+(?:\.\d+)?)\s*(b|kb|mb|gb|tb)?$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Regex for date comparison: date:>2024-01-01, date:<2024-12-31
        private static readonly Regex DateComparePattern = new(
            @"^(>=?|<=?|=)?(\d{4}-\d{2}-\d{2})$",
            RegexOptions.Compiled);

        /// <summary>
        /// Parse a search query string into a structured SearchQuery object.
        /// </summary>
        public static SearchQuery Parse(string input)
        {
            var query = new SearchQuery();
            if (string.IsNullOrWhiteSpace(input))
                return query;

            var nameTokens = new List<string>();

            // Tokenize: split by spaces but respect quoted strings
            var tokens = Tokenize(input);

            foreach (var token in tokens)
            {
                if (TryParseKind(token, out var kind))
                {
                    query.KindFilter = kind;
                }
                else if (TryParseSize(token, out var sizeFilter))
                {
                    query.SizeFilter = sizeFilter;
                }
                else if (TryParseDate(token, out var dateFilter))
                {
                    query.DateFilter = dateFilter;
                }
                else if (TryParseExtension(token, out var ext))
                {
                    query.ExtensionFilter = ext;
                }
                else
                {
                    // Plain text token -> name filter
                    nameTokens.Add(token);
                }
            }

            if (nameTokens.Count > 0)
            {
                var nameFilter = string.Join(" ", nameTokens);
                query.NameFilter = nameFilter;

                // 와일드카드 감지: * 또는 ? 포함 시 Regex로 변환 (전체 이름 매칭)
                if (nameFilter.Contains('*') || nameFilter.Contains('?'))
                {
                    query.NameRegex = WildcardToRegex(nameFilter);
                }
            }

            return query;
        }

        /// <summary>
        /// Get the set of extensions for a given FileKind.
        /// </summary>
        public static HashSet<string> GetExtensionsForKind(FileKind kind)
        {
            return KindExtensions.TryGetValue(kind, out var exts) ? exts : new HashSet<string>();
        }

        /// <summary>
        /// Simple tokenizer: splits on whitespace, keeps quoted strings together.
        /// </summary>
        private static List<string> Tokenize(string input)
        {
            var tokens = new List<string>();
            int i = 0;

            while (i < input.Length)
            {
                // Skip whitespace
                while (i < input.Length && char.IsWhiteSpace(input[i]))
                    i++;

                if (i >= input.Length) break;

                // Quoted string
                if (input[i] == '"' || input[i] == '\'')
                {
                    char quote = input[i];
                    i++;
                    int start = i;
                    while (i < input.Length && input[i] != quote)
                        i++;
                    tokens.Add(input.Substring(start, i - start));
                    if (i < input.Length) i++; // skip closing quote
                }
                else
                {
                    // Regular token
                    int start = i;
                    while (i < input.Length && !char.IsWhiteSpace(input[i]))
                        i++;
                    tokens.Add(input.Substring(start, i - start));
                }
            }

            return tokens;
        }

        private static bool TryParseKind(string token, out FileKind kind)
        {
            kind = default;

            if (!token.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
                return false;

            var value = token.Substring(5).Trim();
            if (string.IsNullOrEmpty(value))
                return false;

            return KindAliases.TryGetValue(value, out kind);
        }

        private static bool TryParseSize(string token, out (CompareOp, long)? sizeFilter)
        {
            sizeFilter = null;

            if (!token.StartsWith("size:", StringComparison.OrdinalIgnoreCase))
                return false;

            var value = token.Substring(5).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(value))
                return false;

            // Named size presets
            switch (value)
            {
                case "empty":
                    sizeFilter = (CompareOp.Equals, 0L);
                    return true;
                case "tiny":
                    // < 16 KB
                    sizeFilter = (CompareOp.LessThan, 16L * 1024);
                    return true;
                case "small":
                    // < 1 MB
                    sizeFilter = (CompareOp.LessThan, 1L * 1024 * 1024);
                    return true;
                case "medium":
                    // 1 MB - 128 MB (use GreaterOrEqual as lower bound;
                    // for simplicity, just filter >= 1MB)
                    sizeFilter = (CompareOp.GreaterOrEqual, 1L * 1024 * 1024);
                    return true;
                case "large":
                    // > 128 MB
                    sizeFilter = (CompareOp.GreaterThan, 128L * 1024 * 1024);
                    return true;
                case "huge":
                case "gigantic":
                    // > 1 GB
                    sizeFilter = (CompareOp.GreaterThan, 1L * 1024 * 1024 * 1024);
                    return true;
            }

            // Numeric size pattern: size:>1MB, size:<100KB, size:500B
            var match = SizePattern.Match(value);
            if (!match.Success)
                return false;

            var opStr = match.Groups[1].Value;
            var numStr = match.Groups[2].Value;
            var unitStr = match.Groups[3].Value;

            if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var num))
                return false;

            // Convert to bytes
            long multiplier = string.IsNullOrEmpty(unitStr) ? 1 : unitStr.ToLowerInvariant() switch
            {
                "b" => 1L,
                "kb" => 1024L,
                "mb" => 1024L * 1024,
                "gb" => 1024L * 1024 * 1024,
                "tb" => 1024L * 1024 * 1024 * 1024,
                _ => 1L
            };

            long bytes = (long)(num * multiplier);

            var op = ParseOp(opStr);
            sizeFilter = (op, bytes);
            return true;
        }

        private static bool TryParseDate(string token, out (CompareOp, DateTime)? dateFilter)
        {
            dateFilter = null;

            if (!token.StartsWith("date:", StringComparison.OrdinalIgnoreCase))
                return false;

            var value = token.Substring(5).Trim();
            if (string.IsNullOrEmpty(value))
                return false;

            var now = DateTime.Now;
            var today = now.Date;

            // Named date presets
            switch (value.ToLowerInvariant())
            {
                case "today":
                    dateFilter = (CompareOp.GreaterOrEqual, today);
                    return true;
                case "yesterday":
                    dateFilter = (CompareOp.GreaterOrEqual, today.AddDays(-1));
                    return true;
                case "thisweek":
                    // Start of current week (Monday)
                    int daysSinceMonday = ((int)today.DayOfWeek + 6) % 7;
                    dateFilter = (CompareOp.GreaterOrEqual, today.AddDays(-daysSinceMonday));
                    return true;
                case "lastweek":
                    int daysSinceMonday2 = ((int)today.DayOfWeek + 6) % 7;
                    dateFilter = (CompareOp.GreaterOrEqual, today.AddDays(-daysSinceMonday2 - 7));
                    return true;
                case "thismonth":
                    dateFilter = (CompareOp.GreaterOrEqual, new DateTime(today.Year, today.Month, 1));
                    return true;
                case "lastmonth":
                    var lastMonth = today.AddMonths(-1);
                    dateFilter = (CompareOp.GreaterOrEqual, new DateTime(lastMonth.Year, lastMonth.Month, 1));
                    return true;
                case "thisyear":
                    dateFilter = (CompareOp.GreaterOrEqual, new DateTime(today.Year, 1, 1));
                    return true;
                case "lastyear":
                    dateFilter = (CompareOp.GreaterOrEqual, new DateTime(today.Year - 1, 1, 1));
                    return true;
            }

            // Date comparison: date:>2024-01-01, date:<2024-12-31
            var match = DateComparePattern.Match(value);
            if (match.Success)
            {
                var opStr = match.Groups[1].Value;
                var dateStr = match.Groups[2].Value;

                if (DateTime.TryParseExact(dateStr, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    var op = ParseOp(opStr);
                    dateFilter = (op, date);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseExtension(string token, out string? ext)
        {
            ext = null;

            if (!token.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
                return false;

            var value = token.Substring(4).Trim();
            if (string.IsNullOrEmpty(value))
                return false;

            // 다중 확장자 지원: ext:jpg;png;gif → ".jpg;.png;.gif"
            if (value.Contains(';'))
            {
                var parts = value.Split(';', StringSplitOptions.RemoveEmptyEntries);
                var normalized = new List<string>();
                foreach (var p in parts)
                {
                    var trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        normalized.Add(trimmed.StartsWith(".") ? trimmed : "." + trimmed);
                }
                if (normalized.Count == 0) return false;
                ext = string.Join(";", normalized);
                return true;
            }

            // 단일 확장자: ensure leading dot
            ext = value.StartsWith(".") ? value : "." + value;
            return true;
        }

        private static CompareOp ParseOp(string opStr)
        {
            return opStr switch
            {
                ">" => CompareOp.GreaterThan,
                "<" => CompareOp.LessThan,
                ">=" => CompareOp.GreaterOrEqual,
                "<=" => CompareOp.LessOrEqual,
                "=" => CompareOp.Equals,
                _ => CompareOp.GreaterOrEqual // default for no operator (e.g., size:1MB means >= 1MB)
            };
        }

        /// <summary>
        /// 와일드카드 패턴을 정규식으로 변환.
        /// * → .* (0개 이상 문자), ? → . (정확히 1개 문자)
        /// 전체 이름 매칭을 위해 ^...$ 앵커 적용.
        /// </summary>
        private static Regex WildcardToRegex(string pattern)
        {
            // Regex 특수문자 이스케이프 후 와일드카드만 복원
            var escaped = Regex.Escape(pattern);
            escaped = escaped.Replace("\\*", ".*").Replace("\\?", ".");
            return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        }
    }
}
