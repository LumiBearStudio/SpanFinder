using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using Windows.Foundation;

namespace Span.Helpers
{
    /// <summary>
    /// Rubber-band (marquee) selection helper for ListView/GridView.
    /// Attaches to the content Grid that wraps the ListViewBase, adding an overlay Canvas
    /// with a selection rectangle. Mouse-only (v1).
    /// </summary>
    internal sealed class RubberBandSelectionHelper
    {
        private enum State { Inactive, Starting, Active }

        public bool IsActive => _state != State.Inactive;

        private const double DragThreshold = 5.0;
        private const double AutoScrollEdge = 30.0;
        private const double AutoScrollSpeed = 6.0;

        private readonly Grid _contentGrid;
        private readonly ListViewBase _listView;
        private readonly Canvas _overlayCanvas;
        private readonly Rectangle _selectionRect;
        private readonly Func<bool> _getIsSyncing;
        private readonly Action<bool> _setIsSyncing;
        private readonly Action<IList<object>>? _syncCallback;

        private bool _detached;
        private State _state = State.Inactive;
        private Point _origin;
        private bool _isCtrlHeld;
        private List<(FileSystemViewModel vm, Rect bounds)> _itemBoundsCache = new();
        private HashSet<FileSystemViewModel> _preSelectionSnapshot = new();
        private bool _isCapturing; // guard: ignore PointerCaptureLost from our own CapturePointer call
        private bool _savedCanDragItems = true; // saved CanDragItems value to restore after rubber band

        // Auto-scroll
        private DispatcherTimer? _autoScrollTimer;
        private double _autoScrollDelta;
        private ScrollViewer? _scrollViewer;

        public RubberBandSelectionHelper(
            Grid contentGrid,
            ListViewBase listView,
            Func<bool> getIsSyncing,
            Action<bool> setIsSyncing,
            Action<IList<object>>? syncCallback = null)
        {
            _contentGrid = contentGrid;
            _listView = listView;
            _getIsSyncing = getIsSyncing;
            _setIsSyncing = setIsSyncing;
            _syncCallback = syncCallback;

            // Create overlay canvas (transparent, not hit-testable when inactive)
            _overlayCanvas = new Canvas
            {
                IsHitTestVisible = false,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                Opacity = 1.0
            };
            Canvas.SetZIndex(_overlayCanvas, 100);

            // Create selection rectangle (hidden initially)
            _selectionRect = new Rectangle
            {
                Fill = (Brush)Application.Current.Resources["SpanSelectionRectFillBrush"],
                Stroke = (Brush)Application.Current.Resources["SpanSelectionRectStrokeBrush"],
                StrokeThickness = 1,
                RadiusX = 2,
                RadiusY = 2,
                Visibility = Visibility.Collapsed
            };
            _overlayCanvas.Children.Add(_selectionRect);

            // Add overlay to the content grid (same cell as ListView)
            _contentGrid.Children.Add(_overlayCanvas);

            // Register pointer events with handledEventsToo so we get them even if ListView marks handled
            _contentGrid.AddHandler(UIElement.PointerPressedEvent,
                new PointerEventHandler(OnPointerPressed), true);
            _contentGrid.AddHandler(UIElement.PointerMovedEvent,
                new PointerEventHandler(OnPointerMoved), true);
            _contentGrid.AddHandler(UIElement.PointerReleasedEvent,
                new PointerEventHandler(OnPointerReleased), true);
            _contentGrid.AddHandler(UIElement.PointerCaptureLostEvent,
                new PointerEventHandler(OnPointerCaptureLost), true);
        }

        public void Detach()
        {
            _detached = true;
            Cleanup();

            try
            {
                _contentGrid.RemoveHandler(UIElement.PointerPressedEvent,
                    (PointerEventHandler)OnPointerPressed);
                _contentGrid.RemoveHandler(UIElement.PointerMovedEvent,
                    (PointerEventHandler)OnPointerMoved);
                _contentGrid.RemoveHandler(UIElement.PointerReleasedEvent,
                    (PointerEventHandler)OnPointerReleased);
                _contentGrid.RemoveHandler(UIElement.PointerCaptureLostEvent,
                    (PointerEventHandler)OnPointerCaptureLost);
                _contentGrid.Children.Remove(_overlayCanvas);
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.Detach", ex);
            }
        }

        private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (_detached) return;
            try
            {
                // Mouse only (v1)
                if (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse)
                    return;

                // Only left button
                var props = e.GetCurrentPoint(_contentGrid).Properties;
                if (!props.IsLeftButtonPressed)
                    return;

                // Clean up any stuck state from previous operation
                if (_state != State.Inactive) Cleanup();

                // Hit-test: if pointer is on actual item content (text/icon), let ListView handle it
                if (IsPointerOnItemContent(e))
                    return;

                // Multi-selection + dead zone of a SELECTED item → let ListView handle for drag (move/copy)
                // Only start rubber band from: empty space, unselected item dead zone, or single/no selection
                if (_listView.SelectedItems.Count >= 2 && IsOnSelectedItem(e))
                    return;

                // Empty space or item padding click — start rubber-band
                var point = e.GetCurrentPoint(_contentGrid).Position;
                _origin = point;
                _isCtrlHeld = InputKeyboardSource.GetKeyStateForCurrentThread(
                    Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

                // Snapshot current selection for Ctrl+drag
                _preSelectionSnapshot.Clear();
                if (_isCtrlHeld)
                {
                    foreach (var item in _listView.SelectedItems)
                    {
                        if (item is FileSystemViewModel fsvm)
                            _preSelectionSnapshot.Add(fsvm);
                    }
                }

                _state = State.Starting;

                // Disable ListView's built-in drag to prevent DragItemsStarting from firing.
                // This must happen BEFORE the next PointerMoved triggers the drag threshold check.
                _savedCanDragItems = _listView.CanDragItems;
                _listView.CanDragItems = false;

                // DON'T capture pointer here — the ListViewItem's internal handlers have already
                // processed this press and started drag tracking. Capturing now causes conflicts
                // (PointerCaptureLost bubbling, internal drag state machine interference).
                // Pointer capture will happen in OnPointerMoved when transitioning to Active.

                e.Handled = true;
                DebugLogger.Log($"[RubberBand] PointerPressed: Starting at ({point.X:F0},{point.Y:F0}), CanDragItems saved={_savedCanDragItems}");
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.PointerPressed", ex);
                Cleanup();
            }
        }

        private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_detached || _state == State.Inactive) return;
            try
            {
                var current = e.GetCurrentPoint(_contentGrid).Position;

                if (_state == State.Starting)
                {
                    // Check threshold
                    double dx = current.X - _origin.X;
                    double dy = current.Y - _origin.Y;
                    if (Math.Sqrt(dx * dx + dy * dy) < DragThreshold)
                        return;

                    // Transition to Active
                    _state = State.Active;
                    _selectionRect.Visibility = Visibility.Visible;

                    // Make overlay hit-test visible so it blocks events from reaching ListView
                    // and capture pointer on overlay canvas (isolated from ListView internals)
                    _overlayCanvas.IsHitTestVisible = true;
                    _isCapturing = true;
                    try { _overlayCanvas.CapturePointer(e.Pointer); } catch { }
                    _isCapturing = false;

                    // Cache item bounds
                    CacheItemBounds();

                    // Clear selection if not Ctrl
                    if (!_isCtrlHeld)
                    {
                        _setIsSyncing(true);
                        try { _listView.SelectedItems.Clear(); }
                        finally { _setIsSyncing(false); }
                    }

                    // Start auto-scroll timer
                    StartAutoScroll();

                    DebugLogger.Log($"[RubberBand] Transitioned to Active, captured on overlay");
                }

                if (_state == State.Active)
                {
                    DrawRect(current);
                    UpdateSelection(current);
                    UpdateAutoScrollDirection(current);
                    e.Handled = true;
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.PointerMoved", ex);
                Cleanup();
            }
        }

        private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (_detached || _state == State.Inactive) return;
            try
            {
                var wasActive = _state == State.Active;
                DebugLogger.Log($"[RubberBand] PointerReleased: state={_state}, wasActive={wasActive}");

                if (_state == State.Starting)
                {
                    // Click on empty/dead-zone without drag — clear selection
                    if (!_isCtrlHeld)
                    {
                        _setIsSyncing(true);
                        try { _listView.SelectedItems.Clear(); }
                        finally { _setIsSyncing(false); }

                        SyncToViewModel();
                    }
                }

                if (wasActive)
                {
                    SyncToViewModel();
                }

                Cleanup();
                try { _overlayCanvas.ReleasePointerCapture(e.Pointer); } catch { }
                e.Handled = true;
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.PointerReleased", ex);
                Cleanup();
            }
        }

        private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
        {
            if (_detached || _state == State.Inactive) return;

            // Guard: ignore PointerCaptureLost fired by our own CapturePointer call
            if (_isCapturing) return;

            // During Starting state, we haven't captured anything ourselves.
            // PointerCaptureLost is from ListView/ListViewItem internals (e.g. drag cancel) → ignore
            if (_state == State.Starting)
            {
                DebugLogger.Log("[RubberBand] PointerCaptureLost: in Starting state, ignoring (internal ListView event)");
                return;
            }

            // During Active state, only react if OUR capture target (overlay canvas) lost the pointer.
            // Ignore PointerCaptureLost from other elements (ListViewItem drag cancellation, etc.)
            if (_state == State.Active)
            {
                if (e.OriginalSource != _overlayCanvas)
                {
                    DebugLogger.Log($"[RubberBand] PointerCaptureLost: source is not overlay canvas, ignoring");
                    return;
                }
            }

            DebugLogger.Log("[RubberBand] PointerCaptureLost: genuine loss of overlay capture, cleaning up");
            try
            {
                SyncToViewModel();
                Cleanup();
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.PointerCaptureLost", ex);
                Cleanup();
            }
        }

        private void SyncToViewModel()
        {
            if (_syncCallback != null)
            {
                _syncCallback(_listView.SelectedItems);
            }
            else if (_listView.DataContext is FolderViewModel folderVm)
            {
                folderVm.SyncSelectedItems(_listView.SelectedItems);
            }
        }

        private void Cleanup()
        {
            _state = State.Inactive;
            _itemBoundsCache.Clear();
            _preSelectionSnapshot.Clear();
            StopAutoScroll();

            // Restore ListView's CanDragItems (disabled during rubber band to prevent built-in drag)
            try { _listView.CanDragItems = _savedCanDragItems; } catch { }

            try
            {
                _selectionRect.Visibility = Visibility.Collapsed;
                _overlayCanvas.IsHitTestVisible = false;
            }
            catch { /* UI element may already be disposed */ }
        }

        /// <summary>
        /// Walk the visual tree from OriginalSource upward to check if pointer hit
        /// actual item CONTENT (text, icon, image).
        /// Special handling for TextBlock: In WinUI 3, a TextBlock with HorizontalAlignment="Stretch"
        /// is hit-testable across its ENTIRE layout bounds, including the empty area to the right of
        /// the text. We compare the pointer position against DesiredSize (the text's natural width)
        /// to distinguish actual text hits from dead-zone hits.
        /// </summary>
        private bool IsPointerOnItemContent(PointerRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                // FontIcon, Image, TextBox: always content
                if (source is FontIcon || source is Image || source is TextBox)
                    return true;

                // TextBlock: check if pointer is on the actual text area, not the stretched dead zone
                if (source is TextBlock tb)
                {
                    try
                    {
                        var pos = e.GetCurrentPoint(tb).Position;
                        double textW = tb.DesiredSize.Width;
                        double textH = tb.DesiredSize.Height;

                        // If pointer is within the text's natural bounds, it's on content
                        if (pos.X >= 0 && pos.X <= textW && pos.Y >= 0 && pos.Y <= textH)
                            return true;

                        // Dead zone: past the text's natural bounds but within the stretched TextBlock
                        // Continue walking up to reach SelectorItem → return false
                    }
                    catch
                    {
                        return true; // Fallback to treating as content on error
                    }
                }

                // Reached SelectorItem (ListViewItem/GridViewItem) without hitting content → on padding/dead zone
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem)
                    return false;

                if (source == _contentGrid)
                    break;

                source = VisualTreeHelper.GetParent(source);
            }
            return false; // Empty space below items
        }

        /// <summary>
        /// Check if pointer is on a SelectorItem that is currently selected.
        /// Used to allow drag (move/copy) from dead zone when multi-selection is active.
        /// </summary>
        private bool IsOnSelectedItem(PointerRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem selectorItem)
                {
                    var itemData = _listView.ItemFromContainer(selectorItem);
                    return itemData != null && _listView.SelectedItems.Contains(itemData);
                }
                if (source == _contentGrid)
                    break;
                source = VisualTreeHelper.GetParent(source);
            }
            return false; // Empty space — not on any item
        }

        /// <summary>
        /// Cache the bounds of all realized items relative to _contentGrid.
        /// </summary>
        private void CacheItemBounds()
        {
            _itemBoundsCache.Clear();

            for (int i = 0; i < _listView.Items.Count; i++)
            {
                var container = _listView.ContainerFromIndex(i) as Microsoft.UI.Xaml.Controls.Primitives.SelectorItem;
                if (container == null)
                    continue; // virtualized out — skip

                var vm = _listView.Items[i] as FileSystemViewModel;
                if (vm == null)
                    continue;

                try
                {
                    var transform = container.TransformToVisual(_contentGrid);
                    var topLeft = transform.TransformPoint(new Point(0, 0));
                    var bounds = new Rect(topLeft.X, topLeft.Y,
                        container.ActualWidth, container.ActualHeight);
                    _itemBoundsCache.Add((vm, bounds));
                }
                catch
                {
                    // TransformToVisual can throw if element not in tree
                }
            }
        }

        /// <summary>
        /// Draw the selection rectangle from _origin to current point.
        /// </summary>
        private void DrawRect(Point current)
        {
            double x = Math.Min(_origin.X, current.X);
            double y = Math.Min(_origin.Y, current.Y);
            double w = Math.Abs(current.X - _origin.X);
            double h = Math.Abs(current.Y - _origin.Y);

            // Clamp to grid bounds
            double gridW = _contentGrid.ActualWidth;
            double gridH = _contentGrid.ActualHeight;
            if (x < 0) { w += x; x = 0; }
            if (y < 0) { h += y; y = 0; }
            if (x + w > gridW) w = gridW - x;
            if (y + h > gridH) h = gridH - y;

            Canvas.SetLeft(_selectionRect, x);
            Canvas.SetTop(_selectionRect, y);
            _selectionRect.Width = Math.Max(0, w);
            _selectionRect.Height = Math.Max(0, h);
        }

        /// <summary>
        /// Update ListView.SelectedItems based on intersection with selection rectangle.
        /// Uses diff-based updates to avoid unnecessary add/remove.
        /// </summary>
        private void UpdateSelection(Point current)
        {
            double rx = Math.Min(_origin.X, current.X);
            double ry = Math.Min(_origin.Y, current.Y);
            double rw = Math.Abs(current.X - _origin.X);
            double rh = Math.Abs(current.Y - _origin.Y);
            var selRect = new Rect(rx, ry, rw, rh);

            // Determine which items intersect
            var intersecting = new HashSet<FileSystemViewModel>();
            foreach (var (vm, bounds) in _itemBoundsCache)
            {
                if (RectsIntersect(selRect, bounds))
                    intersecting.Add(vm);
            }

            // Build target selection set
            HashSet<FileSystemViewModel> target;
            if (_isCtrlHeld)
            {
                // XOR: items in snapshot that are NOT in rect, plus items in rect that were NOT in snapshot
                target = new HashSet<FileSystemViewModel>(_preSelectionSnapshot);
                foreach (var vm in intersecting)
                {
                    if (!target.Remove(vm))
                        target.Add(vm);
                }
            }
            else
            {
                target = intersecting;
            }

            // Diff-update ListView.SelectedItems
            _setIsSyncing(true);
            try
            {
                // Remove items no longer in target
                for (int i = _listView.SelectedItems.Count - 1; i >= 0; i--)
                {
                    if (_listView.SelectedItems[i] is FileSystemViewModel fsvm && !target.Contains(fsvm))
                        _listView.SelectedItems.RemoveAt(i);
                }

                // Add items newly in target
                var currentlySelected = new HashSet<FileSystemViewModel>();
                foreach (var item in _listView.SelectedItems)
                {
                    if (item is FileSystemViewModel fsvm)
                        currentlySelected.Add(fsvm);
                }
                foreach (var vm in target)
                {
                    if (!currentlySelected.Contains(vm))
                        _listView.SelectedItems.Add(vm);
                }
            }
            finally
            {
                _setIsSyncing(false);
            }
        }

        private static bool RectsIntersect(Rect a, Rect b)
        {
            return a.X < b.X + b.Width &&
                   a.X + a.Width > b.X &&
                   a.Y < b.Y + b.Height &&
                   a.Y + a.Height > b.Y;
        }

        // ── Auto-scroll ──

        private ScrollViewer? FindScrollViewer()
        {
            if (_scrollViewer != null)
                return _scrollViewer;

            // ScrollViewer is inside ListView's template
            _scrollViewer = FindChildOfType<ScrollViewer>(_listView);
            return _scrollViewer;
        }

        private static T? FindChildOfType<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T found) return found;
                var result = FindChildOfType<T>(child);
                if (result != null) return result;
            }
            return null;
        }

        private void StartAutoScroll()
        {
            if (_autoScrollTimer != null) return;
            _autoScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScrollTimer.Tick += OnAutoScrollTick;
            _autoScrollDelta = 0;
        }

        private void StopAutoScroll()
        {
            if (_autoScrollTimer != null)
            {
                _autoScrollTimer.Stop();
                _autoScrollTimer.Tick -= OnAutoScrollTick;
                _autoScrollTimer = null;
            }
            _autoScrollDelta = 0;
            _scrollViewer = null;
        }

        private void UpdateAutoScrollDirection(Point current)
        {
            double gridH = _contentGrid.ActualHeight;
            if (current.Y < AutoScrollEdge)
            {
                _autoScrollDelta = -AutoScrollSpeed * (1.0 - current.Y / AutoScrollEdge);
                _autoScrollTimer?.Start();
            }
            else if (current.Y > gridH - AutoScrollEdge)
            {
                _autoScrollDelta = AutoScrollSpeed * (1.0 - (gridH - current.Y) / AutoScrollEdge);
                _autoScrollTimer?.Start();
            }
            else
            {
                _autoScrollDelta = 0;
                _autoScrollTimer?.Stop();
            }
        }

        private void OnAutoScrollTick(object? sender, object e)
        {
            if (_detached || _state != State.Active || Math.Abs(_autoScrollDelta) < 0.1)
                return;

            try
            {
                var sv = FindScrollViewer();
                if (sv == null) return;

                double newOffset = sv.VerticalOffset + _autoScrollDelta;
                newOffset = Math.Max(0, Math.Min(newOffset, sv.ScrollableHeight));
                sv.ChangeView(null, newOffset, null, true);

                // Re-cache bounds after scroll (positions changed)
                CacheItemBounds();
            }
            catch (Exception ex)
            {
                DebugLogger.LogCrash("RubberBand.AutoScrollTick", ex);
                StopAutoScroll();
            }
        }
    }
}
