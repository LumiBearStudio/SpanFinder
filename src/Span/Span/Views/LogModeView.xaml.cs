using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Span.Services;
using System;
using System.Collections.ObjectModel;

namespace Span.Views;

/// <summary>
/// 작업 로그 탭 뷰. Settings 탭과 동일한 패턴으로 동작한다.
/// BackRequested 이벤트로 탭 닫기를 MainWindow에 위임한다.
/// LogEntryDisplay는 LogFlyoutContent.xaml.cs에 정의되어 있으며 공유한다.
/// </summary>
public sealed partial class LogModeView : UserControl
{
    private readonly ActionLogService _logService;
    private readonly ObservableCollection<LogEntryDisplay> _entries = new();
    private List<Models.ActionLogEntry> _allEntries = new();
    private string? _activeFilter;
    private LocalizationService? _loc;

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독하여 탭 닫기 처리)
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
        _allEntries = _logService.GetEntries(100);
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        _entries.Clear();
        var filtered = _activeFilter == null
            ? _allEntries
            : _allEntries.Where(e => e.OperationType == _activeFilter).ToList();

        foreach (var entry in filtered)
        {
            _entries.Add(new LogEntryDisplay(entry));
        }

        EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogListView.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LocalizeUI()
    {
        if (_loc == null) return;
        TitleText.Text = _loc.Get("Log_Title");
        ClearButton.Content = _loc.Get("Log_Clear");
        EmptyStateText.Text = _loc.Get("Log_Empty");
    }

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        Refresh();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;

        string? filter = clicked.Name switch
        {
            "FilterCopy" => "Copy",
            "FilterMove" => "Move",
            "FilterDelete" => "Delete",
            "FilterRename" => "Rename",
            _ => null
        };

        FilterAll.IsChecked = clicked == FilterAll;
        FilterCopy.IsChecked = clicked == FilterCopy;
        FilterMove.IsChecked = clicked == FilterMove;
        FilterDelete.IsChecked = clicked == FilterDelete;
        FilterRename.IsChecked = clicked == FilterRename;

        _activeFilter = filter;
        ApplyFilter();
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is LogEntryDisplay display)
        {
            display.IsExpanded = !display.IsExpanded;
        }
    }
}
