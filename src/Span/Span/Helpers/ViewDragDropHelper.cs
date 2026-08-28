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
            // Must include all operations so that AcceptedOperation (set in DragOver) is
            // always a subset of RequestedOperation. Without Move flag, WinUI OLE layer
            // silently blocks Drop events when AcceptedOperation=Move (same-drive default).
            e.Data.RequestedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy
                | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Move
                | Windows.ApplicationModel.DataTransfer.DataPackageOperation.Link;

            var capturedPaths = new List<string>(paths);
            e.Data.SetDataProvider(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems, request =>
            {
                var deferral = request.GetDeferral();
                _ = ProvideStorageItemsAsync(request, capturedPaths, deferral);
            });
            return true;
        }

        /// <summary>
        /// ListView/GridView에서 커서 위치의 FolderViewModel을 찾는다.
        /// ItemsPanelRoot의 실현된 컨테이너를 순회하며 bounds를 비교한다.
        /// (FindElementsInHostCoordinates는 WinUI 3 드래그 중 작동하지 않음)
        /// </summary>
        public static ViewModels.FolderViewModel? FindFolderAtPoint(
            Microsoft.UI.Xaml.Controls.ListViewBase listView,
            Windows.Foundation.Point pos,
            ViewModels.FolderViewModel? excludeFolder)
        {
            var panel = listView.ItemsPanelRoot;
            if (panel == null) return null;

            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(panel);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(panel, i);
                // ListView → ListViewItem, GridView → GridViewItem. 공통 부모: SelectorItem
                if (child is not Microsoft.UI.Xaml.Controls.Primitives.SelectorItem container) continue;

                var transform = container.TransformToVisual(listView);
                var bounds = transform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0, container.ActualWidth, container.ActualHeight));

                if (bounds.Contains(pos))
                {
                    var item = listView.ItemFromContainer(container);
                    if (item is ViewModels.FolderViewModel fvm && fvm != excludeFolder)
                        return fvm;
                    return null; // 항목은 찾았으나 폴더가 아님 (파일)
                }
            }
            return null;
        }

        /// <summary>
        /// FolderViewModel에 해당하는 ListViewItem의 DataTemplate 루트 Grid를 찾는다.
        /// </summary>
        public static Microsoft.UI.Xaml.Controls.Grid? FindItemGrid(
            Microsoft.UI.Xaml.Controls.ListViewBase listView,
            ViewModels.FolderViewModel folder)
        {
            var container = listView.ContainerFromItem(folder) as Microsoft.UI.Xaml.Controls.Primitives.SelectorItem;
            if (container == null) return null;
            // ContentPresenter의 첫 번째 자식이 DataTemplate 루트 Grid
            var presenter = FindChild<Microsoft.UI.Xaml.Controls.ContentPresenter>(container);
            if (presenter != null && Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(presenter) > 0)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(presenter, 0);
                if (child is Microsoft.UI.Xaml.Controls.Grid grid) return grid;
            }
            return null;
        }

        private static T? FindChild<T>(Microsoft.UI.Xaml.DependencyObject parent) where T : Microsoft.UI.Xaml.DependencyObject
        {
            int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T t) return t;
                var result = FindChild<T>(child);
                if (result != null) return result;
            }
            return null;
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
                // Issue #64: archive:// 항목을 임시 파일로 꺼낸 뒤 실제 경로를 넘긴다.
                // 이전에는 File.Exists가 false라 조용히 누락됐다. 지연 콜백이라
                // 상대 앱이 데이터를 요청할 때만 비용이 든다.
                paths = await Span.Services.Archive.ArchiveEntryStaging.MaterializeAsync(paths);

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
