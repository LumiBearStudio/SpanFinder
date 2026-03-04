using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System.Collections.ObjectModel;
using System.ComponentModel;

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
        private List<ActionLogEntry> _allEntries = new();
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

        private void OnFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked) return;

            // Map filter buttons to operation types
            string? filter = clicked.Name switch
            {
                "FilterCopy" => "Copy",
                "FilterMove" => "Move",
                "FilterDelete" => "Delete",
                "FilterRename" => "Rename",
                _ => null // FilterAll
            };

            // Radio-button behavior: uncheck others
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

        private void OnOpenFolderClick(object sender, RoutedEventArgs e)
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
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[LogFlyout] OpenFolder failed: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// ActionLogEntry의 디스플레이 래퍼.
    /// 작업 유형별 아이콘 글리프, 성공/실패 색상, 포맷된 시간,
    /// 확장 가능한 상세 파일 목록 등 UI 바인딩용 속성을 제공한다.
    /// </summary>
    internal class LogEntryDisplay : INotifyPropertyChanged
    {
        private readonly ActionLogEntry _entry;
        private bool _isExpanded;
        private const int MaxFileDetails = 20;

        public event PropertyChangedEventHandler? PropertyChanged;

        public LogEntryDisplay(ActionLogEntry entry)
        {
            _entry = entry;

            // Build file details list (full paths for single item, file names for multiple)
            var details = new List<string>();
            if (_entry.SourcePaths != null && _entry.SourcePaths.Count > 0)
            {
                var paths = _entry.SourcePaths.Take(MaxFileDetails).ToList();
                bool showFullPath = _entry.SourcePaths.Count == 1;
                foreach (var path in paths)
                {
                    details.Add(showFullPath ? path : GetFileName(path));
                }
                if (_entry.SourcePaths.Count > MaxFileDetails)
                {
                    var loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                    var fmt = loc?.Get("LogMore") ?? "... and {0} more";
                    details.Add(string.Format(fmt, _entry.SourcePaths.Count - MaxFileDetails));
                }
            }
            FileDetails = details;

            // Build destination text
            if (!string.IsNullOrEmpty(_entry.DestinationPath))
            {
                DestinationText = $"\u2192 {_entry.DestinationPath}";
            }

            // Build source folder path (for "open folder" button)
            if (_entry.SourcePaths != null && _entry.SourcePaths.Count > 0)
            {
                var firstPath = _entry.SourcePaths[0];
                try
                {
                    if (!string.IsNullOrEmpty(firstPath) && !FileSystemRouter.IsRemotePath(firstPath))
                    {
                        var parent = System.IO.Path.GetDirectoryName(firstPath);
                        if (!string.IsNullOrEmpty(parent))
                            OpenFolderPath = parent;
                    }
                }
                catch { }
            }

            // For Copy/Move, prefer destination path for "open folder"
            if (!string.IsNullOrEmpty(_entry.DestinationPath) && !FileSystemRouter.IsRemotePath(_entry.DestinationPath))
            {
                OpenFolderPath = _entry.DestinationPath;
            }
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
                    return ts.ToString("HH:mm:ss");
                if (ts.Date == now.Date.AddDays(-1))
                    return $"어제 {ts:HH:mm}";
                return ts.ToString("MM/dd HH:mm");
            }
        }

        // Expandable details
        public List<string> FileDetails { get; }
        public string? DestinationText { get; }

        public Visibility DestinationVisibility =>
            !string.IsNullOrEmpty(DestinationText) ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>
        /// 소스 경로가 1개 이상이거나 대상 경로가 있으면 expand 버튼 표시.
        /// 단일 파일 Delete/Rename도 전체 경로 확인 가능하게 변경.
        /// </summary>
        public Visibility ExpandButtonVisibility =>
            (_entry.SourcePaths != null && _entry.SourcePaths.Count > 0) || !string.IsNullOrEmpty(_entry.DestinationPath)
                ? Visibility.Visible
                : Visibility.Collapsed;

        /// <summary>폴더 열기 버튼 대상 경로. Copy/Move → DestinationPath, Delete/Rename → 소스 부모 경로.</summary>
        public string? OpenFolderPath { get; }

        public Visibility OpenFolderVisibility =>
            !string.IsNullOrEmpty(OpenFolderPath) ? Visibility.Visible : Visibility.Collapsed;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailVisibility)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ExpandGlyph)));
                }
            }
        }

        public Visibility DetailVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;
        public string ExpandGlyph => _isExpanded ? "\uE70E" : "\uE70D";

        private static string GetFileName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            try
            {
                if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "ftp" || uri.Scheme == "sftp"))
                {
                    var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
                    return segments.Length > 0 ? Uri.UnescapeDataString(segments[^1]) : path;
                }
                return System.IO.Path.GetFileName(path);
            }
            catch
            {
                return path;
            }
        }
    }
}
