using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using Span.Services;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Span.ViewModels
{
    /// <summary>
    /// Quick Look 플로팅 윈도우 뷰모델.
    /// 선택된 파일의 미리보기를 표시하며, 파일 선택 변경 시 실시간으로 업데이트.
    /// PreviewPanelViewModel과 유사하지만 Quick Look 전용 경량 버전.
    /// </summary>
    public partial class QuickLookViewModel : ObservableObject, IDisposable
    {
        private readonly PreviewService _previewService;
        private readonly ArchiveReaderService? _archiveReader;
        private readonly ISettingsService? _settings;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _currentCts;
        private bool _disposed;

        // --- State ---
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _hasContent;

        // --- Current item path (for actions) ---
        private string _currentFilePath = "";
        private bool _isFolder;

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
        [ObservableProperty] private string _folderSizeText = "";
        [ObservableProperty] private string _artist = "";
        [ObservableProperty] private string _album = "";

        // --- Preview content ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsImageVisible))]
        [NotifyPropertyChangedFor(nameof(IsTextVisible))]
        [NotifyPropertyChangedFor(nameof(IsPdfVisible))]
        [NotifyPropertyChangedFor(nameof(IsMediaVisible))]
        [NotifyPropertyChangedFor(nameof(IsFolderVisible))]
        [NotifyPropertyChangedFor(nameof(IsHexBinaryVisible))]
        [NotifyPropertyChangedFor(nameof(IsFontVisible))]
        [NotifyPropertyChangedFor(nameof(IsGenericVisible))]
        [NotifyPropertyChangedFor(nameof(IsArchiveVisible))]
        private PreviewType _currentPreviewType = PreviewType.None;

        // --- Image rotation (UI only, not saved until explicit save) ---
        [ObservableProperty] private double _rotationAngle;
        [ObservableProperty] private bool _hasPendingRotation;

        [ObservableProperty] private BitmapImage? _imagePreview;
        [ObservableProperty] private string? _textPreview;
        [ObservableProperty] private BitmapImage? _pdfPreview;
        [ObservableProperty] private MediaSource? _mediaSource;
        [ObservableProperty] private string? _hexPreview;
        [ObservableProperty] private string _fontFamilySource = "";
        [ObservableProperty] private string _fontFormat = "";

        // --- Archive preview ---
        [ObservableProperty] private string _archiveContentTree = "";
        [ObservableProperty] private string _archiveStats = "";
        [ObservableProperty] private string _archiveCompressedSize = "";
        [ObservableProperty] private string _archiveUncompressedSize = "";
        [ObservableProperty] private string _archiveCompressionRatio = "";

        // --- Git info ---
        [ObservableProperty] private string _gitLastCommitInfo = "";
        [ObservableProperty] private bool _hasGitInfo;

        // --- Computed visibility ---
        public bool IsImageVisible => CurrentPreviewType == PreviewType.Image;
        public bool IsTextVisible => CurrentPreviewType == PreviewType.Text;
        public bool IsPdfVisible => CurrentPreviewType == PreviewType.Pdf;
        public bool IsMediaVisible => CurrentPreviewType == PreviewType.Media;
        public bool IsFolderVisible => CurrentPreviewType == PreviewType.Folder;
        public bool IsHexBinaryVisible => CurrentPreviewType == PreviewType.HexBinary;
        public bool IsFontVisible => CurrentPreviewType == PreviewType.Font;
        public bool IsGenericVisible => CurrentPreviewType == PreviewType.Generic;
        public bool IsArchiveVisible => CurrentPreviewType == PreviewType.Archive;

        /// <summary>
        /// Quick Look 윈도우가 닫힐 때 호출될 콜백.
        /// </summary>
        public event Action? CloseRequested;

        public QuickLookViewModel(PreviewService previewService)
        {
            _previewService = previewService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
            _archiveReader = App.Current.Services.GetService<ArchiveReaderService>();

            try
            {
                _settings = App.Current.Services.GetRequiredService<ISettingsService>();
            }
            catch
            {
                _settings = null;
            }
        }

        /// <summary>
        /// 선택된 항목으로 Quick Look 내용을 업데이트한다.
        /// 디바운싱 없이 즉시 로딩 (Quick Look은 이미 열려 있는 상태에서 화살표로 이동하므로).
        /// </summary>
        public void UpdateContent(FileSystemViewModel? item)
        {
            if (_disposed || item == null)
            {
                ClearPreview();
                return;
            }

            _currentCts?.Cancel();
            _currentCts = new CancellationTokenSource();

            _ = UpdatePreviewAsync(item, _currentCts.Token);
        }

        private async Task UpdatePreviewAsync(FileSystemViewModel item, CancellationToken ct)
        {
            if (_disposed) return;

            try
            {
                IsLoading = true;
                HasContent = true;

                // 1. Basic metadata
                SetBasicInfo(item);

                // 2. Type-specific preview
                bool isFolder = item is FolderViewModel;
                var previewType = _previewService.GetPreviewType(item.Path, isFolder);
                if (previewType == PreviewType.HexBinary && _settings != null && !_settings.ShowHexPreview)
                    previewType = PreviewType.Generic;

                // Cloud-only files
                if (!isFolder && previewType != PreviewType.Image && previewType != PreviewType.Media
                    && previewType != PreviewType.Generic && previewType != PreviewType.Folder
                    && Services.CloudSyncService.IsCloudOnlyFile(item.Path))
                {
                    previewType = PreviewType.Generic;
                }

                // Reset previous
                ClearPreviewContent();
                CurrentPreviewType = previewType;

                if (ct.IsCancellationRequested) return;

                // 3. Load preview content
                switch (previewType)
                {
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
                            if (!string.IsNullOrEmpty(mediaMeta.Artist)) Artist = mediaMeta.Artist;
                            if (!string.IsNullOrEmpty(mediaMeta.Album)) Album = mediaMeta.Album;
                        }
                        break;

                    case PreviewType.HexBinary:
                        HexPreview = await _previewService.LoadHexPreviewAsync(item.Path, ct);
                        break;

                    case PreviewType.Font:
                        var fontData = _previewService.GetFontPreviewData(item.Path);
                        if (fontData != null)
                        {
                            FontFamilySource = fontData.FamilyName;
                            FontFormat = fontData.Extension;
                        }
                        break;

                    case PreviewType.Archive:
                        await LoadArchivePreviewAsync(item.Path, ct);
                        break;

                    case PreviewType.Folder:
                        if (item is FolderViewModel folderVm)
                        {
                            FolderItemCount = folderVm.Children.Count > 0
                                ? string.Format(LocalizationService.L("QuickLook_Items"), folderVm.Children.Count)
                                : "";
                            // 비동기 폴더 사이즈 계산
                            FolderSizeText = LocalizationService.L("QuickLook_CalculatingSize");
                            _ = CalculateFolderSizeAsync(item.Path, ct);
                        }
                        break;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLookVM] Preview error: {ex.Message}");
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    IsLoading = false;
            }
        }

        private void SetBasicInfo(FileSystemViewModel item)
        {
            _currentFilePath = item.Path;
            _isFolder = item is FolderViewModel;

            FileName = item.Name;
            FileIconGlyph = item.IconGlyph;
            FileIconBrush = item.IconBrush;
            FileType = item.FileType;

            if (item is FolderViewModel)
            {
                FileSizeFormatted = "";
                if (!Services.FileSystemRouter.IsRemotePath(item.Path)
                    && !Helpers.ArchivePathHelper.IsArchivePath(item.Path))
                {
                    try
                    {
                        var dirInfo = new System.IO.DirectoryInfo(item.Path);
                        if (dirInfo.Exists)
                        {
                            DateCreated = dirInfo.CreationTime.ToString("yyyy-MM-dd HH:mm");
                            DateModified = dirInfo.LastWriteTime.ToString("yyyy-MM-dd HH:mm");
                        }
                        else
                        {
                            DateCreated = "";
                            DateModified = item.DateModified;
                        }
                    }
                    catch
                    {
                        DateCreated = "";
                        DateModified = item.DateModified;
                    }
                }
                else
                {
                    DateCreated = "";
                    DateModified = item.DateModified;
                }
            }
            else
            {
                if (Services.FileSystemRouter.IsRemotePath(item.Path)
                    || Helpers.ArchivePathHelper.IsArchivePath(item.Path))
                {
                    FileSizeFormatted = item.Size;
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
        }

        private async Task LoadArchivePreviewAsync(string path, CancellationToken ct)
        {
            if (_archiveReader == null) return;

            try
            {
                var info = await _archiveReader.GetArchiveInfoAsync(path, ct);
                if (ct.IsCancellationRequested) return;

                if (info.TotalFiles < 0)
                {
                    ArchiveStats = LocalizationService.L("Preview_CannotReadArchive");
                    ArchiveCompressedSize = FormatFileSize(info.CompressedSize);
                    ArchiveUncompressedSize = "-";
                    ArchiveCompressionRatio = "-";
                    ArchiveContentTree = "";
                    return;
                }

                ArchiveStats = string.Format(LocalizationService.L("QuickLook_ArchiveStats"), info.TotalFiles.ToString("N0"), info.TotalFolders.ToString("N0"));
                ArchiveCompressedSize = FormatFileSize(info.CompressedSize);
                ArchiveUncompressedSize = FormatFileSize(info.UncompressedSize);
                ArchiveCompressionRatio = info.CompressionRatio > 0
                    ? $"{info.CompressionRatio:F1}%"
                    : "-";

                // Build tree text
                var sb = new StringBuilder();
                var entries = info.TopEntries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var indent = entry.Depth > 0
                        ? new string(' ', (entry.Depth - 1) * 2) + "\u2514 "
                        : "";
                    var icon = entry.IsDirectory ? "\uD83D\uDCC1 " : "";
                    var size = entry.IsDirectory ? "" : $" ({FormatFileSize(entry.Size)})";
                    sb.AppendLine($"{indent}{icon}{entry.Name}{size}");
                }
                ArchiveContentTree = sb.ToString().TrimEnd();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLookVM] Archive preview error: {ex.Message}");
            }
        }

        private async Task CalculateFolderSizeAsync(string folderPath, CancellationToken ct)
        {
            try
            {
                long totalSize = await Task.Run(() =>
                {
                    long size = 0;
                    try
                    {
                        var dirInfo = new System.IO.DirectoryInfo(folderPath);
                        foreach (var file in dirInfo.EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                        {
                            ct.ThrowIfCancellationRequested();
                            try { size += file.Length; } catch { }
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch { }
                    return size;
                }, ct);

                if (!ct.IsCancellationRequested)
                {
                    _dispatcherQueue.TryEnqueue(() =>
                    {
                        FolderSizeText = FormatFileSize(totalSize);
                        FileSizeFormatted = FormatFileSize(totalSize);
                    });
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLookVM] Folder size error: {ex.Message}");
                _dispatcherQueue.TryEnqueue(() => FolderSizeText = "");
            }
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        }

        private void ClearPreviewContent()
        {
            RotationAngle = 0;
            HasPendingRotation = false;
            ImagePreview = null;
            TextPreview = null;
            PdfPreview = null;
            MediaSource = null;
            HexPreview = null;
            FontFamilySource = "";
            FontFormat = "";
            Dimensions = "";
            Duration = "";
            Artist = "";
            Album = "";
            FolderItemCount = "";
            FolderSizeText = "";
            ArchiveContentTree = "";
            ArchiveStats = "";
            ArchiveCompressedSize = "";
            ArchiveUncompressedSize = "";
            ArchiveCompressionRatio = "";
            HasGitInfo = false;
            GitLastCommitInfo = "";
        }

        private void ClearPreview()
        {
            ClearPreviewContent();
            CurrentPreviewType = PreviewType.None;
            HasContent = false;
            FileName = "";
            FileType = "";
            FileSizeFormatted = "";
            DateCreated = "";
            DateModified = "";
        }

        // =============================================
        //  Action Commands
        // =============================================

        /// <summary>
        /// 액션 실행 요청 이벤트. Window에서 서비스 호출을 처리.
        /// </summary>
        public event Action<string, string>? ActionRequested;

        public string CurrentFilePath => _currentFilePath;
        public bool CurrentIsFolder => _isFolder;

        [RelayCommand]
        private void OpenDefault() => ActionRequested?.Invoke("open", _currentFilePath);

        [RelayCommand]
        private void OpenWith() => ActionRequested?.Invoke("openWith", _currentFilePath);

        [RelayCommand]
        private void CopyPath() => ActionRequested?.Invoke("copyPath", _currentFilePath);

        [RelayCommand]
        private void CopyContent() => ActionRequested?.Invoke("copyContent", _currentFilePath);

        [RelayCommand]
        private void RotateRight()
        {
            RotationAngle = (RotationAngle + 90) % 360;
            HasPendingRotation = RotationAngle != 0;
        }

        [RelayCommand]
        private void SaveRotation() => ActionRequested?.Invoke("saveRotation", _currentFilePath);

        [RelayCommand]
        private void ExtractHere() => ActionRequested?.Invoke("extractHere", _currentFilePath);

        [RelayCommand]
        private void ExtractTo() => ActionRequested?.Invoke("extractTo", _currentFilePath);

        [RelayCommand]
        private void OpenInNewTab() => ActionRequested?.Invoke("openInNewTab", _currentFilePath);

        [RelayCommand]
        private void OpenTerminal() => ActionRequested?.Invoke("openTerminal", _currentFilePath);

        [RelayCommand]
        private void ShowProperties() => ActionRequested?.Invoke("showProperties", _currentFilePath);

        /// <summary>
        /// Quick Look 윈도우를 닫도록 요청한다.
        /// </summary>
        public void Close()
        {
            CloseRequested?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try { _currentCts?.Cancel(); _currentCts?.Dispose(); } catch (ObjectDisposedException) { }

            // Dispose media source
            try { MediaSource?.Dispose(); } catch { }
            MediaSource = null;
        }
    }
}
