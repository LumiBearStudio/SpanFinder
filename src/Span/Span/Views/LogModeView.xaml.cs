using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
using System;
using System.Collections.ObjectModel;

namespace Span.Views;

/// <summary>
/// 작업 로그 탭 뷰. Settings와 동일한 NavigationView 패턴.
/// 자체 사이드바(필터 메뉴)를 NavigationView로 내장한다.
/// </summary>
public sealed partial class LogModeView : UserControl
{
    private readonly ActionLogService _logService;
    private readonly ObservableCollection<LogEntryDisplay> _entries = new();
    private List<Models.ActionLogEntry> _allEntries = new();
    private string? _activeFilter;
    private LocalizationService? _loc;

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독)
    /// </summary>
    public event EventHandler? BackRequested;

    public LogModeView()
    {
        _logService = App.Current.Services.GetRequiredService<ActionLogService>();
        this.InitializeComponent();
        LogListView.ItemsSource = _entries;

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

    private void LocalizeUI()
    {
        if (_loc == null) return;
        TitleText.Text = _loc.Get("Log_Title");
        ClearButton.Content = _loc.Get("Log_Clear");
        EmptyStateText.Text = _loc.Get("Log_Empty");

        NavFilterAll.Content = _loc.Get("FilterAll");
        NavFilterCopy.Content = _loc.Get("Copy");
        NavFilterMove.Content = _loc.Get("Move");
        NavFilterDelete.Content = _loc.Get("Delete");
        NavFilterRename.Content = _loc.Get("Rename");
        NavFilterError.Content = _loc.Get("FilterError");
    }

    private void UpdateErrorBadge()
    {
        var errorCount = LogViewHelper.CountErrors(_allEntries);
        if (errorCount > 0)
            NavFilterError.Content = $"{(_loc?.Get("FilterError") ?? "오류")} ({errorCount})";
    }

    // ── NavigationView ──

    private void LogNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
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

    private void LogNav_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var mode = width < 500
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.Left;

        if (LogNav.PaneDisplayMode != mode)
        {
            LogNav.PaneDisplayMode = mode;
            LogNav.IsPaneOpen = true;
        }
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
