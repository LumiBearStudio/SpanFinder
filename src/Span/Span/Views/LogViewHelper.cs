using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Span.Models;
using Span.Services;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Span.Views;

internal static class LogViewHelper
{
    internal const string ErrorFilter = "__Error__";

    /// <summary>
    /// 로그 항목 리프레시 (양쪽 뷰 공통)
    /// </summary>
    internal static List<ActionLogEntry> RefreshEntries(ActionLogService logService)
    {
        return logService.GetEntries(100);
    }

    /// <summary>
    /// 필터 적용 (양쪽 뷰 공통)
    /// </summary>
    internal static void ApplyFilter(
        List<ActionLogEntry> allEntries,
        string? activeFilter,
        ObservableCollection<LogEntryDisplay> entries)
    {
        entries.Clear();
        var filtered = activeFilter switch
        {
            null => allEntries,
            ErrorFilter => allEntries.Where(e => !e.Success).ToList(),
            _ => allEntries.Where(e => e.OperationType == activeFilter).ToList()
        };
        foreach (var entry in filtered)
            entries.Add(new LogEntryDisplay(entry));
    }

    /// <summary>
    /// 에러 항목 수 (뱃지 표시용)
    /// </summary>
    internal static int CountErrors(List<ActionLogEntry> allEntries)
        => allEntries.Count(e => !e.Success);

    /// <summary>
    /// 필터 버튼 클릭 처리 (라디오 동작)
    /// </summary>
    internal static string? HandleFilterClick(
        ToggleButton clicked,
        ToggleButton filterAll, ToggleButton filterCopy,
        ToggleButton filterMove, ToggleButton filterDelete,
        ToggleButton filterRename, ToggleButton filterError)
    {
        string? filter = clicked.Name switch
        {
            "FilterCopy" => "Copy",
            "FilterMove" => "Move",
            "FilterDelete" => "Delete",
            "FilterRename" => "Rename",
            "FilterError" => ErrorFilter,
            _ => null
        };
        filterAll.IsChecked = clicked == filterAll;
        filterCopy.IsChecked = clicked == filterCopy;
        filterMove.IsChecked = clicked == filterMove;
        filterDelete.IsChecked = clicked == filterDelete;
        filterRename.IsChecked = clicked == filterRename;
        filterError.IsChecked = clicked == filterError;
        return filter;
    }

    /// <summary>
    /// 확장 토글 (양쪽 뷰 공통)
    /// </summary>
    internal static void HandleExpandClick(object sender)
    {
        if (sender is Button btn && btn.DataContext is LogEntryDisplay display)
            display.IsExpanded = !display.IsExpanded;
    }

    /// <summary>
    /// 폴더 열기 (양쪽 뷰 공통)
    /// </summary>
    internal static void HandleOpenFolderClick(object sender, string logTag)
    {
        if (sender is Button btn && btn.DataContext is LogEntryDisplay display
            && !string.IsNullOrEmpty(display.OpenFolderPath))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = display.OpenFolderPath,
                    UseShellExecute = true
                });
            }
            catch (System.Exception ex)
            {
                Helpers.DebugLogger.Log($"[{logTag}] OpenFolder failed: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// UI 현지화 (양쪽 뷰 공통)
    /// </summary>
    internal static void LocalizeUI(LocalizationService? loc, TextBlock titleText, object clearButton, TextBlock emptyStateText)
    {
        if (loc == null) return;
        titleText.Text = loc.Get("Log_Title");
        if (clearButton is Button btn) btn.Content = loc.Get("Log_Clear");
        emptyStateText.Text = loc.Get("Log_Empty");
    }
}
