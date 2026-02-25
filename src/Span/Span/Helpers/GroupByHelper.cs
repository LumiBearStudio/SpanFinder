using System;
using System.Collections.Generic;
using Span.ViewModels;

namespace Span.Helpers
{
    /// <summary>
    /// Group header class for grouped items in GridView/ListView.
    /// </summary>
    public class ItemGroup : List<FileSystemViewModel>
    {
        public string Key { get; }
        public new int Count => base.Count;

        public ItemGroup(string key, IEnumerable<FileSystemViewModel> items) : base(items)
        {
            Key = key;
        }
    }

    /// <summary>
    /// Shared group key logic for Icon/List/Details views.
    /// </summary>
    public static class GroupByHelper
    {
        public static string GetGroupKey(FileSystemViewModel item, string groupBy)
        {
            switch (groupBy)
            {
                case "Name":
                    var firstChar = !string.IsNullOrEmpty(item.Name)
                        ? char.ToUpperInvariant(item.Name[0]).ToString()
                        : "#";
                    return char.IsLetter(firstChar[0]) ? firstChar : "#";

                case "Type":
                    if (item is FolderViewModel) return "Folder";
                    return string.IsNullOrEmpty(item.FileType) ? "Unknown" : item.FileType.ToUpperInvariant();

                case "DateModified":
                    var date = item.DateModifiedValue;
                    var now = DateTime.Now;
                    if (date.Date == now.Date) return "Today";
                    if (date.Date == now.Date.AddDays(-1)) return "Yesterday";
                    if (date >= now.Date.AddDays(-(int)now.DayOfWeek)) return "This Week";
                    if (date.Year == now.Year && date.Month == now.Month) return "This Month";
                    if (date.Year == now.Year) return "This Year";
                    return date.Year > 0 ? date.Year.ToString() : "Unknown";

                case "Size":
                    if (item is FolderViewModel) return "Folders";
                    var size = item.SizeValue;
                    if (size == 0) return "Empty (0 B)";
                    if (size < 16 * 1024) return "Tiny (< 16 KB)";
                    if (size < 1024 * 1024) return "Small (< 1 MB)";
                    if (size < 128 * 1024 * 1024) return "Medium (< 128 MB)";
                    if (size < 1024L * 1024 * 1024) return "Large (< 1 GB)";
                    return "Huge (> 1 GB)";

                default:
                    return string.Empty;
            }
        }
    }
}
