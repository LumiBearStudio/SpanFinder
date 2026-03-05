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

            // Build file details list via helper
            string? moreFormat = null;
            try
            {
                var loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                moreFormat = loc?.Get("LogMore");
            }
            catch { }
            FileDetails = LogEntryDisplayHelper.BuildFileDetails(_entry.SourcePaths, MaxFileDetails, moreFormat);

            // Build destination text
            if (!string.IsNullOrEmpty(_entry.DestinationPath))
            {
                DestinationText = $"\u2192 {_entry.DestinationPath}";
            }

            // Determine open folder path via helper
            OpenFolderPath = LogEntryDisplayHelper.DetermineOpenFolderPath(
                _entry.SourcePaths, _entry.DestinationPath, FileSystemRouter.IsRemotePath);
        }

        public string Description => _entry.Description;
        public string? ErrorMessage => _entry.ErrorMessage;

        public string OperationGlyph => LogEntryDisplayHelper.GetOperationGlyph(_entry.OperationType);

        public string StatusGlyph => _entry.Success ? "\uE73E" : "\uE711";

        public SolidColorBrush StatusBrush => _entry.Success
            ? new SolidColorBrush(ColorHelper.FromArgb(255, 0x6C, 0xCB, 0x5F))
            : new SolidColorBrush(ColorHelper.FromArgb(255, 0xE7, 0x48, 0x56));

        public Visibility ErrorVisibility =>
            !_entry.Success && !string.IsNullOrEmpty(_entry.ErrorMessage)
                ? Visibility.Visible
                : Visibility.Collapsed;

        public string FormattedTime => LogEntryDisplayHelper.FormatTime(_entry.Timestamp, DateTime.Now);

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

    }
}
