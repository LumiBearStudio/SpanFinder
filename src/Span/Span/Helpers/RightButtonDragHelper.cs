using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Foundation;

namespace Span.Helpers
{
    /// <summary>
    /// Windows Explorer style right-button drag for ListView/GridView items.
    ///
    /// WinUI's built-in item drag (CanDragItems) only reacts to the left mouse button.
    /// This helper watches the right button on item containers and starts a manual drag
    /// (UIElement.StartDragAsync) once the pointer moved past the drag threshold.
    /// The drop side recognises such a drag via DataPackage.Properties["RightButtonDrag"]
    /// and shows the Explorer menu (Copy here / Move here / Create shortcuts here / Cancel).
    /// </summary>
    internal sealed class RightButtonDragHelper
    {
        /// <summary>Marker property in the drag DataPackage.</summary>
        public const string RightButtonDragProperty = "RightButtonDrag";

        /// <summary>Pointer movement (px) before a press turns into a drag (SM_CXDRAG default).</summary>
        private const double DragThreshold = 4.0;

        private readonly ListViewBase _listView;
        private readonly bool _selectDraggedItem;
        private readonly Func<IReadOnlyList<FileSystemViewModel>, DataPackage, bool> _onDragStarting;
        private readonly Action _onDragCompleted;

        private bool _detached;
        private bool _tracking;
        private bool _dragging;
        private uint _pointerId;
        private Point _origin;
        private FileSystemViewModel? _pressedItem;

        /// <param name="listView">ListView/GridView whose items can be dragged with the right button.</param>
        /// <param name="selectDraggedItem">
        /// Explorer semantics: dragging an unselected item selects it first. Miller columns pass
        /// false because selecting a folder there navigates into it.
        /// </param>
        /// <param name="onDragStarting">Fills the DataPackage for the given items; return false to cancel.</param>
        /// <param name="onDragCompleted">Called once the drag finished (drop or cancel).</param>
        public RightButtonDragHelper(
            ListViewBase listView,
            bool selectDraggedItem,
            Func<IReadOnlyList<FileSystemViewModel>, DataPackage, bool> onDragStarting,
            Action onDragCompleted)
        {
            _listView = listView;
            _selectDraggedItem = selectDraggedItem;
            _onDragStarting = onDragStarting;
            _onDragCompleted = onDragCompleted;

            // handledEventsToo: ListViewItem marks pointer events handled.
            _listView.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(OnPointerPressed), true);
            _listView.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointerMoved), true);
            _listView.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
            _listView.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler(OnPointerCaptureLost), true);
            _listView.DragStarting += OnDragStarting;
        }

        public void Detach()
        {
            _detached = true;
            _tracking = false;
            try
            {
                _listView.RemoveHandler(UIElement.PointerPressedEvent, (PointerEventHandler)OnPointerPressed);
                _listView.RemoveHandler(UIElement.PointerMovedEvent, (PointerEventHandler)OnPointerMoved);
                _listView.RemoveHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)OnPointerReleased);
                _listView.RemoveHandler(UIElement.PointerCaptureLostEvent, (PointerEventHandler)OnPointerCaptureLost);
                _listView.DragStarting -= OnDragStarting;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RightDrag] Detach failed: {ex.Message}");
            }
        }

        /// <summary>True when the DataPackage was produced by a right-button drag.</summary>
        public static bool IsRightButtonDrag(DataPackageView view)
        {
            try
            {
                return view.Properties.TryGetValue(RightButtonDragProperty, out var value) && value is bool b && b;
            }
            catch
            {
                return false;
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_detached || _dragging) return;
            try
            {
                _tracking = false;
                if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse) return;

                var point = e.GetCurrentPoint(_listView);
                var props = point.Properties;
                if (!props.IsRightButtonPressed || props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
                    return;

                var container = FindContainer(e.OriginalSource as DependencyObject);
                if (container == null) return;
                if (_listView.ItemFromContainer(container) is not FileSystemViewModel item) return;

                _pressedItem = item;
                _pointerId = e.Pointer.PointerId;
                _origin = point.Position;
                _tracking = true;
            }
            catch (Exception ex)
            {
                _tracking = false;
                DebugLogger.Log($"[RightDrag] PointerPressed failed: {ex.Message}");
            }
        }

        private async void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_detached || !_tracking || _dragging) return;
            if (e.Pointer.PointerId != _pointerId) return;

            PointerPoint point;
            try
            {
                point = e.GetCurrentPoint(_listView);
                if (!point.Properties.IsRightButtonPressed)
                {
                    _tracking = false;
                    return;
                }

                double dx = point.Position.X - _origin.X;
                double dy = point.Position.Y - _origin.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
                    return;
            }
            catch
            {
                _tracking = false;
                return;
            }

            _tracking = false;
            _dragging = true;
            try
            {
                DebugLogger.Log($"[RightDrag] Starting drag for {_pressedItem?.Name}");
                var result = await _listView.StartDragAsync(point);
                DebugLogger.Log($"[RightDrag] Drag finished: {result}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RightDrag] StartDragAsync failed: {ex.Message}");
            }
            finally
            {
                _dragging = false;
                _pressedItem = null;
                try { _onDragCompleted(); }
                catch (Exception ex) { DebugLogger.Log($"[RightDrag] completion callback failed: {ex.Message}"); }
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerId == _pointerId)
                _tracking = false;
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (e.Pointer.PointerId == _pointerId)
                _tracking = false;
        }

        private void OnDragStarting(UIElement sender, DragStartingEventArgs args)
        {
            // Only our own StartDragAsync raises DragStarting on the ListView itself;
            // the built-in item drag raises it on the item containers.
            if (_pressedItem == null)
            {
                args.Cancel = true;
                return;
            }

            try
            {
                var items = ViewDragDropHelper.ResolveDragItems(_listView, _pressedItem, _selectDraggedItem);
                if (items.Count == 0 || !_onDragStarting(items, args.Data))
                {
                    args.Cancel = true;
                    return;
                }

                args.Data.Properties[RightButtonDragProperty] = true;
                args.AllowedOperations = DataPackageOperation.Copy | DataPackageOperation.Move | DataPackageOperation.Link;

                // Explorer-like drag image built from the StorageItems in the package.
                try { args.DragUI.SetContentFromDataPackage(); }
                catch (Exception ex) { DebugLogger.Log($"[RightDrag] SetContentFromDataPackage failed: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[RightDrag] DragStarting failed: {ex.Message}");
                args.Cancel = true;
            }
        }

        private SelectorItem? FindContainer(DependencyObject? source)
        {
            while (source != null)
            {
                if (source is SelectorItem item) return item;
                if (source == _listView) return null;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
        }
    }
}
