using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Span.Helpers
{
    /// <summary>
    /// Outbound drag for external apps.
    /// Eager StorageItems supports modern WinRT targets; deferred StorageItems
    /// preserves a fallback when resolving shell items fails.
    /// </summary>
    internal static class OutboundFileDragHelper
    {
        // 즉시 해석은 UI 스레드를 블로킹한다(DragItemsStarting). 느릴 수 있는 입력은 아예
        // 시도하지 않고 지연 공급자로 보내므로, 이 값은 "로컬 디스크가 평소보다 굼뜬 경우"만
        // 흡수하면 된다. 2초는 드래그 시작이 멈춘 것처럼 느껴진다.
        private static readonly TimeSpan ResolveTimeout = TimeSpan.FromMilliseconds(300);

        /// <summary>이 개수를 넘으면 즉시 해석을 시도하지 않는다(경로당 셸 호출 1회).</summary>
        private const int MaxEagerPaths = 32;

        public static void Populate(
            DataPackage data,
            IReadOnlyList<string> paths,
            bool skipArchivePaths = true)
        {
            var localPaths = new List<string>(paths.Count);
            var archivePaths = new List<string>();
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p))
                    continue;
                if (ArchivePathHelper.IsArchivePath(p))
                {
                    if (!skipArchivePaths)
                        archivePaths.Add(p);
                    continue;
                }
                if (File.Exists(p) || Directory.Exists(p))
                    localPaths.Add(p);
            }

            if (archivePaths.Count > 0)
            {
                localPaths.AddRange(archivePaths);
                if (localPaths.Count == 0)
                    return;
                RegisterDeferredStorageItems(data, localPaths);
                return;
            }

            if (localPaths.Count == 0)
                return;

            if (CanResolveEagerly(localPaths) && TrySetEagerStorageItems(data, localPaths))
                return;

            RegisterDeferredStorageItems(data, localPaths);
        }

        public static bool TryGetSourcePaths(DataPackageView view, out List<string> paths)
        {
            if (view.Properties.TryGetValue("SourcePaths", out var obj) && obj is List<string> list && list.Count > 0)
            {
                paths = list;
                return true;
            }

            paths = new List<string>();
            return false;
        }

        public static bool TryGetSourcePane(DataPackageView view, out string pane)
        {
            if (view.Properties.TryGetValue("SourcePane", out var obj) && obj is string value && !string.IsNullOrEmpty(value))
            {
                pane = value;
                return true;
            }

            pane = "";
            return false;
        }

        /// <summary>
        /// 즉시 해석을 시도해도 되는 입력인지 판단한다. 경로마다 셸 호출이 한 번씩 순차로
        /// 일어나고 그 동안 UI 스레드가 멈추므로, 빨리 끝나는 것이 확실한 경우에만 통과시킨다.
        /// 네트워크 공유나 절전 중인 디스크는 호출 하나가 수 초를 먹을 수 있어 제외한다.
        /// 여기서 걸러진 입력은 지연 공급자로 가며, 받는 쪽이 실제로 요청할 때 해석된다.
        /// </summary>
        private static bool CanResolveEagerly(List<string> paths)
        {
            if (paths.Count > MaxEagerPaths)
                return false;

            foreach (var p in paths)
            {
                // UNC는 드라이브 조회 없이 바로 제외한다.
                if (p.StartsWith(@"\\", StringComparison.Ordinal))
                    return false;
                try
                {
                    var root = Path.GetPathRoot(p);
                    if (string.IsNullOrEmpty(root))
                        return false;
                    // GetDriveType은 OS 캐시라 끊긴 네트워크 드라이브에서도 블로킹하지 않는다.
                    if (new DriveInfo(root).DriveType != DriveType.Fixed)
                        return false;
                }
                catch
                {
                    return false;
                }
            }

            return true;
        }

        private static bool TrySetEagerStorageItems(DataPackage data, List<string> localPaths)
        {
            List<IStorageItem>? storageItems = null;
            try
            {
                var task = Task.Run(() => ResolveStorageItems(localPaths));
                if (!task.Wait(ResolveTimeout))
                {
                    DebugLogger.Log("[DragDrop] Eager StorageItem resolve timed out");
                    return false;
                }

                storageItems = task.Result;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DragDrop] Eager StorageItem resolve failed: {ex.Message}");
                return false;
            }

            if (storageItems is not { Count: > 0 })
                return false;

            try
            {
                data.SetStorageItems(storageItems);
                DebugLogger.Log($"[DragDrop] Eager StorageItems set ({storageItems.Count})");
                return true;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DragDrop] SetStorageItems failed: {ex.Message}");
                return false;
            }
        }

        private static void RegisterDeferredStorageItems(DataPackage data, List<string> localPaths)
        {
            var captured = new List<string>(localPaths);
            data.SetDataProvider(StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, captured, deferral);
            });
        }

        private static List<IStorageItem> ResolveStorageItems(IReadOnlyList<string> paths)
        {
            var storageItems = new List<IStorageItem>();
            foreach (var p in paths)
            {
                try
                {
                    if (Directory.Exists(p))
                        storageItems.Add(StorageFolder.GetFolderFromPathAsync(p).AsTask().ConfigureAwait(false).GetAwaiter().GetResult());
                    else if (File.Exists(p))
                        storageItems.Add(StorageFile.GetFileFromPathAsync(p).AsTask().ConfigureAwait(false).GetAwaiter().GetResult());
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[DragDrop] StorageItem resolve failed ({p}): {ex.Message}");
                }
            }

            return storageItems;
        }

        private static async Task ProvideStorageItemsAsync(
            DataProviderRequest request,
            List<string> paths,
            DataProviderDeferral deferral)
        {
            try
            {
                paths = await Span.Services.Archive.ArchiveEntryStaging.MaterializeAsync(paths);
                var storageItems = new List<IStorageItem>();
                foreach (var p in paths)
                {
                    if (Directory.Exists(p))
                        storageItems.Add(await StorageFolder.GetFolderFromPathAsync(p));
                    else if (File.Exists(p))
                        storageItems.Add(await StorageFile.GetFileFromPathAsync(p));
                }
                request.SetData(storageItems);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DragDrop] StorageItems provider error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
