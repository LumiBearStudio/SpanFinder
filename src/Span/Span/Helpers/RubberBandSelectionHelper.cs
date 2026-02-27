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
    /// 러버밴드(마키) 선택 헬퍼: ListView/GridView 위에 오버레이 Canvas를 배치하여
    /// 마우스 드래그로 사각형 범위 선택을 구현한다 (마우스 전용, v1).
    /// 
    /// <para><b>상태 머신 (3단계):</b></para>
    /// <list type="number">
    ///   <item><description>Inactive → 대기 상태. PointerPressed가 발생하면 Starting으로 전환.</description></item>
    ///   <item><description>Starting → 클릭 시작됨, 아직 드래그 임계값(5px) 미달. 임계값 초과 시 Active로 전환.
    ///     임계값 미달 상태에서 PointerReleased 시 단일 클릭으로 처리 (데드존 클릭 or 빈 공간 클릭).</description></item>
    ///   <item><description>Active → 러버밴드 사각형 표시 중. 마우스 이동에 따라 선택 영역 갱신.
    ///     PointerReleased 시 최종 선택을 ViewModel에 동기화하고 정리.</description></item>
    /// </list>
    /// 
    /// <para><b>데드존 처리:</b></para>
    /// <para>ListViewItem의 텍스트/아이콘이 아닌 패딩 영역(데드존)을 클릭하면:  
    /// - 선택된 항목의 데드존 → ListView에 위임 (드래그 시작 가능)  
    /// - 미선택 항목의 데드존 → Starting 상태 진입 → 클릭 시 해당 항목 단일 선택  
    /// - 빈 공간 → Starting 상태 진입 → 클릭 시 선택 해제</para>
    /// 
    /// <para><b>Ctrl 키:</b> Ctrl+드래그 시 기존 선택을 스냅샷으로 보존하고 XOR 방식으로 토글.</para>
    /// <para><b>자동 스크롤:</b> 드래그가 뷰포트 가장자리(30px)에 도달하면 자동 스크롤 시작.</para>
    /// </summary>
    internal sealed class RubberBandSelectionHelper
    {
        /// <summary>러버밴드 상태 머신. Inactive → Starting → Active 순으로 전환.</summary>
        private enum State { Inactive, Starting, Active }

        /// <summary>러버밴드가 활성 상태(Starting 또는 Active)인지 여부.</summary>
        public bool IsActive => _state != State.Inactive;

        /// <summary>Starting → Active 전환에 필요한 최소 이동 거리 (px).</summary>
        private const double DragThreshold = 5.0;
        /// <summary>자동 스크롤이 시작되는 뷰포트 가장자리 영역 크기 (px).</summary>
        private const double AutoScrollEdge = 30.0;
        /// <summary>자동 스크롤 최대 속도 (px/tick, ~60fps).</summary>
        private const double AutoScrollSpeed = 6.0;

        /// <summary>오버레이 Canvas를 포함하는 부모 Grid (ListView와 같은 Grid 셀에 위치).</summary>
        private readonly Grid _contentGrid;
        /// <summary>대상 ListView/GridView.</summary>
        private readonly ListViewBase _listView;
        /// <summary>선택 사각형을 그리는 투명 오버레이 Canvas. ZIndex=100으로 ListView 위에 배치.</summary>
        private readonly Canvas _overlayCanvas;
        /// <summary>시각적 선택 사각형 (Rectangle). Active 상태에서만 Visible.</summary>
        private readonly Rectangle _selectionRect;
        /// <summary>선택 동기화 중 여부를 조회하는 콜백. SelectionChanged 재진입 방지용.</summary>
        private readonly Func<bool> _getIsSyncing;
        /// <summary>선택 동기화 플래그를 설정하는 콜백.</summary>
        private readonly Action<bool> _setIsSyncing;
        /// <summary>선택 완료 시 호출될 동기화 콜백 (null이면 FolderViewModel.SyncSelectedItems 사용).</summary>
        private readonly Action<IList<object>>? _syncCallback;

        /// <summary>Detach() 호출됨 — 이후 이벤트 무시.</summary>
        private bool _detached;
        /// <summary>현재 상태 머신 상태.</summary>
        private State _state = State.Inactive;
        /// <summary>드래그 시작점 (_contentGrid 기준 좌표).</summary>
        private Point _origin;
        /// <summary>PointerPressed 시 Ctrl 키 상태 스냅샷.</summary>
        private bool _isCtrlHeld;
        /// <summary>Active 전환 시 캐시된 아이템 바운드 (가상화된 항목은 제외).</summary>
        private List<(FileSystemViewModel vm, Rect bounds)> _itemBoundsCache = new();
        /// <summary>Ctrl+드래그 시 기존 선택 상태 스냅샷 (XOR 토글용).</summary>
        private HashSet<FileSystemViewModel> _preSelectionSnapshot = new();
        /// <summary>CapturePointer 호출 중 PointerCaptureLost 재진입 방지 가드.</summary>
        private bool _isCapturing;
        /// <summary>러버밴드 시작 전 ListView.CanDragItems 원본값 (복원용).</summary>
        private bool _savedCanDragItems = true;
        /// <summary>Starting 상태에서 데드존 클릭된 SelectorItem (null이면 빈 공간 클릭).</summary>
        private Microsoft.UI.Xaml.Controls.Primitives.SelectorItem? _deadZoneSelectorItem;

        // ── 자동 스크롤 ──
        /// <summary>자동 스크롤 16ms 간격 타이머 (~60fps).</summary>
        private DispatcherTimer? _autoScrollTimer;
        /// <summary>자동 스크롤 방향 및 속도 (양수=아래, 음수=위).</summary>
        private double _autoScrollDelta;
        /// <summary>캐시된 ListView 내부 ScrollViewer 참조.</summary>
        private ScrollViewer? _scrollViewer;

        /// <summary>
        /// 러버밴드 선택 헬퍼를 초기화하고 contentGrid에 이벤트 핸들러를 등록한다.
        /// </summary>
        /// <param name="contentGrid">ListView를 감싸는 Grid. 오버레이 Canvas가 여기에 추가된다.</param>
        /// <param name="listView">대상 ListViewBase (ListView 또는 GridView).</param>
        /// <param name="getIsSyncing">선택 동기화 중 여부 조회 콜백.</param>
        /// <param name="setIsSyncing">선택 동기화 플래그 설정 콜백.</param>
        /// <param name="syncCallback">선택 완료 시 ViewModel 동기화 콜백 (null이면 FolderViewModel.SyncSelectedItems).</param>
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

        /// <summary>
        /// 이벤트 핸들러를 해제하고 오버레이 Canvas를 제거한다.
        /// 뷰 전환 또는 컬럼 제거 시 호출하여 메모리 누수를 방지한다.
        /// </summary>
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

        /// <summary>
        /// ListView.SelectedItems를 ViewModel에 동기화한다.
        /// syncCallback이 제공되면 사용하고, 없으면 FolderViewModel.SyncSelectedItems를 직접 호출한다.
        /// </summary>
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

        /// <summary>
        /// 러버밴드 상태를 초기화한다: 상태를 Inactive로, 캐시 및 스냅샷 클리어,
        /// CanDragItems 복원, 선택 사각형 숨김, 자동 스크롤 중지.
        /// </summary>
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

        /// <summary>두 Rect가 겹치는지 확인한다 (AABB 충돌 검사).</summary>
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
