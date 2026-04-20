using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Span.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
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
        /// 동시 썸네일 로딩 제한 (디스크 I/O + 메모리 과부하 방지).
        /// SoftwareBitmapSource 사용으로 UI 스레드 부하는 극소 (~0.5ms).
        /// 백그라운드 디코딩 병렬도를 6으로 설정.
        /// </summary>
        private static readonly SemaphoreSlim _thumbnailThrottle = new(6, 6);

        private bool _thumbnailLoaded;
        private bool _thumbnailLoading;
        private CancellationTokenSource? _thumbnailCts;

        public FileViewModel(FileItem model) : base(model)
        {
        }

        /// <summary>
        /// .lnk 바로가기의 대상 파일 경로. 미리보기 패널에서 대상 파일 내용을 표시하는 데 사용.
        /// </summary>
        public string? LinkTargetPath { get; set; }

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

            // 진단: 동기 LogCrash로 강제 디스크 기록 (채널 flush 손실 회피)
            try
            {
                var dbgSettings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                Helpers.DebugLogger.LogCrash($"[Diag] LoadThumb ENTER {Name} isolated={dbgSettings?.UseIsolatedThumbnails} pid={Environment.ProcessId}", null);
            }
            catch { }

            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings != null && !settings.ShowThumbnails) return;
            }
            catch { return; }

            _thumbnailLoading = true;

            // 이전 로딩 취소 + 해제 후 새 CTS 생성 (타이머 누수 방지)
            var oldCts = _thumbnailCts;
            _thumbnailCts = null;
            oldCts?.Cancel();
            oldCts?.Dispose();
            var cts = new CancellationTokenSource();
            _thumbnailCts = cts;

            // 스크롤 디바운스: 빠른 스크롤 시 컨테이너 재활용 → CTS 취소 → 딜레이 중 리턴
            // 실제 I/O가 시작되기 전에 취소되므로 스크롤 성능에 영향 없음
            try
            {
                await Task.Delay(150, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _thumbnailLoading = false;
                return;
            }

            // 동시 로딩 제한 (디스크 I/O 과부하 방지)
            // 세마포어 대기 전 취소 확인 → 이미 취소된 토큰으로 WaitAsync 시 예외 방지
            if (cts.IsCancellationRequested) { _thumbnailLoading = false; return; }
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
            // Guard: 세마포어 대기 중 ResetThumbnail이 이 CTS를 Cancel+Dispose했을 수 있음
            if (cts.IsCancellationRequested)
            {
                _thumbnailThrottle.Release();
                _thumbnailLoading = false;
                return;
            }
            cts.CancelAfter(10000);
            SoftwareBitmap? softwareBitmap = null;
            try
            {
                // SemaphoreSlim 대기 중 이미 로드/취소되었을 수 있음
                if (_thumbnailLoaded || cts.IsCancellationRequested) return;

                var filePath = Path;

                // 파일 존재 여부 + 클라우드 상태를 백그라운드 스레드에서 확인
                var (exists, isCloudOnly) = await Task.Run(() =>
                    (File.Exists(filePath), Services.CloudSyncService.IsCloudOnlyFile(filePath)));
                if (!exists || cts.IsCancellationRequested) return;

                Helpers.DebugLogger.LogCrash($"[Diag] LoadThumb pre-isolated {Name} exists={exists} cloud={isCloudOnly}", null);

                // ── Phase 1: 워커 격리 경로 시도 (UseIsolatedThumbnails=true 시) ──
                // 워커가 PNG로 만들어 디스크 캐시 → BitmapImage(file://)로 단순 로딩.
                // 워커 미동작/실패 시 null 반환 → 기존 인프로세스 경로로 폴백.
                bool isolated = await TryLoadIsolatedAsync(filePath, decodePixelWidth, isCloudOnly, cts.Token);
                Helpers.DebugLogger.LogCrash($"[Diag] LoadThumb post-isolated {Name} result={isolated}", null);
                if (isolated)
                    return;

                // Video files & cloud-only files: use Shell thumbnail API
                // (videos can't be decoded via BitmapImage; cloud files must not trigger download)
                if (IsVideoFile || isCloudOnly)
                {
                    await LoadShellThumbnailAsync(filePath, decodePixelWidth, isCloudOnly, cts.Token);
                    return;
                }

                // Animated GIF: BitmapDecoder는 첫 프레임만 디코딩 → BitmapImage fallback (애니메이션 유지)
                var ext = System.IO.Path.GetExtension(filePath);
                if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase))
                {
                    await LoadBitmapImageFallbackAsync(filePath, decodePixelWidth, cts);
                    return;
                }

                // 백그라운드 스레드에서 이미지 디코딩 완료 (UI 스레드 부하 최소화)
                // FileStream → BitmapDecoder → SoftwareBitmap: byte[] 할당 없이 스트림 직접 디코딩
                // 주의: Task.Run에 CancellationToken 전달하지 않음 — 이미 취소된 토큰으로 Task 시작 시
                // OperationCanceledException이 대량 발생하여 성능 저하 (14000+ 파일 시나리오)
                softwareBitmap = await Task.Run(async () =>
                {
                    if (cts.IsCancellationRequested) return null;

                    var fi = new FileInfo(filePath);
                    if (fi.Length > 10 * 1024 * 1024) return null; // Skip files > 10MB

                    // FileShare.ReadWrite: 다른 프로세스(빌드 도구, 이미지 편집기 등)가
                    // 파일을 열고 있어도 읽기 가능 (IOException 0x80070020 방지)
                    using var fileStream = new System.IO.FileStream(
                        filePath, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                        System.IO.FileShare.ReadWrite);
                    using var ras = fileStream.AsRandomAccessStream();

                    var decoder = await BitmapDecoder.CreateAsync(ras);
                    if (cts.IsCancellationRequested) return null;

                    // 손상된 파일: 픽셀 크기 0이면 디코딩 불가
                    if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0) return null;

                    // 스케일 다운: 원본 비율 유지하며 decodePixelWidth에 맞춤
                    uint scaledWidth = (uint)decodePixelWidth;
                    uint scaledHeight = Math.Max(1,
                        (uint)(decodePixelWidth * decoder.PixelHeight / decoder.PixelWidth));

                    var transform = new BitmapTransform
                    {
                        ScaledWidth = scaledWidth,
                        ScaledHeight = scaledHeight,
                        InterpolationMode = BitmapInterpolationMode.Linear
                    };

                    var bmp = await decoder.GetSoftwareBitmapAsync(
                        BitmapPixelFormat.Bgra8,
                        BitmapAlphaMode.Premultiplied,
                        transform,
                        ExifOrientationMode.RespectExifOrientation,
                        ColorManagementMode.DoNotColorManage);

                    // SetBitmapAsync는 Bgra8+Premultiplied 필수 — 혹시 다른 포맷이면 변환
                    if (bmp.BitmapPixelFormat != BitmapPixelFormat.Bgra8
                        || bmp.BitmapAlphaMode != BitmapAlphaMode.Premultiplied)
                    {
                        SoftwareBitmap? converted = null;
                        try
                        {
                            converted = SoftwareBitmap.Convert(bmp, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
                        }
                        finally
                        {
                            bmp.Dispose(); // Convert 성공/실패 무관하게 원본 해제
                        }
                        return converted;
                    }
                    return bmp;
                });

                if (softwareBitmap == null || !_thumbnailLoading || cts.IsCancellationRequested) return;

                Helpers.DebugLogger.LogCrash($"[Diag] LoadThumb pre-SetBitmap {Name} pid={Environment.ProcessId}", null);

                // UI 스레드: SetBitmapAsync는 사전 디코딩된 버퍼 복사만 수행 (~0.5ms)
                var source = new SoftwareBitmapSource();
                await source.SetBitmapAsync(softwareBitmap);

                Helpers.DebugLogger.LogCrash($"[Diag] LoadThumb post-SetBitmap {Name}", null);
                softwareBitmap.Dispose();
                softwareBitmap = null;

                // Guard: 비동기 디코드 중 컨테이너 재활용/취소되었을 수 있음
                // source는 XAML에 아직 미등록 상태 → 안전하게 Dispose 가능
                if (!_thumbnailLoading || cts.IsCancellationRequested)
                {
                    source.Dispose();
                    return;
                }

                ThumbnailSource = source;
                _thumbnailLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] Thumbnail load failed for {Name}: {ex.Message}");
                // 썸네일 로딩 중 예상 가능한 에러는 Sentry 필터링:
                //   - WIC 디코딩 에러 (0x8898xxxx): 손상/비표준 이미지
                //   - 네트워크 에러 (0x80072EE7): 네트워크 공유 연결 끊김
                //   - UnauthorizedAccessException (0x80070005): 네트워크 공유 ACL 거부
                //   - FileNotFoundException / DirectoryNotFoundException: 목록 갱신 전 파일 삭제 레이스
                //   - IOException: 파일 잠금, 네트워크 공유 일시 장애 등
                bool isExpectedError =
                    (ex.HResult & unchecked((int)0xFFFF0000)) == unchecked((int)0x88980000)
                    || ex.HResult == unchecked((int)0x80072EE7)
                    || ex is UnauthorizedAccessException
                    || ex is FileNotFoundException
                    || ex is DirectoryNotFoundException
                    || ex is IOException;
                if (!isExpectedError)
                {
                    try { (App.Current.Services.GetService(typeof(Services.CrashReportingService)) as Services.CrashReportingService)?.CaptureException(ex, $"Thumbnail({Name})"); } catch { }
                }
            }
            finally
            {
                softwareBitmap?.Dispose();
                _thumbnailThrottle.Release();
                _thumbnailLoading = false;
            }
        }

        /// <summary>
        /// Animated GIF용 BitmapImage fallback.
        /// BitmapDecoder는 첫 프레임만 디코딩하므로, GIF는 BitmapImage로 애니메이션 유지.
        /// </summary>
        private async Task LoadBitmapImageFallbackAsync(string filePath, int decodePixelWidth, CancellationTokenSource cts)
        {
            try
            {
                byte[]? fileBytes = await Task.Run(() =>
                {
                    var fi = new FileInfo(filePath);
                    if (fi.Length > 10 * 1024 * 1024) return null;
                    return File.ReadAllBytes(filePath);
                });
                if (fileBytes == null || !_thumbnailLoading || cts.IsCancellationRequested) return;

                await Task.Yield(); // UI 스레드 양보
                if (!_thumbnailLoading || cts.IsCancellationRequested) return;

                var bitmap = new BitmapImage { DecodePixelWidth = decodePixelWidth, DecodePixelType = DecodePixelType.Logical };
                using var memStream = new MemoryStream(fileBytes);
                await bitmap.SetSourceAsync(memStream.AsRandomAccessStream()).AsTask(cts.Token);

                if (!_thumbnailLoading || cts.IsCancellationRequested) return;
                ThumbnailSource = bitmap;
                _thumbnailLoaded = true;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] GIF fallback failed for {Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Phase 1 — 격리 워커 경로로 썸네일 로딩 시도.
        /// 성공: BitmapImage(file://cache.png) 설정 후 true 반환.
        /// 실패/비활성화: false 반환 → 호출자가 인프로세스 경로 폴백.
        ///
        /// feature flag(UseIsolatedThumbnails) OFF 시 즉시 false (호출 비용 거의 0).
        /// 워커 spawn 실패/응답 실패 시에도 false → 사용자 영향 없음.
        /// </summary>
        private async Task<bool> TryLoadIsolatedAsync(string filePath, int decodePixelWidth, bool isCloudOnly, CancellationToken ct)
        {
            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings == null || !settings.UseIsolatedThumbnails) return false;

                // 회귀-GIF: 격리 경로는 PNG 첫 프레임만 굽기 → 애니메이션 멈춤.
                // 인프로세스 LoadBitmapImageFallbackAsync가 BitmapImage로 GIF 애니메이션 유지하므로
                // .gif는 즉시 인프로세스 폴백 강제.
                var ext = System.IO.Path.GetExtension(filePath);
                if (string.Equals(ext, ".gif", StringComparison.OrdinalIgnoreCase)) return false;

                var client = App.Current.Services.GetService(typeof(Services.Thumbnails.ThumbnailClientService))
                    as Services.Thumbnails.ThumbnailClientService;
                if (client == null) return false;

                // 메인이 알고 있는 컨텍스트 — Phase 1은 단순화 (theme/dpi는 메타로만 전달)
                string theme = "Default";
                uint dpi = 96;

                var uri = await client.GetThumbnailUriAsync(
                    filePath,
                    decodePixelWidth,
                    mode: "SingleItem",
                    isCloudOnly: isCloudOnly,
                    applyExif: true,
                    theme: theme,
                    dpi: dpi,
                    ct: ct).ConfigureAwait(true);

                if (uri == null) return false;
                if (!_thumbnailLoading || ct.IsCancellationRequested) return true;  // true 반환해도 OK — 폴백 안 함

                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.DecodePixelType = DecodePixelType.Logical;

                // 회귀-ImageFailed: 디코딩 실패 시 silent breakage 방지
                // (cache 파일이 사용자에 의해 삭제되거나 PNG가 손상된 케이스)
                bitmap.ImageFailed += (s, args) =>
                {
                    Helpers.DebugLogger.Log($"[Thumbnail] Isolated path ImageFailed for {Name}: {args.ErrorMessage}");
                    // 다음 visible-trigger에서 재시도 가능하게 플래그 되돌림
                    _thumbnailLoaded = false;
                };

                bitmap.UriSource = uri;

                if (!_thumbnailLoading || ct.IsCancellationRequested) return true;

                ThumbnailSource = bitmap;
                _thumbnailLoaded = true;
                return true;
            }
            catch (OperationCanceledException) { return true; }  // 취소는 폴백 X
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileViewModel] isolated path failed for {Name}: {ex.Message}");
                return false;  // 인프로세스 폴백
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
                    options).AsTask(ct);

                if (ct.IsCancellationRequested) return;

                if (thumbnail != null && thumbnail.Type == ThumbnailType.Image)
                {
                    // Guard: column may have been removed during async I/O
                    if (!_thumbnailLoading) return;

                    // UI 스레드 양보
                    await Task.Yield();
                    if (!_thumbnailLoading || ct.IsCancellationRequested) return;

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
                    await bitmap.SetSourceAsync(thumbnail).AsTask(ct);
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
            var oldCts = _thumbnailCts;
            _thumbnailCts = null;
            oldCts?.Cancel();
            oldCts?.Dispose();
            _thumbnailLoading = false;
            _thumbnailLoaded = false;
            var old = ThumbnailSource;
            ThumbnailSource = null;
            // 지연 해제: DirectComposition이 UI 스레드와 비동기로 렌더링하므로
            // ThumbnailSource = null 직후 Dispose하면 아직 사용 중인 D3D surface를 해제할 수 있음.
            // Low 우선순위 큐잉으로 Normal(렌더링) 작업 완료 후 Close → 안전하게 D3D 리소스 회수.
            // GC finalizer에만 의존하면 finalization 큐 적체(3790+) → D3D 고갈 → 크래시.
            if (old is IDisposable disposable)
                DeferDispose(disposable);
        }

        private static void DeferDispose(IDisposable disposable)
        {
            var dq = DispatcherQueue.GetForCurrentThread();
            if (dq != null)
                dq.TryEnqueue(DispatcherQueuePriority.Low,
                    () => { try { disposable.Dispose(); } catch { } });
            // dq가 null이면 앱 종료 중 — GC에 위임
        }
    }
}
