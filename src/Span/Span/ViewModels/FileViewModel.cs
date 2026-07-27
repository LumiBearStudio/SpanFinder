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
        // Issue #56: .jfif는 JPEG 별칭 → WIC 네이티브 디코딩(완전 지원).
        // .psd는 "썸네일만" 지원 — 격리 워커(Span.Thumbs.exe)의 셸 썸네일 경로가
        // 서드파티 PSD 핸들러(Photoshop/무료 코덱)를 크래시 격리하에 호출한다.
        // .clip(CLIP STUDIO)은 격리 워커가 내장 SQLite 미리보기(CanvasPreview)를 직접
        // 추출한다(ClipThumbnailExtractor) — 셸 핸들러 불필요, 전 사용자 동작.
        // 핸들러/추출 실패 시 빈 아이콘(현상 유지)이라 손해 없음. 미리보기 패널
        // (PreviewService.ImageExtensions)에는 .psd/.clip을 넣지 않음 — 그쪽은 인프로세스
        // 경로라 서드파티 핸들러 크래시 리스크가 있어 격리된 썸네일 게이트에서만 연다.
        private static readonly HashSet<string> _imageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".ico", ".tiff", ".tif",
            ".jfif", ".psd", ".clip"
        };

        private static readonly HashSet<string> _videoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".m4v", ".flv", ".3gp"
        };

        /// <summary>
        /// Issue #41: 자체 리소스 아이콘을 표시할 실행형 파일 확장자.
        /// FolderIconService(IShellItemImageFactory)가 shell-associated 아이콘을 반환.
        /// </summary>
        private static readonly HashSet<string> _executableExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".lnk", ".msi", ".bat", ".cmd", ".ps1"
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
            // Issue #41: 실행형 파일(.exe/.lnk 등)은 shell-associated 아이콘 로드 요청.
            // FolderIconService를 재사용 — 파일 형식과 무관하게 IShellItemImageFactory가
            // 적절한 아이콘을 반환한다. 실패/OFF 시 기본 글리프 유지.
            RequestExecutableIconLoad();
        }

        private bool _executableIconRequested;

        /// <summary>
        /// Issue #41: .exe/.lnk 등의 자체 리소스 아이콘을 shell API로 lazy 로드.
        /// 중복 호출 방지 (_executableIconRequested 플래그).
        /// </summary>
        public void RequestExecutableIconLoad()
        {
            if (_executableIconRequested) return;
            if (string.IsNullOrEmpty(Path)) return;

            var ext = System.IO.Path.GetExtension(Name);
            if (string.IsNullOrEmpty(ext) || !_executableExtensions.Contains(ext)) return;

            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings == null || !settings.ExecutableIconsEnabled) return;

                var iconSvc = App.Current.Services.GetService(typeof(Services.FolderIconService)) as Services.FolderIconService;
                if (iconSvc == null) return;

                _executableIconRequested = true;
                _ = LoadExecutableIconAsync(iconSvc);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ExeIcon] RequestExecutableIconLoad failed for {Path}: {ex.Message}");
            }
        }

        /// <summary>
        /// Issue #41: 실행 파일은 48px로 요청. 256(jumbo)으로 요청하면 레거시 exe에서 셸이
        /// '창 프레임+작은 아이콘' 합성을 반환하지만, 48은 실제 아이콘 프레임을 반환하고
        /// ScaleUp이 캔버스를 꽉 채운다. 표시 슬롯(16~40px)에도 충분한 해상도.
        /// </summary>
        private const int ExecutableIconSizePx = 48;

        private async Task LoadExecutableIconAsync(Services.FolderIconService iconSvc)
        {
            try
            {
                var icon = await iconSvc.GetCustomIconAsync(Path, ExecutableIconSizePx).ConfigureAwait(false);
                if (icon == null) return;

                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings == null || !settings.ExecutableIconsEnabled) return;
                if (!_executableIconRequested) return;

                var dispatcher = iconSvc.GetUiDispatcher();
                if (dispatcher == null) return;

                var iconToSet = icon;
                dispatcher.TryEnqueue(() =>
                {
                    if (!_executableIconRequested) return;
                    CustomIcon = iconToSet;
                });
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ExeIcon] LoadExecutableIconAsync failed for {Path}: {ex.Message}");
            }
        }

        /// <summary>
        /// 설정 OFF 시 호출되어 캐시된 실행형 아이콘을 초기화.
        /// </summary>
        public void ClearExecutableIcon()
        {
            _executableIconRequested = false;
            CustomIcon = null;
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
            try
            {
                // SemaphoreSlim 대기 중 이미 로드/취소되었을 수 있음
                if (_thumbnailLoaded || cts.IsCancellationRequested) return;

                var filePath = Path;

                // 파일 존재 여부 + 클라우드 상태를 백그라운드 스레드에서 확인
                var (exists, isCloudOnly) = await Task.Run(() =>
                    (File.Exists(filePath), Services.CloudSyncService.IsCloudOnlyFile(filePath)));
                if (!exists || cts.IsCancellationRequested) return;

                // 워커 격리 경로 시도 (UseIsolatedThumbnails=true 시).
                // 워커가 PNG로 만들어 디스크 캐시 → BitmapImage(file://)로 단순 로딩.
                // 워커 미동작/실패 시 false 반환 → 기존 인프로세스 경로로 폴백.
                bool isolated = await TryLoadIsolatedAsync(filePath, decodePixelWidth, isCloudOnly, cts.Token);
                if (isolated)
                    return;

                // Video files & cloud-only files: use Shell thumbnail API
                // (videos can't be decoded via BitmapImage; cloud files must not trigger download)
                if (IsVideoFile || isCloudOnly)
                {
                    await LoadShellThumbnailAsync(filePath, decodePixelWidth, isCloudOnly, cts.Token);
                    return;
                }

                // v1.4.6: 이슈 #23 근본 수정 — 인프로세스 이미지 디코딩도 BitmapImage로 완전 원복.
                // 크래시 덤프 분석: SoftwareBitmapSource.SetBitmapAsync → AsyncCopyToSurfaceTask race → FailFast.
                // BitmapImage.SetSourceAsync는 다른 라이프사이클로 이 크래시를 피함 (v1.3.10 이전 검증된 경로).
                // GIF/비-GIF 구분 불필요 — BitmapImage는 GIF 애니메이션도 유지.
                byte[]? fileBytes = await Task.Run(() =>
                {
                    if (cts.IsCancellationRequested) return null;
                    var fi = new FileInfo(filePath);
                    if (fi.Length > 10 * 1024 * 1024) return null; // Skip files > 10MB
                    return File.ReadAllBytes(filePath);
                });
                if (fileBytes == null || !_thumbnailLoading || cts.IsCancellationRequested) return;

                await Task.Yield();
                if (!_thumbnailLoading || cts.IsCancellationRequested) return;

                var bitmap = new BitmapImage
                {
                    DecodePixelWidth = decodePixelWidth,
                    DecodePixelType = DecodePixelType.Logical
                };
                bitmap.ImageFailed += (s, args) =>
                {
                    Helpers.DebugLogger.Log($"[Thumbnail] In-process ImageFailed for {Name}: {args.ErrorMessage}");
                    _thumbnailLoaded = false;
                };
                using var memStream = new MemoryStream(fileBytes);
                await bitmap.SetSourceAsync(memStream.AsRandomAccessStream()).AsTask(cts.Token);

                if (!_thumbnailLoading || cts.IsCancellationRequested) return;
                ThumbnailSource = bitmap;
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
                _thumbnailThrottle.Release();
                _thumbnailLoading = false;
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

                // GIF: 격리 경로는 PNG 첫 프레임만 굽기 → 애니메이션 멈춤.
                // 인프로세스 BitmapImage 경로가 GIF 애니메이션 유지하므로 .gif는 인프로세스 폴백 강제.
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

                // v1.4.6: 이슈 #23 근본 수정 — SoftwareBitmapSource 경로 완전 원복.
                // 크래시 덤프 분석 결과 AsyncImageFactory::WorkCallback → AsyncCopyToSurfaceTask::CopyOperation
                // 에서 FailFast 확정. SoftwareBitmapSource.SetBitmapAsync가 GPU surface 복사 중 race로 죽음.
                // v1.3.10 설계(BitmapImage + UriSource)가 실제로 안전한 경로였음.
                var bitmap = new BitmapImage();
                bitmap.DecodePixelWidth = decodePixelWidth;
                bitmap.DecodePixelType = DecodePixelType.Logical;

                // cache 파일 손상/삭제 등 — 다음 visible-trigger에 재시도
                bitmap.ImageFailed += (s, args) =>
                {
                    Helpers.DebugLogger.Log($"[Thumbnail] Isolated path ImageFailed for {Name}: {args.ErrorMessage}");
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

                    // v1.4.6: 이슈 #23 근본 수정 — SoftwareBitmapSource 경로 완전 원복.
                    // AsyncCopyToSurfaceTask 크래시 회피. BitmapImage.SetSourceAsync는 다른 라이프사이클.
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
            //
            // v1.4.3: 이슈 #23 회귀 대응 — GIF 경로의 BitmapImage도 UriSource=null로 내부 참조 즉시 끊음.
            // BitmapImage는 IDisposable이 아니지만 UriSource 해제로 composition surface GC 가능 상태 전환.
            if (old is BitmapImage bi)
                bi.UriSource = null;
            else if (old is IDisposable disposable)
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
