using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace Span.ViewModels
{
    /// <summary>
    /// 파일 뷰모델. FileSystemViewModel을 상속하며 확장자 기반 아이콘 해상도,
    /// 비동기 썸네일 로딩(이미지/동영상), Shell API 폴백(클라우드 전용 파일) 기능을 제공.
    /// 동시 썸네일 로딩은 SemaphoreSlim(6)으로 제한.
    /// </summary>
    public class FileViewModel : FileSystemViewModel
    {
        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif"
        };

        private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".flv", ".3gp"
        };

        /// <summary>
        /// 동시 썸네일 로딩 제한 (Shell API 과부하 방지).
        /// 전역 제한: 최대 10개 동시 썸네일 로드.
        /// </summary>
        private static readonly SemaphoreSlim _thumbnailThrottle = new(10, 10);

        private bool _thumbnailLoaded;
        private bool _thumbnailLoading;
        private CancellationTokenSource? _thumbnailCts;

        public FileViewModel(FileItem model) : base(model)
        {
        }

        /// <summary>
        /// 확장자 기반 아이콘 (Segoe Fluent Icons)
        /// </summary>
        public override string IconGlyph => Services.IconService.Current?.GetIcon(((FileItem)_model).FileType) ?? "\uECE0";

        public override Microsoft.UI.Xaml.Media.Brush IconBrush => Services.IconService.Current?.GetBrush(((FileItem)_model).FileType);

        private bool IsImageFile => _imageExtensions.Contains(System.IO.Path.GetExtension(Name));
        private bool IsVideoFile => _videoExtensions.Contains(System.IO.Path.GetExtension(Name));

        public override bool IsThumbnailSupported => IsImageFile || IsVideoFile;

        /// <summary>
        /// Load thumbnail asynchronously. Called when item becomes visible.
        /// Decodes to a small size to minimize memory usage.
        /// For cloud-only files (iCloud, OneDrive, etc.), uses Shell cached thumbnails
        /// to avoid triggering file downloads.
        /// </summary>
        public async Task LoadThumbnailAsync(int decodePixelWidth = 96)
        {
            if (_thumbnailLoaded || _thumbnailLoading) return;
            if (!IsThumbnailSupported) return;

            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings != null && !settings.ShowThumbnails) return;
            }
            catch { return; }

            _thumbnailLoading = true;

            // 이전 로딩 취소 후 새 CTS 생성
            _thumbnailCts?.Cancel();
            var cts = new CancellationTokenSource();
            _thumbnailCts = cts;

            // 동시 로딩 제한 (Shell API 과부하 방지)
            // CancellationToken 전달: 재활용(스크롤 밖) 시 즉시 대기 포기 → 슬롯 낭비 방지
            try
            {
                await _thumbnailThrottle.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                _thumbnailLoading = false;
                return;
            }

            // 세마포어 획득 후 I/O 타임아웃 시작 (대기 시간 제외)
            cts.CancelAfter(10000);
            MemoryStream? memStream = null;
            try
            {
                // SemaphoreSlim 대기 중 이미 로드/취소되었을 수 있음
                if (_thumbnailLoaded || cts.IsCancellationRequested) return;

                var filePath = Path;

                // 파일 존재 여부 + 클라우드 상태를 백그라운드 스레드에서 확인
                var (exists, isCloudOnly) = await Task.Run(() =>
                    (File.Exists(filePath), Services.CloudSyncService.IsCloudOnlyFile(filePath)));
                if (!exists || cts.IsCancellationRequested) return;

                // Video files & cloud-only files: use Shell thumbnail API
                // (videos can't be decoded via BitmapImage; cloud files must not trigger download)
                if (IsVideoFile || isCloudOnly)
                {
                    await LoadShellThumbnailAsync(filePath, decodePixelWidth, isCloudOnly, cts.Token);
                    return;
                }

                // 파일 읽기를 백그라운드 스레드에서 수행하여 UI 스레드 차단 방지
                byte[]? fileBytes = await Task.Run(() =>
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Length > 20 * 1024 * 1024) return null; // Skip files > 20MB
                    return File.ReadAllBytes(filePath);
                });
                if (fileBytes == null || !_thumbnailLoading || cts.IsCancellationRequested) return;

                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.DecodePixelType = DecodePixelType.Logical;
                // 이미지 디코딩 실패 감지 — 클라우드/네트워크 에러는 Sentry 필터링
                bitmap.ImageFailed += (s, args) =>
                {
                    var msg = args.ErrorMessage;
                    Helpers.DebugLogger.Log($"[Thumbnail] ImageFailed({Name}): {msg}");
                    // E_NETWORK_ERROR 등 네트워크/클라우드 에러는 Sentry 노이즈 → 로그만
                    if (msg != null && (msg.Contains("NETWORK") || msg.Contains("0x80072") || msg.Contains("0x80070005")))
                        return;
                    var ex = msg != null ? new InvalidOperationException(msg) : null;
                    if (ex != null)
                    {
                        try { (App.Current.Services.GetService(typeof(Services.CrashReportingService)) as Services.CrashReportingService)?.CaptureException(ex, $"BitmapImage.ImageFailed({Name})"); } catch { }
                    }
                };

                Helpers.DebugLogger.Log($"[Thumbnail] SetSourceAsync START: {Name} ({fileBytes.Length} bytes)");
                // MemoryStream을 using 없이 생성 — SetSourceAsync 후 BitmapImage가 내부 참조할 수 있음
                memStream = new MemoryStream(fileBytes);
                var ras = memStream.AsRandomAccessStream();
                try
                {
                    await bitmap.SetSourceAsync(ras);
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[FileViewModel] SetSourceAsync failed for {Name}: {ex.Message}");
                    // WIC 네트워크/클라우드 에러 (0x88982F50 등)는 Sentry 필터링
                    bool isNetworkWicError = ex.HResult == unchecked((int)0x88982F50)
                        || ex.HResult == unchecked((int)0x80072EE7)
                        || ex.Message.Contains("NETWORK");
                    if (!isNetworkWicError)
                    {
                        try { (App.Current.Services.GetService(typeof(Services.CrashReportingService)) as Services.CrashReportingService)?.CaptureException(ex, $"SetSourceAsync({Name})"); } catch { }
                    }
                    return;
                }
                Helpers.DebugLogger.Log($"[Thumbnail] SetSourceAsync OK: {Name} (pixel={bitmap.PixelWidth}x{bitmap.PixelHeight})");

                // Guard: 비동기 디코드 중 컨테이너 재활용/취소되었을 수 있음
                if (!_thumbnailLoading || cts.IsCancellationRequested) return;

                ThumbnailSource = bitmap;
                _thumbnailLoaded = true;
                memStream = null; // bitmap이 소유 — dispose 하지 않음
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Thumbnail load failed for {Name}: {ex.Message}");
            }
            finally
            {
                memStream?.Dispose();
                _thumbnailThrottle.Release();
                _thumbnailLoading = false;
            }
        }

        /// <summary>
        /// Windows Shell API로 썸네일을 가져옴.
        /// 동영상: Shell이 프레임 캡처 썸네일 생성.
        /// 클라우드 전용: ReturnOnlyIfCached로 다운로드 방지, 캐시 없으면 스킵.
        /// </summary>
        private async Task LoadShellThumbnailAsync(string filePath, int decodePixelWidth, bool cacheOnly, CancellationToken ct)
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                if (ct.IsCancellationRequested) return;

                var options = cacheOnly
                    ? ThumbnailOptions.ReturnOnlyIfCached
                    : ThumbnailOptions.UseCurrentScale;

                using var thumbnail = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.SingleItem,
                    (uint)decodePixelWidth,
                    options);

                if (ct.IsCancellationRequested) return;

                if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                {
                    // Guard: column may have been removed during async I/O
                    if (!_thumbnailLoading) return;

                    var bitmap = new BitmapImage();
                    bitmap.DecodePixelWidth = decodePixelWidth;
                    bitmap.DecodePixelType = DecodePixelType.Logical;
                    bitmap.ImageFailed += (s, args) =>
                    {
                        var msg = args.ErrorMessage;
                        Helpers.DebugLogger.Log($"[Thumbnail] ImageFailed.Shell({Name}): {msg}");
                        if (msg != null && (msg.Contains("NETWORK") || msg.Contains("0x80072") || msg.Contains("0x80070005")))
                            return;
                        var ex = msg != null ? new InvalidOperationException(msg) : null;
                        if (ex != null)
                        {
                            try { (App.Current.Services.GetService(typeof(Services.CrashReportingService)) as Services.CrashReportingService)?.CaptureException(ex, $"BitmapImage.ImageFailed.Shell({Name})"); } catch { }
                        }
                    };

                    Helpers.DebugLogger.Log($"[Thumbnail] Shell SetSourceAsync START: {Name}");
                    await bitmap.SetSourceAsync(thumbnail);
                    Helpers.DebugLogger.Log($"[Thumbnail] Shell SetSourceAsync OK: {Name}");

                    // 비동기 디코드 후 취소 여부 재확인
                    if (!_thumbnailLoading || ct.IsCancellationRequested) return;

                    ThumbnailSource = bitmap;
                    _thumbnailLoaded = true;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Shell thumbnail failed for {Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Clear loaded thumbnail to free memory.
        /// Also resets the loading flag to prevent orphaned async tasks
        /// from writing back to this ViewModel after column removal.
        /// </summary>
        public void UnloadThumbnail()
        {
            _thumbnailCts?.Cancel();
            _thumbnailLoading = false;
            _thumbnailLoaded = false;
            ThumbnailSource = null;
        }
    }
}
