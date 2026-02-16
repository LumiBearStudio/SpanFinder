using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using Span.Services;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Span.ViewModels
{
    public partial class PreviewPanelViewModel : ObservableObject, IDisposable
    {
        private readonly PreviewService _previewService;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _currentCts;
        private Timer? _debounceTimer;
        private bool _disposed;
        private const int DebounceMs = 200;

        // --- State ---

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _hasContent;

        // --- Metadata ---

        [ObservableProperty] private string _fileName = "";
        [ObservableProperty] private string _fileIconGlyph = "";
        [ObservableProperty] private Brush? _fileIconBrush;
        [ObservableProperty] private string _fileType = "";
        [ObservableProperty] private string _fileSizeFormatted = "";
        [ObservableProperty] private string _dateCreated = "";
        [ObservableProperty] private string _dateModified = "";

        // --- Type-specific info ---

        [ObservableProperty] private string _dimensions = "";
        [ObservableProperty] private string _duration = "";
        [ObservableProperty] private string _folderItemCount = "";
        [ObservableProperty] private string _artist = "";
        [ObservableProperty] private string _album = "";

        // --- Preview content ---

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsImageVisible))]
        [NotifyPropertyChangedFor(nameof(IsTextVisible))]
        [NotifyPropertyChangedFor(nameof(IsPdfVisible))]
        [NotifyPropertyChangedFor(nameof(IsMediaVisible))]
        [NotifyPropertyChangedFor(nameof(IsFolderVisible))]
        [NotifyPropertyChangedFor(nameof(IsGenericVisible))]
        private PreviewType _currentPreviewType = PreviewType.None;

        [ObservableProperty] private BitmapImage? _imagePreview;
        [ObservableProperty] private string? _textPreview;
        [ObservableProperty] private BitmapImage? _pdfPreview;
        [ObservableProperty] private MediaSource? _mediaSource;

        // --- Computed visibility ---

        public bool IsImageVisible => CurrentPreviewType == PreviewType.Image;
        public bool IsTextVisible => CurrentPreviewType == PreviewType.Text;
        public bool IsPdfVisible => CurrentPreviewType == PreviewType.Pdf;
        public bool IsMediaVisible => CurrentPreviewType == PreviewType.Media;
        public bool IsFolderVisible => CurrentPreviewType == PreviewType.Folder;
        public bool IsGenericVisible => CurrentPreviewType == PreviewType.Generic;

        public PreviewPanelViewModel(PreviewService previewService)
        {
            _previewService = previewService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        }

        /// <summary>
        /// Called when selection changes. Applies 200ms debouncing.
        /// </summary>
        public void OnSelectionChanged(FileSystemViewModel? selectedItem)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;

            if (selectedItem == null)
            {
                ClearPreview();
                return;
            }

            _debounceTimer = new Timer(
                _ => _dispatcherQueue.TryEnqueue(async () =>
                {
                    if (!_disposed)
                        await UpdatePreviewAsync(selectedItem);
                }),
                null,
                DebounceMs,
                Timeout.Infinite);
        }

        private async Task UpdatePreviewAsync(FileSystemViewModel item)
        {
            if (_disposed) return;

            _currentCts?.Cancel();
            _currentCts = new CancellationTokenSource();
            var ct = _currentCts.Token;

            try
            {
                IsLoading = true;
                HasContent = true;

                // 1. Basic metadata (sync, fast)
                SetBasicInfo(item);

                // 2. Type-specific preview
                bool isFolder = item is FolderViewModel;
                var previewType = _previewService.GetPreviewType(item.Path, isFolder);
                ClearPreviewContent();
                CurrentPreviewType = previewType;

                ct.ThrowIfCancellationRequested();

                switch (previewType)
                {
                    case PreviewType.Folder:
                        LoadFolderInfo(item.Path);
                        break;

                    case PreviewType.Image:
                        ImagePreview = await _previewService.LoadImagePreviewAsync(item.Path, 1024, ct);
                        var imgMeta = await _previewService.GetImageMetadataAsync(item.Path, ct);
                        if (imgMeta != null)
                            Dimensions = $"{imgMeta.Width} x {imgMeta.Height}";
                        break;

                    case PreviewType.Text:
                        TextPreview = await _previewService.LoadTextPreviewAsync(item.Path, ct);
                        break;

                    case PreviewType.Pdf:
                        PdfPreview = await _previewService.LoadPdfPreviewAsync(item.Path, ct);
                        break;

                    case PreviewType.Media:
                        MediaSource = await _previewService.LoadMediaSourceAsync(item.Path, ct);
                        var mediaMeta = await _previewService.GetMediaMetadataAsync(item.Path, ct);
                        if (mediaMeta != null)
                        {
                            Duration = mediaMeta.Duration.ToString(@"hh\:mm\:ss");
                            if (mediaMeta.Width.HasValue && mediaMeta.Height.HasValue)
                                Dimensions = $"{mediaMeta.Width} x {mediaMeta.Height}";
                            if (!string.IsNullOrEmpty(mediaMeta.Artist))
                                Artist = mediaMeta.Artist;
                            if (!string.IsNullOrEmpty(mediaMeta.Album))
                                Album = mediaMeta.Album;
                        }
                        break;

                    case PreviewType.Generic:
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation (rapid selection change)
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewPanel] Error: {ex.Message}");
                CurrentPreviewType = PreviewType.Generic;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    IsLoading = false;
            }
        }

        private void SetBasicInfo(FileSystemViewModel item)
        {
            FileName = item.Name;
            FileIconGlyph = item.IconGlyph;
            FileIconBrush = item.IconBrush;
            FileType = item.FileType;

            if (item is FolderViewModel)
            {
                FileSizeFormatted = "";
                DateCreated = "";
                DateModified = item.DateModified;
            }
            else
            {
                var metadata = _previewService.GetBasicMetadata(item.Path);
                FileSizeFormatted = metadata.SizeFormatted;
                DateCreated = metadata.Created.ToString("yyyy-MM-dd HH:mm");
                DateModified = metadata.Modified.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void LoadFolderInfo(string path)
        {
            var count = _previewService.GetFolderItemCount(path);
            var loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();
            FolderItemCount = string.Format(loc.Get("FolderItemCount"), count);
        }

        private void ClearPreviewContent()
        {
            // Null out MediaSource before disposing to unbind from UI
            var oldMedia = MediaSource;
            MediaSource = null;
            oldMedia?.Dispose();

            ImagePreview = null;
            TextPreview = null;
            PdfPreview = null;
            Dimensions = "";
            Duration = "";
            FolderItemCount = "";
            Artist = "";
            Album = "";
        }

        public void ClearPreview()
        {
            ClearPreviewContent();
            FileName = "";
            FileIconGlyph = "";
            FileIconBrush = null;
            FileType = "";
            FileSizeFormatted = "";
            DateCreated = "";
            DateModified = "";
            CurrentPreviewType = PreviewType.None;
            HasContent = false;
            IsLoading = false;
        }

        public void Dispose()
        {
            _disposed = true;
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _debounceTimer?.Dispose();
            ClearPreviewContent();
        }
    }
}
