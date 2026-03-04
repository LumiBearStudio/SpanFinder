using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.Services;
using Span.Services.FileOperations;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace Span.Views
{
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
