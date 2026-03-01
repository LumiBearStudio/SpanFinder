using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Span.Models;
using Span.ViewModels;

namespace Span.Helpers
{
    /// <summary>
    /// Applies a parsed SearchQuery to a collection of FileSystemViewModel items.
    /// All active filters are combined with AND logic.
    /// </summary>
    public static class SearchFilter
    {
        /// <summary>
        /// Filter items based on the parsed search query.
        /// Returns items that match ALL specified filters (AND logic).
        /// </summary>
        public static List<FileSystemViewModel> Apply(
            SearchQuery query,
            IEnumerable<FileSystemViewModel> items)
        {
            if (query == null || query.IsEmpty)
                return items.ToList();

            return items.Where(item => Matches(query, item)).ToList();
        }

        /// <summary>
        /// Find the first matching item (for search-and-select behavior).
        /// </summary>
        public static FileSystemViewModel? FindFirst(
            SearchQuery query,
            IEnumerable<FileSystemViewModel> items)
        {
            if (query == null || query.IsEmpty)
                return null;

            return items.FirstOrDefault(item => Matches(query, item));
        }

        /// <summary>
        /// Check if a single item matches all filters in the query.
        /// </summary>
        public static bool Matches(SearchQuery query, FileSystemViewModel item)
        {
            // Name filter: wildcard → Regex full match, plain text → contains
            if (!string.IsNullOrEmpty(query.NameFilter))
            {
                if (query.NameRegex != null)
                {
                    // 와일드카드 패턴: 전체 이름 매칭 (*.exe, report*, test?.doc)
                    if (!query.NameRegex.IsMatch(item.Name))
                        return false;
                }
                else
                {
                    // 일반 텍스트: 부분 일치 (대소문자 무시)
                    if (!item.Name.Contains(query.NameFilter, StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }

            // Kind filter: match by file extension category
            if (query.KindFilter.HasValue)
            {
                if (!MatchesKind(query.KindFilter.Value, item))
                    return false;
            }

            // Size filter: compare file size
            if (query.SizeFilter.HasValue)
            {
                if (!MatchesSize(query.SizeFilter.Value.Op, query.SizeFilter.Value.Bytes, item))
                    return false;
            }

            // Date filter: compare modification date
            if (query.DateFilter.HasValue)
            {
                if (!MatchesDate(query.DateFilter.Value.Op, query.DateFilter.Value.Date, item))
                    return false;
            }

            // Extension filter: exact extension match
            if (!string.IsNullOrEmpty(query.ExtensionFilter))
            {
                if (!MatchesExtension(query.ExtensionFilter, item))
                    return false;
            }

            return true;
        }

        private static bool MatchesKind(FileKind kind, FileSystemViewModel item)
        {
            // Folders never match a kind filter (kind filters are for files)
            if (item is FolderViewModel)
                return false;

            var ext = Path.GetExtension(item.Name);
            if (string.IsNullOrEmpty(ext))
                return false;

            var kindExtensions = SearchQueryParser.GetExtensionsForKind(kind);
            return kindExtensions.Contains(ext);
        }

        private static bool MatchesSize(CompareOp op, long targetBytes, FileSystemViewModel item)
        {
            // Folders don't have a meaningful size for this filter
            if (item is FolderViewModel)
                return false;

            long itemSize = item.SizeValue;
            return Compare(op, itemSize, targetBytes);
        }

        private static bool MatchesDate(CompareOp op, DateTime targetDate, FileSystemViewModel item)
        {
            var itemDate = item.DateModifiedValue;
            if (itemDate == DateTime.MinValue)
                return false;

            // Compare date portions for cleaner semantics
            return CompareDates(op, itemDate, targetDate);
        }

        private static bool MatchesExtension(string extension, FileSystemViewModel item)
        {
            // Folders don't have extensions
            if (item is FolderViewModel)
                return false;

            var itemExt = Path.GetExtension(item.Name);

            // 다중 확장자: ext:jpg;png;gif → ".jpg;.png;.gif"
            if (extension.Contains(';'))
            {
                var exts = extension.Split(';');
                foreach (var ext in exts)
                {
                    if (string.Equals(itemExt, ext, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }

            return string.Equals(itemExt, extension, StringComparison.OrdinalIgnoreCase);
        }

        private static bool Compare(CompareOp op, long value, long target)
        {
            return op switch
            {
                CompareOp.GreaterThan => value > target,
                CompareOp.LessThan => value < target,
                CompareOp.GreaterOrEqual => value >= target,
                CompareOp.LessOrEqual => value <= target,
                CompareOp.Equals => value == target,
                _ => false
            };
        }

        private static bool CompareDates(CompareOp op, DateTime value, DateTime target)
        {
            return op switch
            {
                CompareOp.GreaterThan => value > target,
                CompareOp.LessThan => value < target,
                CompareOp.GreaterOrEqual => value >= target,
                CompareOp.LessOrEqual => value <= target,
                CompareOp.Equals => value.Date == target.Date,
                _ => false
            };
        }
    }
}
