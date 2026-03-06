using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Span.Services;
using System.Collections.ObjectModel;

namespace Span.Views;

/// <summary>
/// 작업 로그 탭 뷰. Home 탭과 동일한 패턴으로 사이드바를 유지한 채 탐색기 영역에 표시.
/// LogEntryDisplay는 LogEntryDisplay.cs에 정의되어 있으며 공유한다.
/// </summary>
public sealed partial class LogModeView : UserControl
{
    private readonly ActionLogService _logService;
    private readonly ObservableCollection<LogEntryDisplay> _entries = new();
    private List<Models.ActionLogEntry> _allEntries = new();
    private string? _activeFilter;
    private LocalizationService? _loc;

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
    }

    private void ApplyFilter()
    {
        LogViewHelper.ApplyFilter(_allEntries, _activeFilter, _entries);
        EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LogListView.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateErrorBadge();
    }

    private void UpdateErrorBadge()
    {
        var errorCount = LogViewHelper.CountErrors(_allEntries);
        ErrorBadge.Visibility = errorCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        ErrorBadgeCount.Text = errorCount.ToString();
    }

    private void LocalizeUI()
    {
        LogViewHelper.LocalizeUI(_loc, TitleText, ClearButton, EmptyStateText,
            FilterAll, FilterCopy, FilterMove, FilterDelete, FilterRename, FilterErrorText);
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _logService.Clear();
        Refresh();
    }

    private void OnFilterClick(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked) return;
        _activeFilter = LogViewHelper.HandleFilterClick(clicked, FilterAll, FilterCopy, FilterMove, FilterDelete, FilterRename, FilterError);
        ApplyFilter();
    }

    private void OnExpandClick(object sender, RoutedEventArgs e)
    {
        LogViewHelper.HandleExpandClick(sender);
    }

    /// <summary>
    /// "폴더 열기" 버튼 클릭 — Windows Explorer에서 해당 폴더를 연다.
    /// </summary>
    private void OnOpenFolderClick(object sender, RoutedEventArgs e)
    {
        LogViewHelper.HandleOpenFolderClick(sender, "LogModeView");
    }
}
