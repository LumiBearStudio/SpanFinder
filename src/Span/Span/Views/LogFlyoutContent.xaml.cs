using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.Services;
using System.Collections.ObjectModel;

namespace Span.Views
{
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
