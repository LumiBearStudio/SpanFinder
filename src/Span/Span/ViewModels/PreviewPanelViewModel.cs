using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using Span.Services;
using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Core;

namespace Span.ViewModels
{
    /// <summary>
    /// 미리보기 패널 뷰모델. 파일 선택 시 200ms 디바운싱 후 유형별 미리보기를 로딩.
    /// Image/Text/PDF/Media/Hex/Font/Folder/Generic 프리뷰와 파일 메타데이터(크기/날짜),
    /// Git 정보(Tier 1: 파일 최근 커밋)를 병렬 로딩. Tier 2(폴더 대시보드)는 v1.0.9 상태바로 이동.
    /// </summary>
    public partial class PreviewPanelViewModel : ObservableObject, IDisposable
    {
        private readonly PreviewService _previewService;
        private readonly GitStatusService? _gitService;
        private readonly ArchiveReaderService? _archiveReader;
        private readonly ISettingsService _settings;
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _currentCts;
        private CancellationTokenSource? _hashCts;
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
        [NotifyPropertyChangedFor(nameof(IsHexBinaryVisible))]
        [NotifyPropertyChangedFor(nameof(IsFontVisible))]
        [NotifyPropertyChangedFor(nameof(IsGenericVisible))]
        [NotifyPropertyChangedFor(nameof(IsArchiveVisible))]
        private PreviewType _currentPreviewType = PreviewType.None;

        [ObservableProperty] private BitmapImage? _imagePreview;
        [ObservableProperty] private string? _textPreview;
        [ObservableProperty] private string _textFileExtension = "";
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

        // --- Git info (Tier 1: 파일 마지막 커밋) ---

        [ObservableProperty] private string _gitLastCommitInfo = "";
        [ObservableProperty] private bool _hasGitInfo;

        // --- File Hash ---

        [ObservableProperty] private string _fileHashText = "";
        [ObservableProperty] private bool _isHashCalculating;
        [ObservableProperty] private bool _showHashSection;

        // --- Computed visibility ---

        public bool IsImageVisible => CurrentPreviewType == PreviewType.Image;
        public bool IsTextVisible => CurrentPreviewType == PreviewType.Text
            || CurrentPreviewType == PreviewType.Markdown
            || CurrentPreviewType == PreviewType.Csv;
        public bool IsPdfVisible => CurrentPreviewType == PreviewType.Pdf;
        public bool IsMediaVisible => CurrentPreviewType == PreviewType.Media;
        public bool IsFolderVisible => CurrentPreviewType == PreviewType.Folder;
        public bool IsHexBinaryVisible => CurrentPreviewType == PreviewType.HexBinary;
        public bool IsFontVisible => CurrentPreviewType == PreviewType.Font;
        public bool IsGenericVisible => CurrentPreviewType == PreviewType.Generic;
        public bool IsArchiveVisible => CurrentPreviewType == PreviewType.Archive;

        public PreviewPanelViewModel(PreviewService previewService)
        {
            _previewService = previewService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

            // Archive reader (optional)
            _archiveReader = App.Current.Services.GetService<ArchiveReaderService>();

            // Git 서비스 (optional — ShowGitIntegration이 꺼져 있으면 null)
            try
            {
                _settings = App.Current.Services.GetRequiredService<ISettingsService>();
                if (_settings.ShowGitIntegration)
                {
                    _gitService = App.Current.Services.GetService<GitStatusService>();
                    if (_gitService != null && !_gitService.IsAvailable)
                        _gitService = null; // git.exe 미설치
                }
            }
            catch
            {
                _settings = null!;
            }
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
                    try
                    {
                        if (!_disposed)
                            await UpdatePreviewAsync(selectedItem);
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[PreviewPanel] UpdatePreview error: {ex.Message}");
                    }
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
                // .lnk 바로가기가 파일을 가리키면 대상 파일의 미리보기 표시
                var previewPath = item.Path;
                if (item is FileViewModel fvm && !string.IsNullOrEmpty(fvm.LinkTargetPath))
                    previewPath = fvm.LinkTargetPath;
                bool isFolder = item is FolderViewModel;
                var previewType = _previewService.GetPreviewType(previewPath, isFolder);
                if (previewType == PreviewType.HexBinary && _settings != null && !_settings.ShowHexPreview)
                    previewType = PreviewType.Generic;

                // Cloud-only files: avoid triggering download for text/pdf/hex
                // Image/Media는 허용 — 이미지는 캐시 썸네일, 미디어는 접근 시 자동 다운로드
                if (!isFolder && previewType != PreviewType.Image && previewType != PreviewType.Media
                    && previewType != PreviewType.Generic && previewType != PreviewType.Folder
                    && Services.CloudSyncService.IsCloudOnlyFile(item.Path))
                {
                    previewType = PreviewType.Generic;
                }
                ClearPreviewContent();
                CurrentPreviewType = previewType;

                ct.ThrowIfCancellationRequested();

                // 3. Content loading + Git info (병렬)
                var contentTask = LoadContentAsync(previewType, item, previewPath, ct);
                var gitTask = LoadGitInfoAsync(item, isFolder, ct);
                var hashTask = LoadFileHashAsync(item, isFolder, ct);

                await Task.WhenAll(contentTask, gitTask, hashTask);
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

        private async Task LoadContentAsync(PreviewType previewType, FileSystemViewModel item, string previewPath, CancellationToken ct)
        {
            switch (previewType)
            {
                case PreviewType.Folder:
                    await LoadFolderInfoAsync(item, ct);
                    break;

                case PreviewType.Image:
                    ImagePreview = await _previewService.LoadImagePreviewAsync(previewPath, 1024, ct);
                    var imgMeta = await _previewService.GetImageMetadataAsync(previewPath, ct);
                    if (imgMeta != null)
                        Dimensions = $"{imgMeta.Width} x {imgMeta.Height}";
                    break;

                case PreviewType.Text:
                    TextFileExtension = System.IO.Path.GetExtension(previewPath)?.ToLowerInvariant() ?? "";
                    TextPreview = await _previewService.LoadTextPreviewAsync(previewPath, ct);
                    break;

                case PreviewType.Pdf:
                    PdfPreview = await _previewService.LoadPdfPreviewAsync(previewPath, ct);
                    break;

                case PreviewType.Media:
                    MediaSource = await _previewService.LoadMediaSourceAsync(previewPath, ct);
                    var mediaMeta = await _previewService.GetMediaMetadataAsync(previewPath, ct);
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

                case PreviewType.HexBinary:
                    HexPreview = await _previewService.LoadHexPreviewAsync(previewPath, ct);
                    break;

                case PreviewType.Font:
                    var fontData = _previewService.GetFontPreviewData(previewPath);
                    if (fontData != null)
                    {
                        FontFamilySource = fontData.FamilyName;
                        FontFormat = fontData.Extension;
                    }
                    break;

                case PreviewType.Archive:
                    await LoadArchiveInfoAsync(previewPath, ct);
                    break;

                case PreviewType.Markdown:
                case PreviewType.Csv:
                    // 사이드 패널에서는 텍스트로 폴백 표시
                    TextFileExtension = System.IO.Path.GetExtension(previewPath)?.ToLowerInvariant() ?? "";
                    TextPreview = await _previewService.LoadTextPreviewAsync(previewPath, ct);
                    break;

                case PreviewType.Generic:
                    break;
            }
        }

        /// <summary>
        /// Git 정보를 비동기로 로딩 (기존 미리보기와 병렬 실행).
        /// Tier 1: 파일 → git log -1 (폴더는 스킵 — Tier 2는 v1.0.9에서 상태바로 이동)
        /// </summary>
        private async Task LoadGitInfoAsync(FileSystemViewModel item, bool isFolder, CancellationToken ct)
        {
            if (_gitService == null || isFolder) return;
            if (!_settings.ShowGitIntegration) return;

            try
            {
                HasGitInfo = false;
                await LoadGitLastCommitAsync(item.Path, ct);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Preview] Git info failed: {ex.Message}");
            }
        }

        private async Task LoadGitLastCommitAsync(string filePath, CancellationToken ct)
        {
            var commit = await _gitService!.GetLastCommitAsync(filePath, ct);
            if (ct.IsCancellationRequested) return;

            if (commit != null)
            {
                GitLastCommitInfo = $"{commit.RelativeTime}\n{commit.Subject}\n{commit.Author}";
                HasGitInfo = true;
            }
            else
            {
                GitLastCommitInfo = "";
                HasGitInfo = false;
            }
        }

        private async Task LoadFileHashAsync(FileSystemViewModel item, bool isFolder, CancellationToken ct)
        {
            // Settings OFF or folder → hide
            if (!(_settings?.ShowFileHash ?? false) || isFolder)
            {
                ShowHashSection = false;
                FileHashText = "";
                return;
            }

            // Remote/archive → skip
            if (Services.FileSystemRouter.IsRemotePath(item.Path)
                || Helpers.ArchivePathHelper.IsArchivePath(item.Path))
            {
                ShowHashSection = false;
                return;
            }

            // Too large → skip hash
            try
            {
                var fileInfo = new System.IO.FileInfo(item.Path);
                if (fileInfo.Length > 100 * 1024 * 1024)
                {
                    ShowHashSection = true;
                    IsHashCalculating = false;
                    FileHashText = "> 100 MB";
                    return;
                }
            }
            catch
            {
                ShowHashSection = false;
                return;
            }

            // Cancel previous hash
            _hashCts?.Cancel();
            var hashCts = new CancellationTokenSource();
            _hashCts = hashCts;
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, hashCts.Token);
            var hashCt = linkedCts.Token;

            try
            {
                ShowHashSection = true;
                IsHashCalculating = true;
                FileHashText = "";

                var filePath = item.Path;
                var hash = await Task.Run(async () =>
                {
                    const int bufferSize = 65536;
                    using var sha256 = System.Security.Cryptography.SHA256.Create();
                    using var stream = new System.IO.FileStream(
                        filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                        System.IO.FileShare.Read, bufferSize, useAsync: true);

                    var buffer = new byte[bufferSize];
                    int read;
                    while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, hashCt)) > 0)
                    {
                        sha256.TransformBlock(buffer, 0, read, null, 0);
                    }
                    sha256.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    return Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
                }, hashCt);

                if (!hashCt.IsCancellationRequested)
                {
                    FileHashText = hash;
                    IsHashCalculating = false;
                }
            }
            catch (OperationCanceledException) { /* normal cancellation */ }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Preview] Hash error: {ex.Message}");
                ShowHashSection = false;
                IsHashCalculating = false;
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
                // 원격/아카이브 파일: FileInfo로 로컬 읽기 불가 → 모델 데이터 사용
                if (Services.FileSystemRouter.IsRemotePath(item.Path)
                    || Helpers.ArchivePathHelper.IsArchivePath(item.Path))
                {
                    FileSizeFormatted = item.Size; // FileSystemViewModel.Size (이미 포맷됨)
                    DateCreated = "";              // 아카이브/원격은 생성일자 미지원
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

        private async Task LoadFolderInfoAsync(FileSystemViewModel item, CancellationToken ct)
        {
            var path = item.Path;
            int count;

            if (Helpers.ArchivePathHelper.IsArchivePath(path)
                || Services.FileSystemRouter.IsRemotePath(path))
            {
                // For archive/remote folders, use the provider to get item count
                try
                {
                    var router = App.Current.Services.GetRequiredService<Services.FileSystemRouter>();
                    var provider = router.GetProvider(path);
                    var items = await provider.GetItemsAsync(path, ct);
                    count = items.Count;
                }
                catch
                {
                    count = 0;
                }
            }
            else
            {
                count = _previewService.GetFolderItemCount(path);
            }

            var loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();
            FolderItemCount = string.Format(loc.Get("FolderItemCount"), count);
        }

        private async Task LoadArchiveInfoAsync(string path, CancellationToken ct)
        {
            if (_archiveReader == null) return;

            try
            {
                var info = await _archiveReader.GetArchiveInfoAsync(path, ct);
                if (ct.IsCancellationRequested) return;

                var loc = App.Current.Services.GetRequiredService<Services.LocalizationService>();

                // TotalFiles == -1 means archive is unreadable (corrupted, password-protected, etc.)
                if (info.TotalFiles < 0)
                {
                    ArchiveStats = loc.Get("Preview_ArchiveError") ?? "Cannot read archive (corrupted or password-protected)";
                    ArchiveCompressedSize = FormatFileSize(info.CompressedSize);
                    ArchiveUncompressedSize = "-";
                    ArchiveCompressionRatio = "-";
                    ArchiveContentTree = "";
                    return;
                }

                ArchiveStats = string.Format(loc.Get("Preview_ArchiveFiles"),
                    info.TotalFiles.ToString("N0"), info.TotalFolders.ToString("N0"));
                ArchiveCompressedSize = FormatFileSize(info.CompressedSize);
                ArchiveUncompressedSize = FormatFileSize(info.UncompressedSize);
                ArchiveCompressionRatio = info.CompressionRatio > 0
                    ? $"{info.CompressionRatio:F1}%"
                    : "-";

                // Build tree text — use tree-style lines for readability
                var sb = new StringBuilder();
                var entries = info.TopEntries;
                for (int i = 0; i < entries.Count; i++)
                {
                    var entry = entries[i];
                    var indent = entry.Depth > 0
                        ? new string(' ', (entry.Depth - 1) * 2) + "└ "
                        : "";

                    if (entry.IsDirectory)
                        sb.AppendLine($"{indent}📁 {entry.Name}  ({entry.ChildCount})");
                    else
                        sb.AppendLine($"{indent}📄 {entry.Name}  {FormatFileSize(entry.Size)}");
                }

                if (info.TotalFiles + info.TotalFolders > entries.Count)
                {
                    var remaining = info.TotalFiles + info.TotalFolders - entries.Count;
                    sb.AppendLine(string.Format(LocalizationService.L("QuickLook_MoreItems"), remaining.ToString("N0")));
                }

                ArchiveContentTree = sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Preview] Archive info error: {ex.Message}");
                ArchiveContentTree = LocalizationService.L("Preview_ErrorReadingArchive");
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
            // Null out MediaSource before disposing to unbind from UI
            var oldMedia = MediaSource;
            MediaSource = null;
            oldMedia?.Dispose();



            ImagePreview = null;
            TextPreview = null;
            PdfPreview = null;
            HexPreview = null;
            FontFamilySource = "";
            FontFormat = "";
            Dimensions = "";
            Duration = "";
            FolderItemCount = "";
            Artist = "";
            Album = "";

            // Archive 정보 초기화
            ArchiveContentTree = "";
            ArchiveStats = "";
            ArchiveCompressedSize = "";
            ArchiveUncompressedSize = "";
            ArchiveCompressionRatio = "";

            // Git 정보 초기화
            GitLastCommitInfo = "";
            HasGitInfo = false;

            // Hash 정보 초기화
            _hashCts?.Cancel();
            FileHashText = "";
            ShowHashSection = false;
            IsHashCalculating = false;
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
            try { _currentCts?.Cancel(); _currentCts?.Dispose(); } catch (ObjectDisposedException) { }
            try { _hashCts?.Cancel(); _hashCts?.Dispose(); } catch (ObjectDisposedException) { }
            _debounceTimer?.Dispose();
            ClearPreviewContent();
        }
    }
}
