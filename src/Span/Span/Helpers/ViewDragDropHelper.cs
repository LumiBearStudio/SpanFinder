using System;
using System.Collections.Generic;
using System.Linq;
using Span.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace Span.Helpers
{
    /// <summary>
    /// Shared drag/drop logic used by Miller columns, DetailsModeView, ListModeView, and IconModeView.
    /// </summary>
    public static class ViewDragDropHelper
    {
        /// <summary>
        /// Sets up WinUI drag data from the items of a DragItemsStarting event.
        /// Returns false if there is nothing to drag.
        /// </summary>
        /// <param name="listView">Source list; needed for Explorer-style selection of the dragged item.</param>
        /// <param name="selectDraggedItem">
        /// Explorer: dragging an unselected item selects it (Ctrl adds it to the selection and
        /// drags the whole selection). Miller columns pass false because selecting there navigates.
        /// </param>
        public static bool SetupDragData(
            Microsoft.UI.Xaml.Controls.DragItemsStartingEventArgs e,
            bool isRightPane,
            Microsoft.UI.Xaml.Controls.ListViewBase? listView = null,
            bool selectDraggedItem = true)
        {
            var items = e.Items.OfType<FileSystemViewModel>().ToList();
            if (items.Count == 0)
                return false;

            if (listView != null && selectDraggedItem)
                items = EnsureDraggedItemsSelected(listView, items);

            PopulateDragData(e.Data, items, isRightPane);
            return true;
        }

        /// <summary>
        /// Fills a DataPackage for an internal file drag: SourcePaths/SourcePane for drops inside
        /// Span plus StorageItems for external targets.
        /// </summary>
        public static void PopulateDragData(DataPackage data, IReadOnlyList<FileSystemViewModel> items, bool isRightPane)
        {
            var paths = items.Select(i => i.Path).ToList();
            // 텍스트는 싣지 않는다 (외부 앱 파일 드롭 호환 — MainWindow.OnDragItemsStarting 참고)
            data.Properties["SourcePaths"] = paths;
            data.Properties["SourcePane"] = isRightPane ? "Right" : "Left";
            data.RequestedOperation = DataPackageOperation.Copy
                | DataPackageOperation.Move
                | DataPackageOperation.Link;
            OutboundFileDragHelper.Populate(data, paths, skipArchivePaths: false);
        }

        /// <summary>
        /// Explorer semantics for the drag start: a drag that begins on an item outside the
        /// selection selects that item (Ctrl held: adds it and drags the whole selection).
        /// Returns the items that should actually be dragged.
        /// </summary>
        public static List<FileSystemViewModel> EnsureDraggedItemsSelected(
            Microsoft.UI.Xaml.Controls.ListViewBase listView,
            List<FileSystemViewModel> items)
        {
            if (items.Count != 1)
                return items;

            var item = items[0];
            try
            {
                if (listView.SelectedItems.Contains(item))
                    return items;

                bool ctrl = (NativeMethods.GetAsyncKeyState(NativeMethods.VK_CONTROL) & 0x8000) != 0;
                if (!ctrl)
                    listView.SelectedItems.Clear();
                listView.SelectedItems.Add(item);

                if (ctrl && listView.SelectedItems.Count > 1)
                    return listView.SelectedItems.OfType<FileSystemViewModel>().ToList();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[DragDrop] Selecting dragged item failed: {ex.Message}");
            }
            return items;
        }

        /// <summary>
        /// Items to drag when a drag starts on <paramref name="pressed"/>: the whole selection when
        /// the item is part of it, otherwise just that item (selected first when requested).
        /// </summary>
        public static List<FileSystemViewModel> ResolveDragItems(
            Microsoft.UI.Xaml.Controls.ListViewBase listView,
            FileSystemViewModel pressed,
            bool selectDraggedItem)
        {
            try
            {
                if (listView.SelectedItems.Contains(pressed))
                    return listView.SelectedItems.OfType<FileSystemViewModel>().ToList();
            }
            catch { }

            var single = new List<FileSystemViewModel> { pressed };
            return selectDraggedItem ? EnsureDraggedItemsSelected(listView, single) : single;
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

        /// <summary>Nearest ListView/GridView ancestor of an element (e.g. an item template Grid).</summary>
        public static Microsoft.UI.Xaml.Controls.ListViewBase? FindOwningListView(Microsoft.UI.Xaml.DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Microsoft.UI.Xaml.Controls.ListViewBase lv) return lv;
                element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
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
    }
}
