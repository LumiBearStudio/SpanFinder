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
        private static readonly TimeSpan ResolveTimeout = TimeSpan.FromMilliseconds(2000);

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

            if (TrySetEagerStorageItems(data, localPaths))
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
