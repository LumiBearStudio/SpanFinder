using System.Collections.Generic;
using System.Linq;
using Span.ViewModels;

namespace Span.Helpers
{
    /// <summary>
    /// Shared drag/drop logic used by DetailsModeView, ListModeView, and IconModeView.
    /// </summary>
    public static class ViewDragDropHelper
    {
        /// <summary>
        /// Sets up drag data from selected items.
        /// Returns false if drag should be cancelled.
        /// </summary>
        public static bool SetupDragData(
            Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e,
            bool isRightPane)
        {
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0) return false;

            var paths = items.Select(i => i.Path).ToList();
            e.Data.SetText(string.Join("\n", paths));
            e.Data.Properties["SourcePaths"] = paths;
            e.Data.Properties["SourcePane"] = isRightPane ? "Right" : "Left";
            // Default to Copy for external drop targets (Windows Explorer, Desktop).
            // WinUI→Shell bridge defaults to Move when Move flag is present, ignoring
            // cross-drive convention. Internal Span drops use HandleDropAsync directly.
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;

            var capturedPaths = new List<string>(paths);
            e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });
            return true;
        }

        /// <summary>
        /// Provides StorageItems asynchronously for external app drops.
        /// </summary>
        public static async System.Threading.Tasks.Task ProvideStorageItemsAsync(
            Windows.ApplicationModel.DataTransfer.DataProviderRequest request,
            List<string> paths,
            Windows.ApplicationModel.DataTransfer.DataProviderDeferral deferral)
        {
            try
            {
                var storageItems = new List<Windows.Storage.IStorageItem>();
                foreach (var p in paths)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFolder.GetFolderFromPathAsync(p));
                        else if (System.IO.File.Exists(p))
                            storageItems.Add(await Windows.Storage.StorageFile.GetFileFromPathAsync(p));
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[DragDrop] StorageItem resolve failed ({p}): {ex.Message}");
                    }
                }
                request.SetData(storageItems);
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DragDrop] SetData error: {ex.Message}");
            }
            finally
            {
                deferral.Complete();
            }
        }
    }
}
