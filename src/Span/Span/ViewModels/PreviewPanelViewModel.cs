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
    /// Git 정보(Tier 1: 파일 최근 커밋, Tier 2: 폴더 레포 대시보드)를 병렬 로딩.
    /// </summary>
    public partial class PreviewPanelViewModel : ObservableObject, IDisposable
    {
        private readonly PreviewService _previewService;
        private readonly GitStatusService? _gitService;
        private readonly ISettingsService _settings;
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
        [NotifyPropertyChangedFor(nameof(IsHexBinaryVisible))]
        [NotifyPropertyChangedFor(nameof(IsFontVisible))]
        [NotifyPropertyChangedFor(nameof(IsGenericVisible))]
        private PreviewType _currentPreviewType = PreviewType.None;

        [ObservableProperty] private BitmapImage? _imagePreview;
        [ObservableProperty] private string? _textPreview;
        [ObservableProperty] private BitmapImage? _pdfPreview;
        [ObservableProperty] private MediaSource? _mediaSource;
        [ObservableProperty] private string? _hexPreview;
        [ObservableProperty] private string _fontFamilySource = "";
        [ObservableProperty] private string _fontFormat = "";

        // --- Git info (Tier 1: 파일 마지막 커밋) ---

        [ObservableProperty] private string _gitLastCommitInfo = "";
        [ObservableProperty] private bool _hasGitInfo;

        // --- Git dashboard (Tier 2: 폴더 Git 레포 대시보드) ---

        [ObservableProperty] private string _gitBranch = "";
        [ObservableProperty] private string _gitStatusSummary = "";
        [ObservableProperty] private string _gitRecentCommits = "";
        [ObservableProperty] private string _gitChangedFiles = "";
        [ObservableProperty] private bool _isGitRepo;

        // --- Computed visibility ---

        public bool IsImageVisible => CurrentPreviewType == PreviewType.Image;
        public bool IsTextVisible => CurrentPreviewType == PreviewType.Text;
        public bool IsPdfVisible => CurrentPreviewType == PreviewType.Pdf;
        public bool IsMediaVisible => CurrentPreviewType == PreviewType.Media;
        public bool IsFolderVisible => CurrentPreviewType == PreviewType.Folder;
        public bool IsHexBinaryVisible => CurrentPreviewType == PreviewType.HexBinary;
        public bool IsFontVisible => CurrentPreviewType == PreviewType.Font;
        public bool IsGenericVisible => CurrentPreviewType == PreviewType.Generic;

        public PreviewPanelViewModel(PreviewService previewService)
        {
            _previewService = previewService;
            _dispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();

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
                var contentTask = LoadContentAsync(previewType, item, ct);
                var gitTask = LoadGitInfoAsync(item, isFolder, ct);

                await Task.WhenAll(contentTask, gitTask);
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

        private async Task LoadContentAsync(PreviewType previewType, FileSystemViewModel item, CancellationToken ct)
        {
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

                case PreviewType.Generic:
                    break;
            }
        }

        /// <summary>
        /// Git 정보를 비동기로 로딩 (기존 미리보기와 병렬 실행).
        /// Tier 1: 파일 → git log -1
        /// Tier 2: 폴더 (Git 레포) → git status + git log 대시보드
        /// </summary>
        private async Task LoadGitInfoAsync(FileSystemViewModel item, bool isFolder, CancellationToken ct)
        {
            // Git 서비스 비활성 → 스킵
            if (_gitService == null)
            {
                HasGitInfo = false;
                IsGitRepo = false;
                return;
            }

            // Settings에서 런타임 체크 (토글이 바뀌었을 수 있음)
            if (_settings != null && !_settings.ShowGitIntegration)
            {
                HasGitInfo = false;
                IsGitRepo = false;
                return;
            }

            try
            {
                if (isFolder)
                {
                    // Tier 2: 폴더 Git 대시보드
                    await LoadGitDashboardAsync(item.Path, ct);
                }
                else
                {
                    // Tier 1: 파일 마지막 커밋
                    IsGitRepo = false;
                    await LoadGitLastCommitAsync(item.Path, ct);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewPanel] Git error: {ex.Message}");
                HasGitInfo = false;
                IsGitRepo = false;
            }
        }

        private async Task LoadGitLastCommitAsync(string filePath, CancellationToken ct)
        {
            var commit = await _gitService!.GetLastCommitAsync(filePath, ct);
            if (ct.IsCancellationRequested) return;

            if (commit != null)
            {
                GitLastCommitInfo = $"{commit.RelativeTime}\n{commit.Subject}\nby {commit.Author}";
                HasGitInfo = true;
            }
            else
            {
                GitLastCommitInfo = "";
                HasGitInfo = false;
            }
        }

        private async Task LoadGitDashboardAsync(string folderPath, CancellationToken ct)
        {
            var repoInfo = await _gitService!.GetRepoInfoAsync(folderPath, ct);
            if (ct.IsCancellationRequested) return;

            if (repoInfo != null)
            {
                IsGitRepo = true;
                GitBranch = repoInfo.Branch;

                // 상태 요약
                var parts = new StringBuilder();
                if (repoInfo.ModifiedCount > 0)
                    parts.Append($"{repoInfo.ModifiedCount}개 수정됨");
                if (repoInfo.StagedCount > 0)
                {
                    if (parts.Length > 0) parts.Append(", ");
                    parts.Append($"{repoInfo.StagedCount}개 스테이징됨");
                }
                if (repoInfo.UntrackedCount > 0)
                {
                    if (parts.Length > 0) parts.Append(", ");
                    parts.Append($"{repoInfo.UntrackedCount}개 미추적");
                }
                if (repoInfo.DeletedCount > 0)
                {
                    if (parts.Length > 0) parts.Append(", ");
                    parts.Append($"{repoInfo.DeletedCount}개 삭제됨");
                }
                GitStatusSummary = parts.Length > 0 ? parts.ToString() : "변경 사항 없음 (Clean)";

                // 최근 커밋
                if (repoInfo.RecentCommits.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var c in repoInfo.RecentCommits)
                    {
                        sb.AppendLine($"{c.Hash} ({c.RelativeTime})");
                        sb.AppendLine($"  {c.Subject}");
                    }
                    GitRecentCommits = sb.ToString().TrimEnd();
                }
                else
                {
                    GitRecentCommits = "";
                }

                // 변경 파일
                if (repoInfo.ChangedFiles.Count > 0)
                {
                    var sb = new StringBuilder();
                    foreach (var f in repoInfo.ChangedFiles.Take(20)) // 최대 20개
                    {
                        var marker = f.State switch
                        {
                            GitFileState.Modified => "M ",
                            GitFileState.Added => "A ",
                            GitFileState.Deleted => "D ",
                            GitFileState.Renamed => "R ",
                            GitFileState.Untracked => "? ",
                            GitFileState.Conflicted => "! ",
                            _ => "  ",
                        };
                        sb.AppendLine($"{marker} {f.Path}");
                    }
                    if (repoInfo.ChangedFiles.Count > 20)
                        sb.AppendLine($"  ... 외 {repoInfo.ChangedFiles.Count - 20}개");
                    GitChangedFiles = sb.ToString().TrimEnd();
                }
                else
                {
                    GitChangedFiles = "";
                }

                HasGitInfo = false; // 대시보드 모드에서는 Tier 1 섹션 숨김
            }
            else
            {
                IsGitRepo = false;
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
                if (!Services.FileSystemRouter.IsRemotePath(item.Path))
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
                // 원격 파일(FTP/SFTP): FileInfo로 로컬 읽기 불가 → 모델 데이터 사용
                if (Services.FileSystemRouter.IsRemotePath(item.Path))
                {
                    FileSizeFormatted = item.Size; // FileSystemViewModel.Size (이미 포맷됨)
                    DateCreated = "";              // FTP는 생성일자 미지원
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
            HexPreview = null;
            FontFamilySource = "";
            FontFormat = "";
            Dimensions = "";
            Duration = "";
            FolderItemCount = "";
            Artist = "";
            Album = "";

            // Git 정보 초기화
            GitLastCommitInfo = "";
            HasGitInfo = false;
            GitBranch = "";
            GitStatusSummary = "";
            GitRecentCommits = "";
            GitChangedFiles = "";
            IsGitRepo = false;
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
