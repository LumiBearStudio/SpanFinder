using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Span.Services
{
    /// <summary>
    /// desktop.ini 기반 커스텀 폴더 아이콘을 Windows Shell API(IShellItemImageFactory)로 추출하여
    /// WinUI ImageSource로 변환하는 서비스. 싱글톤.
    ///
    /// 안전 설계:
    /// - 전용 STA 단일 워커 스레드 (COM shell API 요구사항)
    /// - LRU 캐시 (512개 상한)
    /// - UI 스레드 블록 방지: 모든 추출은 백그라운드
    /// - 실패 시 null 반환 → FolderViewModel에서 기본 글리프 폴백
    /// - 이슈 #23 회피: ContextMenuService와 동일한 SoftwareBitmapSource 경로 (저볼륨 일회성 로드)
    /// </summary>
    public sealed class FolderIconService : IDisposable
    {
        // ── COM: IShellItem / IShellItemImageFactory ──

        [ComImport]
        [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, out IntPtr ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(SIZE size, int flags, out IntPtr phbm);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx; public int cy; }

        [Flags]
        private enum SIIGBF
        {
            ResizeToFit = 0x00,
            BiggerSizeOk = 0x01,
            MemoryOnly = 0x02,
            IconOnly = 0x04,
            ThumbnailOnly = 0x08,
            InCacheOnly = 0x10,
            ScaleUp = 0x100,
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

        // ── GDI: HBITMAP → BGRA8 ──

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAP
        {
            public int bmType;
            public int bmWidth;
            public int bmHeight;
            public int bmWidthBytes;
            public ushort bmPlanes;
            public ushort bmBitsPixel;
            public IntPtr bmBits;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        private const uint BI_RGB = 0;

        [DllImport("gdi32.dll")]
        private static extern int GetObjectW(IntPtr hObject, int nCount, ref BITMAP lpObject);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        private static extern int GetDIBits(IntPtr hdc, IntPtr hbmp, uint uStartScan, uint cScanLines,
            byte[] lpvBits, ref BITMAPINFOHEADER lpbi, uint uUsage);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        // ── 캐시 + 워커 ──

        private const int CacheCapacity = 512;
        private const int IconPixelSize = 48; // 일반 폴더 아이콘 크기

        private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

        private readonly object _cacheLock = new();
        private readonly Dictionary<string, ImageSource?> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<string> _lruOrder = new();

        private readonly BlockingCollection<WorkItem> _queue = new(new ConcurrentQueue<WorkItem>());
        private readonly Thread _worker;
        private readonly CancellationTokenSource _disposeCts = new();
        private DispatcherQueue? _uiDispatcher;

        private class WorkItem
        {
            public string Path = string.Empty;
            public TaskCompletionSource<ImageSource?> Tcs = null!;
            public CancellationToken Token;
        }

        public static FolderIconService? Current { get; private set; }

        public FolderIconService()
        {
            Current = this;

            _worker = new Thread(WorkerLoop)
            {
                Name = "FolderIconWorker",
                IsBackground = true
            };
            _worker.SetApartmentState(ApartmentState.STA);
            _worker.Start();
        }

        /// <summary>
        /// MainWindow 생성 후 UI DispatcherQueue를 설정. Initialize 전 호출은 안전하게 null 반환.
        /// </summary>
        public void Initialize(DispatcherQueue uiDispatcher)
        {
            _uiDispatcher = uiDispatcher;
        }

        /// <summary>
        /// 커스텀 아이콘을 비동기로 가져온다. 없거나 실패 시 null 반환.
        /// </summary>
        public Task<ImageSource?> GetCustomIconAsync(string folderPath, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(folderPath) || _uiDispatcher == null)
                return Task.FromResult<ImageSource?>(null);

            // 캐시 조회 (이미 처리된 경로면 즉시 반환, 실패 경로는 null 캐싱됨)
            lock (_cacheLock)
            {
                if (_cache.TryGetValue(folderPath, out var cached))
                {
                    TouchLru(folderPath);
                    return Task.FromResult(cached);
                }
            }

            var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var item = new WorkItem { Path = folderPath, Tcs = tcs, Token = ct };

            try
            {
                _queue.Add(item, _disposeCts.Token);
            }
            catch
            {
                tcs.TrySetResult(null);
            }

            return tcs.Task;
        }

        /// <summary>
        /// 특정 경로의 캐시 항목 제거 (desktop.ini 변경 등으로 갱신이 필요할 때).
        /// </summary>
        public void InvalidateCache(string folderPath)
        {
            lock (_cacheLock)
            {
                if (_cache.Remove(folderPath))
                    _lruOrder.Remove(folderPath);
            }
        }

        /// <summary>
        /// 전체 캐시 비우기 (설정 토글 OFF 등).
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _cache.Clear();
                _lruOrder.Clear();
            }
        }

        private void TouchLru(string key)
        {
            _lruOrder.Remove(key);
            _lruOrder.AddLast(key);
        }

        private void AddToCache(string key, ImageSource? value)
        {
            lock (_cacheLock)
            {
                if (_cache.ContainsKey(key))
                {
                    _cache[key] = value;
                    TouchLru(key);
                    return;
                }

                _cache[key] = value;
                _lruOrder.AddLast(key);

                while (_cache.Count > CacheCapacity && _lruOrder.First != null)
                {
                    var evictKey = _lruOrder.First.Value;
                    _lruOrder.RemoveFirst();
                    _cache.Remove(evictKey);
                }
            }
        }

        // ── STA 워커 ──

        private void WorkerLoop()
        {
            try
            {
                while (!_disposeCts.IsCancellationRequested)
                {
                    WorkItem item;
                    try
                    {
                        item = _queue.Take(_disposeCts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (item.Token.IsCancellationRequested)
                    {
                        item.Tcs.TrySetResult(null);
                        continue;
                    }

                    try
                    {
                        var (pixels, w, h) = ExtractShellIcon(item.Path);
                        if (pixels == null || w <= 0 || h <= 0)
                        {
                            AddToCache(item.Path, null);
                            item.Tcs.TrySetResult(null);
                            continue;
                        }

                        // UI 스레드로 마샬링해서 SoftwareBitmapSource 생성
                        var dispatcher = _uiDispatcher;
                        if (dispatcher == null)
                        {
                            AddToCache(item.Path, null);
                            item.Tcs.TrySetResult(null);
                            continue;
                        }
                        var localItem = item; // 클로저 캡처용
                        bool queued = dispatcher.TryEnqueue(async () =>
                        {
                            try
                            {
                                var source = await CreateBitmapSourceAsync(pixels, w, h);
                                AddToCache(localItem.Path, source);
                                localItem.Tcs.TrySetResult(source);
                            }
                            catch (Exception ex)
                            {
                                Helpers.DebugLogger.Log($"[FolderIconService] UI convert failed for {localItem.Path}: {ex.Message}");
                                AddToCache(localItem.Path, null);
                                localItem.Tcs.TrySetResult(null);
                            }
                        });
                        if (!queued)
                        {
                            // UI thread shutting down → TCS hangs forever if we skip
                            Helpers.DebugLogger.Log($"[FolderIconService] TryEnqueue failed (UI shutdown) for {item.Path}");
                            AddToCache(item.Path, null);
                            item.Tcs.TrySetResult(null);
                        }
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[FolderIconService] Worker error for {item.Path}: {ex.Message}");
                        AddToCache(item.Path, null);
                        item.Tcs.TrySetResult(null);
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FolderIconService] Worker loop fatal: {ex.Message}");
            }
            finally
            {
                // 워커 종료 시 대기 중인 모든 항목 완료 처리 (caller 데드락 방지)
                try
                {
                    while (_queue.TryTake(out var pending))
                    {
                        pending.Tcs.TrySetResult(null);
                    }
                }
                catch { }
            }
        }

        /// <summary>
        /// IShellItemImageFactory로 폴더 아이콘 HBITMAP을 추출하고 BGRA8 픽셀로 변환.
        /// STA 워커 스레드에서 호출됨.
        /// </summary>
        private (byte[]? pixels, int width, int height) ExtractShellIcon(string folderPath)
        {
            IShellItem? shellItem = null;
            IShellItemImageFactory? imageFactory = null;
            IntPtr hBitmap = IntPtr.Zero;

            try
            {
                if (!Directory.Exists(folderPath))
                    return (null, 0, 0);

                var riid = IID_IShellItem;
                SHCreateItemFromParsingName(folderPath, IntPtr.Zero, ref riid, out shellItem);
                if (shellItem == null)
                    return (null, 0, 0);

                imageFactory = shellItem as IShellItemImageFactory;
                if (imageFactory == null)
                    return (null, 0, 0);

                var size = new SIZE { cx = IconPixelSize, cy = IconPixelSize };
                int hr = imageFactory.GetImage(size, (int)(SIIGBF.IconOnly | SIIGBF.BiggerSizeOk), out hBitmap);
                if (hr != 0 || hBitmap == IntPtr.Zero)
                    return (null, 0, 0);

                return ExtractBitmapPixels(hBitmap);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FolderIconService] ExtractShellIcon failed for {folderPath}: {ex.Message}");
                return (null, 0, 0);
            }
            finally
            {
                try { if (hBitmap != IntPtr.Zero) DeleteObject(hBitmap); } catch { }
                try { if (imageFactory != null) Marshal.ReleaseComObject(imageFactory); } catch { }
                try { if (shellItem != null) Marshal.ReleaseComObject(shellItem); } catch { }
            }
        }

        /// <summary>
        /// HBITMAP → BGRA8 바이트 배열 추출. ShellContextMenu와 동일한 패턴.
        /// </summary>
        private static (byte[]? pixels, int width, int height) ExtractBitmapPixels(IntPtr hBitmap)
        {
            var bmp = new BITMAP();
            int bmpSize = Marshal.SizeOf<BITMAP>();
            if (GetObjectW(hBitmap, bmpSize, ref bmp) == 0)
                return (null, 0, 0);

            int w = bmp.bmWidth;
            int h = bmp.bmHeight;

            if (w <= 0 || h <= 0 || w > 512 || h > 512)
                return (null, 0, 0);

            var bih = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w,
                biHeight = -h, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            };

            byte[] pixels = new byte[w * h * 4];
            IntPtr hdc = IntPtr.Zero;

            try
            {
                hdc = CreateCompatibleDC(IntPtr.Zero);
                if (hdc == IntPtr.Zero)
                    return (null, 0, 0);

                int scanLines = GetDIBits(hdc, hBitmap, 0, (uint)h, pixels, ref bih, 0);
                if (scanLines == 0)
                    return (null, 0, 0);

                // 24비트 원본: 전체 알파 0이면 불투명 처리
                bool allAlphaZero = true;
                for (int i = 3; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0) { allAlphaZero = false; break; }
                }
                if (allAlphaZero)
                {
                    for (int i = 3; i < pixels.Length; i += 4)
                        pixels[i] = 0xFF;
                }

                return (pixels, w, h);
            }
            finally
            {
                if (hdc != IntPtr.Zero)
                    DeleteDC(hdc);
            }
        }

        /// <summary>
        /// BGRA8 → SoftwareBitmap → SoftwareBitmapSource. UI 스레드에서 호출 필수.
        /// </summary>
        private static async Task<ImageSource?> CreateBitmapSourceAsync(byte[] pixels, int w, int h)
        {
            var softwareBitmap = new Windows.Graphics.Imaging.SoftwareBitmap(
                Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                w, h,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied);
            softwareBitmap.CopyFromBuffer(pixels.AsBuffer());

            var source = new SoftwareBitmapSource();
            await source.SetBitmapAsync(softwareBitmap);
            return source;
        }

        public void Dispose()
        {
            try
            {
                _disposeCts.Cancel();
                _queue.CompleteAdding();

                // 대기 중인 항목들 즉시 완료 처리 (worker thread가 못 꺼내는 경우 방지)
                try
                {
                    while (_queue.TryTake(out var pending))
                    {
                        pending.Tcs.TrySetResult(null);
                    }
                }
                catch { }

                _worker.Join(TimeSpan.FromSeconds(2));
            }
            catch { }
            finally
            {
                try { _disposeCts.Dispose(); } catch { }
                try { _queue.Dispose(); } catch { }
            }
        }
    }
}
