using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Services;
using System;
using System.Collections.ObjectModel;

namespace Span.Views;

/// <summary>
/// 작업 로그 탭 뷰. 탐색기와 동일한 커스텀 사이드바 패턴.
/// </summary>
public sealed partial class LogModeView : UserControl
{
    private readonly ActionLogService _logService;
    private readonly ObservableCollection<LogEntryDisplay> _entries = new();
    private List<Models.ActionLogEntry> _allEntries = new();
    private string? _activeFilter;
    private LocalizationService? _loc;
    private Grid? _selectedNavItem;

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독)
    /// </summary>
    public event EventHandler? BackRequested;

    public LogModeView()
    {
        _logService = App.Current.Services.GetRequiredService<ActionLogService>();
        this.InitializeComponent();
        LogListView.ItemsSource = _entries;
        _selectedNavItem = NavFilterAll;

        this.Loaded += (s, e) =>
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            LocalizeUI();
            if (_loc != null) _loc.LanguageChanged += LocalizeUI;
        };
        this.Unloaded += (s, e) =>
        {
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
        };
    }

    /// <summary>
    /// 탭이 활성화될 때 호출하여 최신 로그를 로드한다.
    /// </summary>
    public void Refresh()
    {
        _allEntries = LogViewHelper.RefreshEntries(_logService);
        ApplyFilter();
        UpdateErrorBadge();
    }

    /// <summary>
    /// 필터를 설정한다 (외부에서 호출 가능).
    /// </summary>
    public void SetFilter(string? filter)
    {
        _activeFilter = filter;
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        LogViewHelper.ApplyFilter(_allEntries, _activeFilter, _entries);
        EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogListView.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static TextBlock? FindTextBlock(Grid grid)
    {
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb && tb.Name == "") // unnamed TextBlock in Column 1
            {
                if (Microsoft.UI.Xaml.Controls.Grid.GetColumn((FrameworkElement)child) == 1)
                    return tb;
            }
        }
        return null;
    }

    private void LocalizeUI()
    {
        if (_loc == null) return;
        TitleText.Text = _loc.Get("Log_Title");
        ClearButton.Content = _loc.Get("Log_Clear");
        EmptyStateText.Text = _loc.Get("Log_Empty");

        SetNavText(NavFilterAll, _loc.Get("FilterAll"));
        SetNavText(NavFilterCopy, _loc.Get("Copy"));
        SetNavText(NavFilterMove, _loc.Get("Move"));
        SetNavText(NavFilterDelete, _loc.Get("Delete"));
        SetNavText(NavFilterRename, _loc.Get("Rename"));
        SetNavText(NavFilterError, _loc.Get("FilterError"));
    }

    private static void SetNavText(Grid grid, string text)
    {
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb && Grid.GetColumn(tb) == 1)
            {
                tb.Text = text;
                return;
            }
        }
    }

    private void UpdateErrorBadge()
    {
        var errorCount = LogViewHelper.CountErrors(_allEntries);
        if (errorCount > 0)
            SetNavText(NavFilterError, $"{(_loc?.Get("FilterError") ?? "오류")} ({errorCount})");
    }

    // ── 커스텀 사이드바 (탐색기 사이드바와 동일 패턴) ──

    private void OnNavItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Tag is string tag)
        {
            SelectNavItem(grid);
            _activeFilter = tag switch
            {
                "Copy" => "Copy",
                "Move" => "Move",
                "Delete" => "Delete",
                "Rename" => "Rename",
                "Error" => LogViewHelper.ErrorFilter,
                _ => null // "All"
            };
            ApplyFilter();
        }
    }

    private void SelectNavItem(Grid item)
    {
        if (_selectedNavItem != null)
            _selectedNavItem.Background = new SolidColorBrush(Colors.Transparent);

        _selectedNavItem = item;
        item.Background = (Brush)Application.Current.Resources["SpanBgSelectedBrush"];
    }

    private void OnNavItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedNavItem)
            grid.Background = (Brush)Application.Current.Resources["SpanBgHoverBrush"];
    }

    private void OnNavItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedNavItem)
            grid.Background = new SolidColorBrush(Colors.Transparent);
    }

    // ── Actions ──

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        Refresh();
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        LogViewHelper.HandleExpandClick(sender);
    }

    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        LogViewHelper.HandleOpenFolderClick(sender, "LogModeView");
    }
}
