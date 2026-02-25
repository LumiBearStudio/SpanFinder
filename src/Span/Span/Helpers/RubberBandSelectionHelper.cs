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
        private Microsoft.UI.Xaml.Controls.Primitives.SelectorItem? _deadZoneSelectorItem; // item under dead-zone click (null = empty space)

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

                // Scrollbar: let ScrollViewer handle scrollbar interaction
                if (IsPointerOnScrollbar(e))
                    return;

                // Hit-test: if pointer is on actual item content (text/icon), let ListView handle it
                if (IsPointerOnItemContent(e))
                    return;

                // Determine if pointer is on an item's dead zone or truly empty space
                var selectorItem = FindSelectorItemAtPointer(e);

                // Dead zone of a SELECTED item → let ListView handle (file drag / keep selection)
                if (selectorItem != null)
                {
                    var itemData = _listView.ItemFromContainer(selectorItem);
                    if (itemData != null && _listView.SelectedItems.Contains(itemData))
                        return;
                }

                // Dead zone of UNSELECTED item or empty space → rubber band Starting
                // Click (no drag) → select item or clear selection
                // Drag (threshold exceeded) → rubber band multi-select
                var point = e.GetCurrentPoint(_contentGrid).Position;
                _origin = point;
                _isCtrlHeld = InputKeyboardSource.GetKeyStateForCurrentThread(
                    Windows.System.VirtualKey.Control).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                _deadZoneSelectorItem = selectorItem; // null = empty space

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

                // Disable ListView's built-in drag to prevent DragItemsStarting from firing
                _savedCanDragItems = _listView.CanDragItems;
                _listView.CanDragItems = false;

                e.Handled = true;
                DebugLogger.Log($"[RubberBand] PointerPressed: Starting at ({point.X:F0},{point.Y:F0}), onItem={selectorItem != null}");
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
                    // Click without drag — distinguish dead zone vs empty space
                    if (_deadZoneSelectorItem != null)
                    {
                        // Dead zone of UNSELECTED item → select it (like Windows Explorer)
                        var itemData = _listView.ItemFromContainer(_deadZoneSelectorItem);
                        if (itemData != null)
                        {
                            _setIsSyncing(true);
                            try
                            {
                                if (_isCtrlHeld)
                                {
                                    // Ctrl+click: toggle selection
                                    if (_listView.SelectedItems.Contains(itemData))
                                        _listView.SelectedItems.Remove(itemData);
                                    else
                                        _listView.SelectedItems.Add(itemData);
                                }
                                else
                                {
                                    // Normal click: single-select
                                    _listView.SelectedItems.Clear();
                                    _listView.SelectedItems.Add(itemData);
                                }
                            }
                            finally { _setIsSyncing(false); }
                        }
                        SyncToViewModel();
                    }
                    else
                    {
                        // Empty space click → clear selection
                        if (!_isCtrlHeld)
                        {
                            _setIsSyncing(true);
                            try { _listView.SelectedItems.Clear(); }
                            finally { _setIsSyncing(false); }
                            SyncToViewModel();
                        }
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
            _deadZoneSelectorItem = null;
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
        /// Check if the pointer is on a ScrollBar element (thumb, track, arrows).
        /// Prevents rubber-band selection from starting when the user grabs the scrollbar.
        /// </summary>
        private bool IsPointerOnScrollbar(PointerRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)
                    return true;
                if (source == _contentGrid)
                    break;
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }

        /// <summary>
        /// Walk the visual tree from OriginalSource upward to check if pointer hit
        /// actual item CONTENT (text, icon, image) — not dead-zone padding.
        /// Dead-zone hits on SelectorItem return false so rubber band can handle click-vs-drag.
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

                        if (pos.X >= 0 && pos.X <= textW && pos.Y >= 0 && pos.Y <= textH)
                            return true;
                        // Dead zone of TextBlock → continue walking up
                    }
                    catch
                    {
                        return true;
                    }
                }

                // SelectorItem or contentGrid → stop walking
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem || source == _contentGrid)
                    break;

                source = VisualTreeHelper.GetParent(source);
            }
            return false; // Dead zone of item row OR empty space below items
        }

        /// <summary>
        /// Find the SelectorItem (ListViewItem) under the pointer, or null if on empty space.
        /// </summary>
        private Microsoft.UI.Xaml.Controls.Primitives.SelectorItem? FindSelectorItemAtPointer(PointerRoutedEventArgs e)
        {
            var source = e.OriginalSource as DependencyObject;
            while (source != null)
            {
                if (source is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem si)
                    return si;
                if (source == _contentGrid)
                    break;
                source = VisualTreeHelper.GetParent(source);
            }
            return null;
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
