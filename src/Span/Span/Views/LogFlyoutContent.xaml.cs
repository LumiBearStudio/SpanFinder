using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Span.Services;
using System.Collections.ObjectModel;

namespace Span.Views
{
    /// <summary>
    /// 파일 작업 로그 Flyout UserControl.
    /// ActionLogService에서 최근 100개 로그 항목을 가져와 ListView로 표시한다.
    /// 필터링, 확장 가능한 상세 정보, 개별 파일 목록을 지원한다.
    /// </summary>
    public sealed partial class LogFlyoutContent : UserControl
    {
        private readonly ActionLogService _logService;
        private readonly ObservableCollection<LogEntryDisplay> _entries = new();
        private List<Models.ActionLogEntry> _allEntries = new();
        private string? _activeFilter;
        private LocalizationService? _loc;

        public LogFlyoutContent(ActionLogService logService)
        {
            _logService = logService;
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

            Refresh();
        }

        /// <summary>
        /// 호스트 창 크기에 맞춰 Flyout 너비를 동적으로 조절한다.
        /// 창 너비의 35%를 사용하되 MinWidth~MaxWidth 범위 내로 제한한다.
        /// </summary>
        public void UpdateWidth(double windowWidth)
        {
            var desired = Math.Clamp(windowWidth * 0.35, MinWidth, MaxWidth);
            Width = desired;
        }

        public void Refresh()
        {
            _allEntries = LogViewHelper.RefreshEntries(_logService);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            LogViewHelper.ApplyFilter(_allEntries, _activeFilter, _entries);
            EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LogScrollViewer.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LocalizeUI()
        {
            LogViewHelper.LocalizeUI(_loc, TitleText, ClearButton, EmptyStateText);
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            _logService.Clear();
            Refresh();
        }

        private void OnFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;
            _activeFilter = LogViewHelper.HandleFilterClick(clicked, FilterAll, FilterCopy, FilterMove, FilterDelete, FilterRename);
            ApplyFilter();
        }

        private void OnExpandClick(object sender, RoutedEventArgs e)
        {
            LogViewHelper.HandleExpandClick(sender);
        }

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
        {
            LogViewHelper.HandleOpenFolderClick(sender, "LogFlyout");
        }
    }
}
