using System;
using System.Collections.Generic;
using Span.Services;
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
    /// Returns keys prefixed with sort order (e.g. "01|Today") for correct ordering.
    /// Use <see cref="StripSortPrefix"/> to get the display label.
    /// </summary>
    public static class GroupByHelper
    {
        /// <summary>
        /// Returns a sort-prefixed group key like "03|Earlier this week".
        /// The prefix ensures chronological/logical ordering when sorted alphabetically.
        /// </summary>
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
                    if (item is FolderViewModel) return LocalizationService.L("Group_Folder");
                    return string.IsNullOrEmpty(item.FileType)
                        ? LocalizationService.L("Group_Unknown")
                        : item.FileType.ToUpperInvariant();

                case "DateModified":
                    return GetDateGroupKey(item.DateModifiedValue);

                case "Size":
                    return GetSizeGroupKey(item);

                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Strips the "NN|" sort prefix from a group key, returning the display label.
        /// If no prefix is present, returns the key as-is.
        /// </summary>
        public static string StripSortPrefix(string key)
        {
            if (key.Length > 3 && key[2] == '|')
                return key.Substring(3);
            return key;
        }

        private static string GetDateGroupKey(DateTime date)
        {
            var now = DateTime.Now;
            var today = now.Date;

            // Today
            if (date.Date == today)
                return "01|" + LocalizationService.L("Group_Today");

            // Yesterday
            if (date.Date == today.AddDays(-1))
                return "02|" + LocalizationService.L("Group_Yesterday");

            // Earlier this week (same week, but not today/yesterday)
            // Week starts on Sunday for DayOfWeek enum
            var startOfWeek = today.AddDays(-(int)today.DayOfWeek);
            if (date.Date >= startOfWeek)
                return "03|" + LocalizationService.L("Group_ThisWeek");

            // Last week
            var startOfLastWeek = startOfWeek.AddDays(-7);
            if (date.Date >= startOfLastWeek)
                return "04|" + LocalizationService.L("Group_LastWeek");

            // Earlier this month
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            if (date.Date >= startOfMonth)
                return "05|" + LocalizationService.L("Group_ThisMonth");

            // Last month
            var startOfLastMonth = startOfMonth.AddMonths(-1);
            if (date.Date >= startOfLastMonth)
                return "06|" + LocalizationService.L("Group_LastMonth");

            // Older
            return "07|" + LocalizationService.L("Group_Older");
        }

        private static string GetSizeGroupKey(FileSystemViewModel item)
        {
            if (item is FolderViewModel)
                return "01|" + LocalizationService.L("Group_Folders");

            var size = item.SizeValue;
            if (size == 0) return "02|" + LocalizationService.L("Group_Empty");
            if (size < 16 * 1024) return "03|" + LocalizationService.L("Group_Tiny");
            if (size < 1024 * 1024) return "04|" + LocalizationService.L("Group_Small");
            if (size < 128 * 1024 * 1024) return "05|" + LocalizationService.L("Group_Medium");
            if (size < 1024L * 1024 * 1024) return "06|" + LocalizationService.L("Group_Large");
            return "07|" + LocalizationService.L("Group_Huge");
        }
    }
}
