using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.Services;
using System.Collections.ObjectModel;

namespace Span.Views
{
    /// <summary>
    /// 파일 작업 로그 Flyout UserControl.
    /// ActionLogService에서 최근 100개 로그 항목을 가져와 ListView로 표시한다.
    /// 각 항목의 작업 유형 아이콘, 성공/실패 상태, 타임스탬프를 시각화한다.
    /// </summary>
    public sealed partial class LogFlyoutContent : UserControl
    {
        private readonly ActionLogService _logService;
        private readonly ObservableCollection<LogEntryDisplay> _entries = new();
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

        public void Refresh()
        {
            _entries.Clear();
            var entries = _logService.GetEntries(100);
            foreach (var entry in entries)
            {
                _entries.Add(new LogEntryDisplay(entry));
            }

            EmptyState.Visibility = _entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            LogScrollViewer.Visibility = _entries.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void LocalizeUI()
        {
            if (_loc == null) return;
            TitleText.Text = _loc.Get("Log_Title");
            ClearButton.Content = _loc.Get("Log_Clear");
            EmptyStateText.Text = _loc.Get("Log_Empty");
        }

        private void OnClearClick(object sender, RoutedEventArgs e)
        {
            _logService.Clear();
            Refresh();
        }
    }

    /// <summary>
    /// ActionLogEntry의 디스플레이 래퍼.
    /// 작업 유형별 아이콘 글리프, 성공/실패 색상, 포맷된 시간 등
    /// UI 바인딩용 속성을 제공한다.
    /// </summary>
    internal class LogEntryDisplay
    {
        private readonly ActionLogEntry _entry;

        public LogEntryDisplay(ActionLogEntry entry)
        {
            _entry = entry;
        }

        public string Description => _entry.Description;
        public string? ErrorMessage => _entry.ErrorMessage;

        public string OperationGlyph => _entry.OperationType switch
        {
            "Copy" => "\uE8C8",
            "Move" => "\uE8DE",
            "Delete" => "\uE74D",
            "Rename" => "\uE8AC",
            "NewFolder" => "\uE8B7",
            _ => "\uE946"
        };

        public string StatusGlyph => _entry.Success ? "\uE73E" : "\uE711";

        public SolidColorBrush StatusBrush => _entry.Success
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 0x6C, 0xCB, 0x5F))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 0xE7, 0x48, 0x56));

        public Visibility ErrorVisibility =>
            !_entry.Success && !string.IsNullOrEmpty(_entry.ErrorMessage)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public string FormattedTime
        {
            get
            {
                var now = DateTime.Now;
                var ts = _entry.Timestamp;

                if (ts.Date == now.Date)
                    return ts.ToString("HH:mm");
                if (ts.Date == now.Date.AddDays(-1))
                    return "어제";
                return ts.ToString("MM/dd");
            }
        }
    }
}
