using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Span.Models;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Span
{
    /// <summary>
    /// MainWindow의 탭 관리 부분 클래스.
    /// 탭별 독립 뷰 패널(Miller, Details, List, Icon)의 Show/Hide 패턴 관리,
    /// 탭 생성·전환·닫기·복제·재정렬, 탭 떼어내기(tear-off) 드래그,
    /// 탭 표시명 업데이트, 세션 복원 시 탭 패널 초기화 등을 담당한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        // =================================================================
        //  Tab Display Name
        // =================================================================

        /// <summary>
        /// 탭에 표시할 이름을 반환한다. Home 모드이면 "Home", 아니면 폴더명.
        /// </summary>
        public string GetTabDisplayName(Models.ViewMode mode, string folderName)
            => mode == Models.ViewMode.Home ? _loc.Get("Home") : folderName;

        // =================================================================
        //  Dynamic Tab Width (Chrome-style)
        // =================================================================

        /// <summary>
        /// 탭 바 너비 변경 시 각 탭의 Width를 동적 계산하여 적용한다.
        /// Chrome처럼 탭이 많아지면 줄어들고, 적으면 최대 너비까지 늘어난다.
        /// </summary>
        private void RecalculateTabWidths()
        {
            try
            {
                if (_isClosed || TabScrollViewer == null || TabRepeater == null) return;
                int tabCount = ViewModel?.Tabs?.Count ?? 0;
                if (tabCount == 0) return;

                double availableWidth = TabScrollViewer.ActualWidth;
                if (availableWidth <= 0) return;

                // + 버튼(32px) + 여백
                double newTabBtnWidth = 38;
                double available = availableWidth - newTabBtnWidth;

                double tabWidth = Math.Max(MIN_TAB_WIDTH, Math.Min(MAX_TAB_WIDTH, available / tabCount));
                _calculatedTabWidth = tabWidth;

                // 각 탭 아이템에 너비 적용
                for (int i = 0; i < tabCount; i++)
                {
                    if (TabRepeater.TryGetElement(i) is FrameworkElement elem)
                    {
                        // ItemsRepeater의 루트 요소는 DataTemplate의 Grid
                        if (elem is Grid grid)
                            grid.Width = tabWidth;
                        else
                            elem.Width = tabWidth;
                    }
                }
            }
            catch { /* layout timing — safe to ignore */ }
        }

        // =================================================================
        //  Per-Tab Miller Panel Management (Show/Hide pattern)
        // =================================================================

        #region Miller Panel Management

        /// <summary>
        /// LoadTabsFromSettings 후 모든 탭에 대한 Miller 패널 초기화.
        /// 기존 패널을 정리하고, 각 탭에 대해 패널을 (재)생성한다.
        /// 활성 탭 패널만 Visible, 나머지는 Collapsed.
        /// </summary>
        private void InitializeTabMillerPanels()
        {
            // 기존 동적 패널 정리 (XAML 정의 MillerScrollViewer 제외)
            foreach (var kvp in _tabMillerPanels)
            {
                if (kvp.Value.scroller != MillerScrollViewer)
                {
                    kvp.Value.items.ItemsSource = null;
                    MillerTabsHost.Children.Remove(kvp.Value.scroller);
                }
            }
            _tabMillerPanels.Clear();

            // M4: 활성 탭만 즉시 패널 할당 — 비활성 탭은 SwitchMillerPanel에서 Lazy 생성
            for (int i = 0; i < ViewModel.Tabs.Count; i++)
            {
                var tab = ViewModel.Tabs[i];
                if (i == ViewModel.ActiveTabIndex)
                {
                    // 활성 탭은 XAML 정의 패널 재사용
                    MillerColumnsControl.ItemsSource = tab.Explorer?.Columns;
                    MillerScrollViewer.Visibility = Visibility.Visible;
                    _tabMillerPanels[tab.Id] = (MillerScrollViewer, MillerColumnsControl);
                    _activeMillerTabId = tab.Id;
                }
                // 비활성 탭은 SwitchMillerPanel 호출 시 Lazy 생성
            }

            // ── Per-Tab Details/List/Icon Panels 초기화 ──
            // 기존 동적 패널 정리 (XAML 정의 인스턴스 제외)
            foreach (var kvp in _tabDetailsPanels)
            {
                if (kvp.Value != DetailsView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    DetailsTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabDetailsPanels.Clear();

            foreach (var kvp in _tabListPanels)
            {
                if (kvp.Value != ListView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    ListTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabListPanels.Clear();

            foreach (var kvp in _tabIconPanels)
            {
                if (kvp.Value != IconView)
                {
                    try { kvp.Value?.Cleanup(); } catch { }
                    IconTabsHost.Children.Remove(kvp.Value);
                }
            }
            _tabIconPanels.Clear();

            // 활성 탭에 XAML 정의 인스턴스 할당
            var activeTab = ViewModel.Tabs.Count > 0 ? ViewModel.Tabs[ViewModel.ActiveTabIndex] : null;
            if (activeTab != null)
            {
                _tabDetailsPanels[activeTab.Id] = DetailsView;
                _tabListPanels[activeTab.Id] = ListView;
                _tabIconPanels[activeTab.Id] = IconView;
                _activeDetailsTabId = activeTab.Id;
                _activeListTabId = activeTab.Id;
                _activeIconTabId = activeTab.Id;
            }

            Helpers.DebugLogger.Log($"[MillerPanel] Initialized {_tabMillerPanels.Count} panels (active: {_activeMillerTabId})");
        }

        /// <summary>
        /// 새 탭에 대한 Miller Columns 패널(ScrollViewer + ItemsControl) 생성.
        /// XAML 정의 MillerColumnsControl의 Template을 재사용하여 이벤트 핸들러 호환성 보장.
        /// </summary>
        private (ScrollViewer scroller, ItemsControl items) CreateMillerPanelForTab(Models.TabItem tab)
        {
            var itemsControl = new ItemsControl
            {
                ItemTemplate = MillerColumnsControl.ItemTemplate,
                ItemsPanel = MillerColumnsControl.ItemsPanel,
                ItemsSource = tab.Explorer?.Columns
            };

            // 키보드 이벤트 핸들러 등록 (XAML 정의 컨트롤과 동일)
            itemsControl.AddHandler(
                UIElement.KeyDownEvent,
                new KeyEventHandler(OnMillerKeyDown),
                true
            );
            // CharacterReceived: 비라틴 문자 타입 어헤드 지원
            itemsControl.AddHandler(
                UIElement.CharacterReceivedEvent,
                new Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>(OnMillerCharacterReceived),
                true
            );

            var scrollViewer = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
                Content = itemsControl,
                Visibility = Visibility.Collapsed // 생성 시 숨김, 전환 시 표시
            };

            // 뷰포트 리사이즈 시 마지막 컬럼으로 자동 스크롤
            scrollViewer.SizeChanged += OnMillerScrollViewerSizeChanged;

            // MillerTabsHost Grid에 추가
            MillerTabsHost.Children.Add(scrollViewer);
            _tabMillerPanels[tab.Id] = (scrollViewer, itemsControl);

            Helpers.DebugLogger.Log($"[MillerPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return (scrollViewer, itemsControl);
        }

        /// <summary>
        /// 활성 탭의 Miller 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// </summary>
        private void SwitchMillerPanel(string newTabId)
        {
            if (_activeMillerTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeMillerTabId != null && _tabMillerPanels.TryGetValue(_activeMillerTabId, out var oldPanel))
            {
                oldPanel.scroller.Visibility = Visibility.Collapsed;
            }

            // M4: 새 패널 — 없으면 Lazy 생성
            if (!_tabMillerPanels.TryGetValue(newTabId, out var newPanel))
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateMillerPanelForTab(tab);
                }
            }

            if (newPanel.scroller != null)
            {
                newPanel.scroller.Visibility = Visibility.Visible;
                _activeMillerTabId = newTabId;
            }
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Miller 패널 제거.
        /// </summary>
        private void RemoveMillerPanel(string tabId)
        {
            if (_tabMillerPanels.TryGetValue(tabId, out var panel))
            {
                // 이벤트 해제
                panel.scroller.SizeChanged -= OnMillerScrollViewerSizeChanged;
                panel.items.RemoveHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnMillerKeyDown));
                panel.items.RemoveHandler(UIElement.CharacterReceivedEvent,
                    new Windows.Foundation.TypedEventHandler<UIElement, Microsoft.UI.Xaml.Input.CharacterReceivedRoutedEventArgs>(OnMillerCharacterReceived));
                panel.items.ItemsSource = null;
                MillerTabsHost.Children.Remove(panel.scroller);
                _tabMillerPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[MillerPanel] Removed panel for tab {tabId}");
            }
        }

        #endregion

        // =================================================================
        //  Per-Tab Details Panel Management (Show/Hide pattern)
        // =================================================================

        #region Details Panel Management

        /// <summary>
        /// 새 탭에 대한 DetailsModeView 인스턴스 생성.
        /// ContextMenu, HWND 등 설정 후 DetailsTabsHost에 추가.
        /// </summary>
        private Views.DetailsModeView CreateDetailsPanelForTab(Models.TabItem tab)
        {
            var detailsView = new Views.DetailsModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            DetailsTabsHost.Children.Add(detailsView);
            _tabDetailsPanels[tab.Id] = detailsView;
            detailsView.ApplyDensity(_settings.Density);

            Helpers.DebugLogger.Log($"[DetailsPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return detailsView;
        }

        /// <summary>
        /// 활성 탭의 Details 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// shouldCreate=true면 패널이 없을 때 lazy 생성.
        /// </summary>
        private void SwitchDetailsPanel(string newTabId, bool shouldCreate)
        {
            if (_activeDetailsTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeDetailsTabId != null && _tabDetailsPanels.TryGetValue(_activeDetailsTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            // 새 패널 — 없으면 shouldCreate일 때만 Lazy 생성
            if (_tabDetailsPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateDetailsPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeDetailsTabId = newTabId;
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Details 패널 제거.
        /// </summary>
        private void RemoveDetailsPanel(string tabId)
        {
            if (_tabDetailsPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                DetailsTabsHost.Children.Remove(panel);
                _tabDetailsPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[DetailsPanel] Removed panel for tab {tabId}");
            }
        }

        #endregion

        // =================================================================
        //  Per-Tab List Panel Management (Show/Hide pattern)
        // =================================================================

        #region List Panel Management

        private Views.ListModeView CreateListPanelForTab(Models.TabItem tab)
        {
            var listView = new Views.ListModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            ListTabsHost.Children.Add(listView);
            _tabListPanels[tab.Id] = listView;
            listView.ApplyDensity(_settings.Density);

            Helpers.DebugLogger.Log($"[ListPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return listView;
        }

        private void SwitchListPanel(string newTabId, bool shouldCreate)
        {
            if (_activeListTabId == newTabId) return;

            if (_activeListTabId != null && _tabListPanels.TryGetValue(_activeListTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            if (_tabListPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateListPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeListTabId = newTabId;
        }

        private void RemoveListPanel(string tabId)
        {
            if (_tabListPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                ListTabsHost.Children.Remove(panel);
                _tabListPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[ListPanel] Removed panel for tab {tabId}");
            }
        }

        #endregion

        // =================================================================
        //  Per-Tab Icon Panel Management (Show/Hide pattern)
        // =================================================================

        #region Icon Panel Management

        /// <summary>
        /// 새 탭에 대한 IconModeView 인스턴스 생성.
        /// ContextMenu, HWND 등 설정 후 IconTabsHost에 추가.
        /// </summary>
        private Views.IconModeView CreateIconPanelForTab(Models.TabItem tab)
        {
            var iconView = new Views.IconModeView
            {
                IsManualViewModel = true,
                ViewModel = tab.Explorer,
                ContextMenuService = _contextMenuService,
                ContextMenuHost = this,
                OwnerHwnd = _hwnd,
                Visibility = Visibility.Collapsed
            };

            IconTabsHost.Children.Add(iconView);
            _tabIconPanels[tab.Id] = iconView;
            iconView.ApplyDensity(_settings.Density);

            Helpers.DebugLogger.Log($"[IconPanel] Created panel for tab {tab.Id} ({tab.Header})");
            return iconView;
        }

        /// <summary>
        /// 활성 탭의 Icon 패널로 전환 — Visibility 토글만으로 즉시 전환.
        /// shouldCreate=true면 패널이 없을 때 lazy 생성.
        /// </summary>
        private void SwitchIconPanel(string newTabId, bool shouldCreate)
        {
            if (_activeIconTabId == newTabId) return;

            // 이전 패널 숨기기
            if (_activeIconTabId != null && _tabIconPanels.TryGetValue(_activeIconTabId, out var oldPanel))
            {
                oldPanel.Visibility = Visibility.Collapsed;
            }

            // 새 패널 — 없으면 shouldCreate일 때만 Lazy 생성
            if (_tabIconPanels.TryGetValue(newTabId, out var newPanel))
            {
                newPanel.Visibility = Visibility.Visible;
            }
            else if (shouldCreate)
            {
                var tab = ViewModel.Tabs.FirstOrDefault(t => t.Id == newTabId);
                if (tab != null)
                {
                    newPanel = CreateIconPanelForTab(tab);
                    newPanel.Visibility = Visibility.Visible;
                }
            }

            _activeIconTabId = newTabId;
        }

        /// <summary>
        /// 탭 닫힐 때 해당 Icon 패널 제거.
        /// </summary>
        private void RemoveIconPanel(string tabId)
        {
            if (_tabIconPanels.TryGetValue(tabId, out var panel))
            {
                try { panel.Cleanup(); } catch { }
                IconTabsHost.Children.Remove(panel);
                _tabIconPanels.Remove(tabId);
                Helpers.DebugLogger.Log($"[IconPanel] Removed panel for tab {tabId}");
            }
        }

        #endregion

        // =================================================================
        //  Tab Pointer Event Handlers (Click, Drag, Reorder, Tear-off)
        // =================================================================

        #region Tab Pointer Events

        /// <summary>
        /// 탭 아이템 PointerPressed 이벤트. 탭 클릭 시 탭 전환,
        /// 드래그 시작 추적, 마우스 가운데 버튼 클릭 시 탭 닫기 등을 처리한다.
        /// </summary>
        private void OnTabItemPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index >= 0)
                {
                    // Middle-click: close tab (if not last tab)
                    if (e.GetCurrentPoint(fe).Properties.IsMiddleButtonPressed && ViewModel.Tabs.Count > 1)
                    {
                        RemoveMillerPanel(tab.Id);
                        RemoveDetailsPanel(tab.Id);
                        RemoveListPanel(tab.Id);
                        RemoveIconPanel(tab.Id);
                        ViewModel.CloseTab(index);
                        if (ViewModel.ActiveTab != null)
                        {
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                            SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                            SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                        }
                        ResubscribeLeftExplorer();
                        UpdateViewModeVisibility();
                        FocusActiveView();
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                        e.Handled = true;
                        return;
                    }

                    // Record drag start for tear-off detection
                    _tabDragStartPoint = e.GetCurrentPoint(null).Position;
                    _draggingTab = tab;
                    _isTabDragging = false; // Will become true if threshold exceeded

                    // Capture pointer so PointerMoved fires even outside the tab element
                    // 탭 1개: 윈도우 드래그용, 탭 2개+: 떼어내기/재정렬용
                    fe.CapturePointer(e.Pointer);

                    // 특수 탭(Settings/ActionLog)은 Miller/Details/Icon 패널 없음
                    if (tab.ViewMode != ViewMode.Settings && tab.ViewMode != ViewMode.ActionLog)
                    {
                        // ★ 탭 전환 시 phantom SelectionChanged 억제 (500ms)
                        if (tab.Explorer is ViewModels.ExplorerViewModel newExpl)
                            newExpl.TabSwitchSuppressionTicks = Environment.TickCount64 + 500;

                        // Show/Hide 패널 전환 (ViewModel.SwitchToTab 전에 실행하여 바인딩 재평가 방지)
                        SwitchMillerPanel(tab.Id);
                        SwitchDetailsPanel(tab.Id, tab.ViewMode == ViewMode.Details);
                        SwitchListPanel(tab.Id, tab.ViewMode == ViewMode.List);
                        SwitchIconPanel(tab.Id, Helpers.ViewModeExtensions.IsIconMode(tab.ViewMode));
                    }
                    ViewModel.SwitchToTab(index);
                    // LeftExplorer 변경 후 수동으로 필요한 것만 갱신 (PropertyChanged 미발생이므로)
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    UpdateToolbarButtonStates();
                    FocusActiveView();

                    // 탭 전환 시 Quick Look 윈도우 닫기
                    CloseQuickLookWindow();
                }
            }
        }

        /// <summary>
        /// 탭 아이템 PointerMoved 이벤트. 드래그 임계값을 초과하면
        /// 탭 재정렬 또는 탭 떼어내기(tear-off)를 시작한다.
        /// </summary>
        private void OnTabItemPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (_draggingTab == null) return;

            var currentPoint = e.GetCurrentPoint(null).Position;
            double dx = currentPoint.X - _tabDragStartPoint.X;
            double dy = currentPoint.Y - _tabDragStartPoint.Y;

            // Check if drag threshold exceeded
            if (!_isTabDragging)
            {
                if (Math.Sqrt(dx * dx + dy * dy) < TAB_DRAG_THRESHOLD)
                    return;
                _isTabDragging = true;

                // 멀티탭: 드래그 시각 피드백 + Passthrough 확장
                // 싱글탭: 창 이동이므로 비주얼 효과 없음
                if (ViewModel.Tabs.Count > 1)
                {
                    ApplyDragVisual(sender as FrameworkElement, true);
                    ExpandTitleBarPassthrough();
                }
            }

            // 탭 1개일 때: 윈도우 전체를 드래그 이동 + 재도킹 감지
            if (ViewModel.Tabs.Count <= 1)
            {
                Helpers.NativeMethods.GetCursorPos(out var curPos);
                if (!_isWindowDragging)
                {
                    _isWindowDragging = true;
                    _windowDragFrameCount = 0;
                    _windowDragGhostTarget = null;
                    _windowDragStartCursor = curPos;
                    Helpers.NativeMethods.GetWindowRect(_hwnd, out var wr);
                    _windowDragStartRect = wr;
                }
                int newX = _windowDragStartRect.Left + (curPos.X - _windowDragStartCursor.X);
                int newY = _windowDragStartRect.Top + (curPos.Y - _windowDragStartCursor.Y);
                Helpers.NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero,
                    newX, newY, 0, 0,
                    Helpers.NativeMethods.SWP_NOSIZE | Helpers.NativeMethods.SWP_NOZORDER | Helpers.NativeMethods.SWP_NOACTIVATE);

                // 고스트 탭 호버 감지 (4프레임마다, 30프레임 이후부터)
                _windowDragFrameCount++;
                if (_windowDragFrameCount >= 30 && _windowDragFrameCount % 4 == 0)
                {
                    var hoverTarget = App.Current.FindWindowAtPoint(curPos.X, curPos.Y, this);

                    if (hoverTarget != _windowDragGhostTarget)
                    {
                        if (_windowDragGhostTarget != null && !_windowDragGhostTarget._isClosed)
                        {
                            var prev = _windowDragGhostTarget;
                            prev.DispatcherQueue.TryEnqueue(() => prev.HideGhostTab());
                        }
                        _windowDragGhostTarget = hoverTarget;

                        // 드래그 윈도우 반투명/불투명 전환
                        SetWindowOpacity(_hwnd, hoverTarget != null ? (byte)180 : (byte)255);

                        if (hoverTarget != null && !hoverTarget._isClosed)
                        {
                            hoverTarget.DispatcherQueue.TryEnqueue(
                                () => hoverTarget.ShowGhostTab(curPos.X, curPos.Y));
                        }
                    }
                    else if (hoverTarget != null && !hoverTarget._isClosed)
                    {
                        hoverTarget.DispatcherQueue.TryEnqueue(
                            () => hoverTarget.ShowGhostTab(curPos.X, curPos.Y));
                    }
                }
                return;
            }

            // 경계 근접도에 따른 시각 피드백 (분리 임박 힌트)
            UpdateTearOffProximityVisual(sender as FrameworkElement);

            // Check if cursor is outside the window → tear off
            if (IsCursorOutsideWindow())
            {
                var tabToTearOff = _draggingTab;
                _draggingTab = null;
                _isTabDragging = false;

                // 드래그 비주얼 해제
                ApplyDragVisual(sender as FrameworkElement, false);

                // Passthrough 영역 복원
                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);

                // Release pointer capture so the new window can take over
                if (sender is UIElement element)
                {
                    try { element.ReleasePointerCaptures(); } catch { }
                }

                TearOffTab(tabToTearOff);
                return;
            }

            // Cursor is inside the window → handle tab reorder
            var tabIndex = GetTabIndexAtPoint(currentPoint);
            if (tabIndex >= 0)
            {
                int currentIndex = ViewModel.Tabs.IndexOf(_draggingTab!);
                if (currentIndex >= 0 && currentIndex != tabIndex)
                {
                    ViewModel.Tabs.Move(currentIndex, tabIndex);
                    // Update active tab index to follow the moved tab
                    ViewModel.ActiveTabIndex = tabIndex;
                    Helpers.DebugLogger.Log($"[TabReorder] Moved tab from {currentIndex} to {tabIndex}");

                    // Tabs.Move로 ItemsRepeater가 요소를 재배치하면 포인터 캡처가 풀릴 수 있음.
                    // 새 위치의 요소에 캡처를 재설정하여 타이틀바 시스템 드래그 방지.
                    try
                    {
                        if (TabRepeater.TryGetElement(tabIndex) is UIElement newElem)
                        {
                            newElem.CapturePointer(e.Pointer);
                        }
                    }
                    catch { }
                }
            }
        }

        /// <summary>
        /// 탭 아이템 PointerReleased 이벤트. 드래그 상태를 초기화한다.
        /// </summary>
        private void OnTabItemPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            // 드래그 비주얼 해제
            ApplyDragVisual(sender as FrameworkElement, false);

            // 싱글-탭 윈도우 드래그 중 재도킹 처리
            if (_isWindowDragging && ViewModel.Tabs.Count <= 1)
            {
                Helpers.NativeMethods.GetCursorPos(out var dropPos);

                // 고스트 탭 정리
                if (_windowDragGhostTarget != null)
                {
                    var gt = _windowDragGhostTarget;
                    _windowDragGhostTarget = null;
                    gt.DispatcherQueue.TryEnqueue(() => gt.HideGhostTab());
                }

                // 30프레임 이상 드래그했을 때만 재도킹 시도
                var targetWindow = _windowDragFrameCount >= 30
                    ? App.Current.FindWindowAtPoint(dropPos.X, dropPos.Y, this)
                    : null;

                if (targetWindow != null && targetWindow != this)
                {
                    var tab = ViewModel.ActiveTab;
                    if (tab != null)
                    {
                        ViewModel.SaveActiveTabState();
                        var dockDto = new Models.TabStateDto(
                            tab.Id, tab.Header, tab.Path,
                            (int)tab.ViewMode, (int)tab.IconSize);

                        int ghostIdx = targetWindow._ghostTabIndex;

                        // 포인터 캡처 먼저 해제
                        if (sender is UIElement el)
                        {
                            try { el.ReleasePointerCaptures(); } catch { }
                        }
                        _draggingTab = null;
                        _isTabDragging = false;
                        _isWindowDragging = false;

                        // 타겟 창에 탭 도킹
                        targetWindow.DispatcherQueue.TryEnqueue(() =>
                        {
                            targetWindow.DockTab(dockDto, ghostIdx);
                            Helpers.DebugLogger.Log($"[ReDock] Single-tab '{dockDto.Header}' merged into target at index {ghostIdx}");
                        });

                        // 현재 창 닫기를 다음 프레임으로 지연 (이벤트 핸들러 완료 후)
                        DispatcherQueue.TryEnqueue(() =>
                        {
                            _forceClose = true;
                            _isClosed = true;
                            App.Current.UnregisterWindow(this);
                            Close();
                        });
                        return;
                    }
                }
            }

            // 반투명 해제 (도킹 안 했을 경우)
            if (_isWindowDragging)
                SetWindowOpacity(_hwnd, 255);

            _draggingTab = null;
            _isTabDragging = false;
            _isWindowDragging = false;
            _windowDragGhostTarget = null;
            if (sender is UIElement element)
            {
                try { element.ReleasePointerCaptures(); } catch { }
            }
            // Update title bar input regions since tabs may have been reordered
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        /// <summary>
        /// Returns the tab index at the given point (relative to the window).
        /// Tab width is dynamically calculated (Chrome-style).
        /// </summary>
        private int GetTabIndexAtPoint(Windows.Foundation.Point windowPoint)
        {
            try
            {
                // Convert window point to position relative to the TabRepeater
                var transform = TabRepeater.TransformToVisual(null);
                var tabBarOrigin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                double relativeX = windowPoint.X - tabBarOrigin.X;
                if (relativeX < 0) return 0;

                // Dynamic tab width (Chrome-style) + 0px spacing (StackLayout Spacing=0)
                int index = (int)(relativeX / _calculatedTabWidth);
                return Math.Clamp(index, 0, ViewModel.Tabs.Count - 1);
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// 커서가 윈도우 영역 바깥에 있는지 확인한다.
        /// 탭 tear-off 판단에 사용된다.
        /// </summary>
        private bool IsCursorOutsideWindow()
        {
            if (!Helpers.NativeMethods.GetCursorPos(out var cursorPos))
                return false;
            if (!Helpers.NativeMethods.GetWindowRect(_hwnd, out var windowRect))
                return false;

            return cursorPos.X < windowRect.Left ||
                   cursorPos.X > windowRect.Right ||
                   cursorPos.Y < windowRect.Top ||
                   cursorPos.Y > windowRect.Bottom;
        }

        /// <summary>
        /// 드래그 시작/종료 시 탭에 시각 피드백 적용.
        /// 시작: 반투명 + 위로 살짝 이동, 종료: 원래 상태 복원.
        /// </summary>
        private void ApplyDragVisual(FrameworkElement? tabElement, bool isDragging)
        {
            if (tabElement == null) return;
            try
            {
                if (isDragging)
                {
                    tabElement.Opacity = 0.6;
                    tabElement.Translation = new System.Numerics.Vector3(0, -3, 0);
                }
                else
                {
                    tabElement.Opacity = 1.0;
                    tabElement.Translation = new System.Numerics.Vector3(0, 0, 0);
                }
            }
            catch { }
        }

        /// <summary>
        /// 커서가 윈도우 경계에 가까울수록 탭 투명도를 높여
        /// 분리(tear-off) 임박을 시각적으로 알린다.
        /// 경계 30px 이내에서 0.6 → 0.3으로 페이드.
        /// </summary>
        private void UpdateTearOffProximityVisual(FrameworkElement? tabElement)
        {
            if (tabElement == null || ViewModel.Tabs.Count <= 1) return;
            try
            {
                if (!Helpers.NativeMethods.GetCursorPos(out var cursorPos)) return;
                if (!Helpers.NativeMethods.GetWindowRect(_hwnd, out var rect)) return;

                double scale = AppTitleBar?.XamlRoot?.RasterizationScale ?? 1.0;
                int edgeThreshold = (int)(30 * scale); // 30 DIP

                // 경계까지의 최소 거리 (물리 픽셀)
                int distLeft = cursorPos.X - rect.Left;
                int distRight = rect.Right - cursorPos.X;
                int distTop = cursorPos.Y - rect.Top;
                int distBottom = rect.Bottom - cursorPos.Y;
                int minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

                if (minDist < edgeThreshold)
                {
                    // 경계에 가까울수록 투명 (0.6 → 0.3)
                    double ratio = Math.Max(0, (double)minDist / edgeThreshold);
                    tabElement.Opacity = 0.3 + 0.3 * ratio;
                    // 위로 더 올라가는 느낌 (-3 → -6)
                    float lift = -3f - 3f * (1f - (float)ratio);
                    tabElement.Translation = new System.Numerics.Vector3(0, lift, 0);
                }
                else
                {
                    // 기본 드래그 상태 유지
                    tabElement.Opacity = 0.6;
                    tabElement.Translation = new System.Numerics.Vector3(0, -3, 0);
                }
            }
            catch { }
        }

        #endregion

        // =================================================================
        //  New Window / Tear-off / Manual Drag
        // =================================================================

        #region Tear-off and Window Management

        /// <summary>
        /// Open a new window at the current path (Ctrl+N).
        /// </summary>
        private void OpenNewWindow()
        {
            try
            {
                // Build a TabStateDto from the current active tab state
                ViewModel.SaveActiveTabState();
                var activeTab = ViewModel.ActiveTab;
                var currentPath = ViewModel.ActiveExplorer?.CurrentPath ?? string.Empty;
                var header = activeTab?.Header ?? _loc.Get("Home");
                var viewMode = activeTab != null ? (int)activeTab.ViewMode : (int)ViewMode.MillerColumns;
                var iconSize = activeTab != null ? (int)activeTab.IconSize : (int)ViewMode.IconMedium;

                var dto = new Models.TabStateDto(
                    System.Guid.NewGuid().ToString("N")[..8],
                    header,
                    currentPath,
                    viewMode,
                    iconSize);

                var newWindow = new MainWindow();
                newWindow._pendingTearOff = dto;

                App.Current.RegisterWindow(newWindow);
                newWindow.Activate();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[OpenNewWindow] Error: {ex.Message}");
            }
        }

        private void TearOffTab(Models.TabItem tab)
        {
            try
            {
                // 1. Save tab state as DTO
                ViewModel.SaveActiveTabState();
                var dto = new Models.TabStateDto(
                    tab.Id, tab.Header, tab.Path,
                    (int)tab.ViewMode, (int)tab.IconSize);

                // 2. 원본 창의 Win32 사이즈 (물리 픽셀) + 커서 위치 캡처
                Helpers.NativeMethods.GetWindowRect(_hwnd, out var srcRect);
                int srcW = srcRect.Right - srcRect.Left;
                int srcH = srcRect.Bottom - srcRect.Top;
                Helpers.NativeMethods.GetCursorPos(out var cursorPos);

                // 3. Remove tab from current window (panels + ViewModel)
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index < 0) return;

                RemoveMillerPanel(tab.Id);
                RemoveDetailsPanel(tab.Id);
                RemoveListPanel(tab.Id);
                RemoveIconPanel(tab.Id);
                ViewModel.CloseTab(index);

                // Switch panels for the new active tab
                if (ViewModel.ActiveTab != null)
                {
                    SwitchMillerPanel(ViewModel.ActiveTab.Id);
                    SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                    SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                    SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                }
                ResubscribeLeftExplorer();
                UpdateViewModeVisibility();
                FocusActiveView();

                // 4. 새 창 생성 + HWND 확보
                var newWindow = new MainWindow();
                newWindow._pendingTearOff = dto;
                var newHwnd = WinRT.Interop.WindowNative.GetWindowHandle(newWindow);

                // 5. DWMWA_CLOAK — 창을 DWM에서 합성하되 화면에 숨김 (깜빡임 방지)
                int cloakOn = 1;
                Helpers.NativeMethods.DwmSetWindowAttribute(newHwnd,
                    Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOn, sizeof(int));
                int transOff = 1;
                Helpers.NativeMethods.DwmSetWindowAttribute(newHwnd,
                    Helpers.NativeMethods.DWMWA_TRANSITIONS_FORCEDISABLED, ref transOff, sizeof(int));

                // 6. Activate — XAML 파이프라인 시작 (클로킹 상태라 화면에 안 보임)
                App.Current.RegisterWindow(newWindow);
                newWindow.Activate();

                // 7. 초기 위치/크기 설정 + DPI 로깅
                int offsetX = srcW / 4;  // 커서가 타이틀바 왼쪽 25% 지점
                int offsetY = 15;         // 커서가 타이틀바 상단 근처

                uint srcDpi = Helpers.NativeMethods.GetDpiForWindow(_hwnd);
                uint newDpi = Helpers.NativeMethods.GetDpiForWindow(newHwnd);
                Helpers.DebugLogger.Log($"[TearOff] srcDpi={srcDpi}, newDpi={newDpi}, srcSize={srcW}x{srcH}");

                // SetWindowPos로 초기 위치/크기 (Activate 후 재적용은 타이머에서)
                Helpers.NativeMethods.SetWindowPos(newHwnd, Helpers.NativeMethods.HWND_TOP,
                    cursorPos.X - offsetX,
                    cursorPos.Y - offsetY,
                    srcW, srcH,
                    Helpers.NativeMethods.SWP_NOACTIVATE);

                // 8. 수동 드래그 시작 — 타이머 첫 틱에서 크기도 재적용 (Activate 레이아웃 덮어쓰기 방지)
                StartManualWindowDrag(newHwnd, offsetX, offsetY, srcW, srcH);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[TearOff] Error: {ex.Message}");
            }
        }

        /// <summary>
        /// 수동 창 드래그: DispatcherTimer로 커서를 추적하여 SetWindowPos로 창 이동.
        /// SC_DRAGMOVE를 대체 (WinUI 3에서 NC 메시지가 필터링되어 SC_DRAGMOVE 동작 안함).
        /// 타이머는 원본 창의 DispatcherQueue에서 실행 (새 창은 아직 초기화 중일 수 있음).
        /// </summary>
        private void StartManualWindowDrag(IntPtr targetHwnd, int dragOffsetX, int dragOffsetY,
            int targetWidth, int targetHeight)
        {
            var dragTimer = new DispatcherTimer();
            dragTimer.Interval = TimeSpan.FromMilliseconds(8); // ~120Hz 부드러운 추적

            bool uncloaked = false;
            bool sizeApplied = false;
            int frameCount = 0;
            MainWindow? lastGhostTarget = null; // 고스트 탭이 표시된 타깃 창 추적

            dragTimer.Tick += (s, e) =>
            {
                if (_isClosed)
                {
                    dragTimer.Stop();
                    // 고스트 정리
                    if (lastGhostTarget != null)
                    {
                        var gt = lastGhostTarget;
                        lastGhostTarget = null;
                        gt.DispatcherQueue.TryEnqueue(() => gt.HideGhostTab());
                    }
                    return;
                }

                // 1. 마우스 왼쪽 버튼 하드웨어 상태 확인 (메시지 큐와 무관)
                bool mouseDown = (Helpers.NativeMethods.GetAsyncKeyState(
                    Helpers.NativeMethods.VK_LBUTTON) & 0x8000) != 0;

                if (!mouseDown)
                {
                    // 마우스 놓음 → 드래그 종료
                    dragTimer.Stop();

                    // Find the new torn-off window by HWND (need it first to exclude from hit-test)
                    MainWindow? newWindow = null;
                    foreach (var w in ((App)App.Current).GetRegisteredWindows())
                    {
                        if (w is MainWindow mw && WinRT.Interop.WindowNative.GetWindowHandle(mw) == targetHwnd)
                        {
                            newWindow = mw;
                            break;
                        }
                    }

                    // Check for re-docking: is the cursor over another Span window's tab bar?
                    // Exclude the dragged window (newWindow) from hit-test.
                    // Require minimum 30 frames (~240ms) of dragging to prevent
                    // accidental re-dock when cursor is still near the original window.
                    Helpers.NativeMethods.GetCursorPos(out var dropPos);
                    var targetWindow = frameCount >= 30
                        ? App.Current.FindWindowAtPoint(
                            dropPos.X, dropPos.Y, (Window?)newWindow ?? this)
                        : null;

                    // 고스트 탭 정리 (재도킹 여부와 무관하게 항상)
                    if (lastGhostTarget != null)
                    {
                        var gt = lastGhostTarget;
                        lastGhostTarget = null;
                        gt.DispatcherQueue.TryEnqueue(() => gt.HideGhostTab());
                    }

                    // 반투명 해제 (재도킹하든 안 하든)
                    SetWindowOpacity(targetHwnd, 255);

                    if (targetWindow != null && newWindow != null
                        && targetWindow != newWindow  // 자기 자신에게 재도킹 방지
                        && newWindow.ViewModel.Tabs.Count > 0)
                    {
                        // Re-dock: transfer tab from new window to target window
                        var tab = newWindow.ViewModel.ActiveTab;
                        if (tab != null)
                        {
                            newWindow.ViewModel.SaveActiveTabState();
                            var dockDto = new Models.TabStateDto(
                                tab.Id, tab.Header, tab.Path,
                                (int)tab.ViewMode, (int)tab.IconSize);

                            // 고스트 인덱스를 캡처 (DockTab에서 사용)
                            int ghostIdx = targetWindow._ghostTabIndex;

                            // Close the new (torn-off) window
                            newWindow._forceClose = true;
                            newWindow._isClosed = true;
                            App.Current.UnregisterWindow(newWindow);
                            newWindow.Close();

                            // Dock the tab into the target window.
                            if (targetWindow == this)
                            {
                                // Same window — call directly (same thread)
                                targetWindow.DockTab(dockDto, ghostIdx);
                                Helpers.DebugLogger.Log($"[ReDock] Tab '{dockDto.Header}' merged into same window at index {ghostIdx}");
                            }
                            else
                            {
                                // Different window — must dispatch to its UI thread
                                targetWindow.DispatcherQueue.TryEnqueue(() =>
                                {
                                    targetWindow.DockTab(dockDto, ghostIdx);
                                    Helpers.DebugLogger.Log($"[ReDock] Tab '{dockDto.Header}' merged into other window at index {ghostIdx}");
                                });
                            }
                            return;
                        }
                    }

                    // 최종 크기 보정 (Activate 레이아웃이 덮어썼을 수 있음)
                    if (!sizeApplied)
                    {
                        Helpers.NativeMethods.GetCursorPos(out var finalPos2);
                        Helpers.NativeMethods.SetWindowPos(
                            targetHwnd, Helpers.NativeMethods.HWND_TOP,
                            finalPos2.X - dragOffsetX, finalPos2.Y - dragOffsetY,
                            targetWidth, targetHeight, 0);
                    }

                    if (!uncloaked)
                    {
                        int cloakOff = 0;
                        Helpers.NativeMethods.DwmSetWindowAttribute(targetHwnd,
                            Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                    }
                    Helpers.NativeMethods.SetForegroundWindow(targetHwnd);
                    return;
                }

                // 2. 현재 커서 위치
                if (!Helpers.NativeMethods.GetCursorPos(out var pos))
                    return;

                frameCount++;

                // 3. 첫 몇 프레임: 크기 포함하여 SetWindowPos (Activate의 기본 크기를 강제 덮어씀)
                if (!sizeApplied && frameCount <= 3)
                {
                    Helpers.NativeMethods.SetWindowPos(
                        targetHwnd, Helpers.NativeMethods.HWND_TOP,
                        pos.X - dragOffsetX,
                        pos.Y - dragOffsetY,
                        targetWidth, targetHeight,
                        Helpers.NativeMethods.SWP_NOACTIVATE);

                    if (frameCount == 3) sizeApplied = true;
                }
                else
                {
                    // 이후: 위치만 이동 (크기는 확정됨)
                    Helpers.NativeMethods.SetWindowPos(
                        targetHwnd, Helpers.NativeMethods.HWND_TOP,
                        pos.X - dragOffsetX,
                        pos.Y - dragOffsetY,
                        0, 0,
                        Helpers.NativeMethods.SWP_NOSIZE | Helpers.NativeMethods.SWP_NOACTIVATE);
                }

                // 4. 몇 프레임 후 클로킹 해제 (XAML이 첫 프레임을 렌더링할 시간 확보)
                if (!uncloaked && frameCount >= 5) // ~40ms
                {
                    uncloaked = true;
                    int cloakOff = 0;
                    Helpers.NativeMethods.DwmSetWindowAttribute(targetHwnd,
                        Helpers.NativeMethods.DWMWA_CLOAK, ref cloakOff, sizeof(int));
                    Helpers.NativeMethods.SetForegroundWindow(targetHwnd);
                }

                // 5. 고스트 탭 호버 감지 — 4프레임(~32ms)마다 체크, 30프레임 이후부터
                if (uncloaked && frameCount >= 30 && frameCount % 4 == 0)
                {
                    // Find the new torn-off window to exclude from hit-test
                    MainWindow? draggedWindow = null;
                    foreach (var w in ((App)App.Current).GetRegisteredWindows())
                    {
                        if (w is MainWindow mw && WinRT.Interop.WindowNative.GetWindowHandle(mw) == targetHwnd)
                        {
                            draggedWindow = mw;
                            break;
                        }
                    }

                    var hoverTarget = App.Current.FindWindowAtPoint(
                        pos.X, pos.Y, (Window?)draggedWindow ?? this);

                    if (hoverTarget != lastGhostTarget)
                    {
                        // 이전 타깃의 고스트 숨기기
                        if (lastGhostTarget != null && !lastGhostTarget._isClosed)
                        {
                            var prevTarget = lastGhostTarget;
                            prevTarget.DispatcherQueue.TryEnqueue(() => prevTarget.HideGhostTab());
                        }

                        lastGhostTarget = hoverTarget;

                        // 드래그 윈도우 반투명/불투명 전환
                        if (hoverTarget != null)
                            SetWindowOpacity(targetHwnd, 180); // 도킹 가능 → 반투명
                        else
                            SetWindowOpacity(targetHwnd, 255); // 도킹 불가 → 불투명

                        // 새 타깃에 고스트 표시
                        if (hoverTarget != null && !hoverTarget._isClosed)
                        {
                            hoverTarget.DispatcherQueue.TryEnqueue(
                                () => hoverTarget.ShowGhostTab(pos.X, pos.Y));
                        }
                    }
                    else if (hoverTarget != null && !hoverTarget._isClosed)
                    {
                        // 같은 타깃 — 위치만 업데이트
                        hoverTarget.DispatcherQueue.TryEnqueue(
                            () => hoverTarget.ShowGhostTab(pos.X, pos.Y));
                    }
                }
            };

            dragTimer.Start();
        }

        #endregion

        // =================================================================
        //  Title Bar Regions (Passthrough for tab interaction)
        // =================================================================

        #region Title Bar Regions

        /// <summary>
        /// MS 공식 패턴: SetTitleBar(AppTitleBar)가 드래그/캡션 버튼을 자동 관리.
        /// Passthrough 영역 = TabBarContent(StackPanel)의 실제 콘텐츠 영역을
        /// ScrollViewer 뷰포트에 클리핑한 교집합.
        /// → 탭 오른쪽 빈 공간은 드래그 영역으로 유지
        /// → 스크롤 시에도 캡션 버튼 영역을 넘지 않음
        /// </summary>
        private void UpdateTitleBarRegions()
        {
            try
            {
                if (_isClosed || TabScrollViewer == null || TabRepeater == null) return;
                if (!ExtendsContentIntoTitleBar) return;
                if (AppTitleBar?.XamlRoot == null) return;

                double scale = AppTitleBar.XamlRoot.RasterizationScale;

                // 캡션 버튼 영역 확보
                RightPaddingColumn.Width = new GridLength(
                    this.AppWindow.TitleBar.RightInset / scale);

                // ScrollViewer 뷰포트 경계 (클리핑용)
                GeneralTransform svTransform = TabScrollViewer.TransformToVisual(null);
                Windows.Foundation.Rect svBounds = svTransform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0,
                        TabScrollViewer.ActualWidth,
                        TabScrollViewer.ActualHeight));

                var rects = new List<Windows.Graphics.RectInt32>();

                // 각 탭 요소를 개별 Passthrough rect로 등록
                if (TabRepeater.ItemsSourceView != null)
                {
                    for (int i = 0; i < TabRepeater.ItemsSourceView.Count; i++)
                    {
                        if (TabRepeater.TryGetElement(i) is not FrameworkElement element) continue;

                        var clipped = GetClippedRect(element, svBounds);
                        if (clipped.HasValue)
                        {
                            rects.Add(ToRectInt32(clipped.Value, scale));
                        }
                    }
                }

                // + (New Tab) 버튼도 Passthrough로 등록
                if (NewTabButton != null)
                {
                    var clipped = GetClippedRect(NewTabButton, svBounds);
                    if (clipped.HasValue)
                    {
                        rects.Add(ToRectInt32(clipped.Value, scale));
                    }
                }

                var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
                nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, rects.ToArray());
            }
            catch { /* Layout not ready yet */ }
        }

        /// <summary>
        /// 탭 드래그 중 타이틀바 전체(캡션 버튼 제외)를 Passthrough로 확장.
        /// IXP 레이어가 드래그를 캡션 드래그(창 이동)로 인식하는 것을 방지한다.
        /// 드래그 종료 시 UpdateTitleBarRegions()로 원래 영역을 복원해야 한다.
        /// </summary>
        private void ExpandTitleBarPassthrough()
        {
            try
            {
                if (_isClosed || AppTitleBar?.XamlRoot == null) return;
                if (!ExtendsContentIntoTitleBar) return;

                double scale = AppTitleBar.XamlRoot.RasterizationScale;

                // AppTitleBar 전체 영역을 Passthrough로 (캡션 버튼 영역은 RightInset으로 제외)
                GeneralTransform transform = AppTitleBar.TransformToVisual(null);
                var bounds = transform.TransformBounds(
                    new Windows.Foundation.Rect(0, 0,
                        AppTitleBar.ActualWidth, AppTitleBar.ActualHeight));

                // 캡션 버튼 영역(최소화/최대화/닫기) 폭을 제외
                double rightInset = this.AppWindow.TitleBar.RightInset / scale;
                double passthroughWidth = bounds.Width - rightInset;
                if (passthroughWidth <= 0) return;

                var rect = new Windows.Graphics.RectInt32(
                    (int)Math.Round(bounds.X * scale),
                    (int)Math.Round(bounds.Y * scale),
                    (int)Math.Round(passthroughWidth * scale),
                    (int)Math.Round(bounds.Height * scale));

                var nonClientInputSrc = InputNonClientPointerSource.GetForWindowId(this.AppWindow.Id);
                nonClientInputSrc.SetRegionRects(NonClientRegionKind.Passthrough, new[] { rect });
            }
            catch { }
        }

        /// <summary>
        /// 요소의 bounds를 뷰포트에 클리핑하여 반환. 뷰포트 밖이면 null.
        /// </summary>
        private static Windows.Foundation.Rect? GetClippedRect(
            FrameworkElement element, Windows.Foundation.Rect viewport)
        {
            GeneralTransform transform = element.TransformToVisual(null);
            Windows.Foundation.Rect bounds = transform.TransformBounds(
                new Windows.Foundation.Rect(0, 0,
                    element.ActualWidth, element.ActualHeight));

            double left = Math.Max(bounds.X, viewport.X);
            double top = Math.Max(bounds.Y, viewport.Y);
            double right = Math.Min(bounds.X + bounds.Width, viewport.X + viewport.Width);
            double bottom = Math.Min(bounds.Y + bounds.Height, viewport.Y + viewport.Height);

            if (right > left && bottom > top)
                return new Windows.Foundation.Rect(left, top, right - left, bottom - top);
            return null;
        }

        private static Windows.Graphics.RectInt32 ToRectInt32(
            Windows.Foundation.Rect rect, double scale)
        {
            return new Windows.Graphics.RectInt32(
                (int)Math.Round(rect.X * scale),
                (int)Math.Round(rect.Y * scale),
                Math.Max(0, (int)Math.Round(rect.Width * scale)),
                Math.Max(0, (int)Math.Round(rect.Height * scale)));
        }

        #endregion

        // =================================================================
        //  Tab Close / New Tab / Context Menu / Duplicate
        // =================================================================

        #region Tab Close, New, Context Menu, Duplicate

        private void OnTabCloseClick(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                int index = ViewModel.Tabs.IndexOf(tab);
                if (index >= 0)
                {
                    if (tab.ViewMode == ViewMode.Settings)
                    {
                        // Settings 탭은 Miller/Details/Icon 패널 없으므로 제거 스킵
                        // 임시로 활성 탭 인덱스 보정 후 CloseTab
                        ViewModel.CloseTab(index);
                        if (ViewModel.ActiveTab != null && ViewModel.ActiveTab.ViewMode != ViewMode.Settings)
                        {
                            // ★ 탭 전환 시 phantom SelectionChanged 억제 (500ms)
                            if (ViewModel.ActiveTab.Explorer is ViewModels.ExplorerViewModel settingsCloseExpl)
                                settingsCloseExpl.TabSwitchSuppressionTicks = Environment.TickCount64 + 500;
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        }
                    }
                    else
                    {
                        // 패널 제거 (닫히는 탭)
                        RemoveMillerPanel(tab.Id);
                        RemoveDetailsPanel(tab.Id);
                        RemoveListPanel(tab.Id);
                        RemoveIconPanel(tab.Id);
                        ViewModel.CloseTab(index);
                        // CloseTab이 SwitchToTab을 호출하면 활성 탭이 변경됨 — 패널 전환
                        if (ViewModel.ActiveTab != null)
                        {
                            // ★ 탭 전환 시 phantom SelectionChanged 억제 (500ms)
                            if (ViewModel.ActiveTab.Explorer is ViewModels.ExplorerViewModel closeSwitchExpl)
                                closeSwitchExpl.TabSwitchSuppressionTicks = Environment.TickCount64 + 500;
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                            SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                            SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                        }
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    // Tab count changed — update passthrough region
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                }
            }
        }

        /// <summary>
        /// 새 탭 버튼 클릭 이벤트. 현재 활성 탭의 경로로 새 탭을 생성한다.
        /// </summary>
        private void OnNewTabClick(object sender, RoutedEventArgs e)
        {
            ViewModel.AddNewTab();
            // 새 탭의 패널 생성 및 전환
            var newTab = ViewModel.ActiveTab;
            if (newTab != null)
            {
                CreateMillerPanelForTab(newTab);
                SwitchMillerPanel(newTab.Id);
                // Details/Icon은 ViewMode 전환 시 lazy 생성 (새 탭은 보통 Home 또는 Miller)
                SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
                SwitchListPanel(newTab.Id, newTab.ViewMode == ViewMode.List);
                SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
            }
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
            // Tab count changed — update passthrough region
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        // =================================================================
        //  Tab Context Menu (Right-click on tab)
        // =================================================================

        private void OnTabRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is Models.TabItem tab)
            {
                e.Handled = true;

                var flyout = new MenuFlyout();

                // Close Tab
                var closeItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseTab"),
                    Icon = new FontIcon { Glyph = "\uE711" }
                };
                closeItem.Click += (s, args) =>
                {
                    int index = ViewModel.Tabs.IndexOf(tab);
                    if (index >= 0 && ViewModel.Tabs.Count > 1)
                    {
                        RemoveMillerPanel(tab.Id);
                        RemoveDetailsPanel(tab.Id);
                        RemoveListPanel(tab.Id);
                        RemoveIconPanel(tab.Id);
                        ViewModel.CloseTab(index);
                        if (ViewModel.ActiveTab != null)
                        {
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                            SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                            SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                        }
                        ResubscribeLeftExplorer();
                        UpdateViewModeVisibility();
                        FocusActiveView();
                        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                    }
                };
                closeItem.IsEnabled = ViewModel.Tabs.Count > 1;
                flyout.Items.Add(closeItem);

                // Close Other Tabs
                var closeOthersItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseOtherTabs"),
                };
                closeOthersItem.Click += (s, args) =>
                {
                    var closedIds = ViewModel.CloseOtherTabs(tab);
                    foreach (var id in closedIds)
                    {
                        RemoveMillerPanel(id);
                        RemoveDetailsPanel(id);
                        RemoveListPanel(id);
                        RemoveIconPanel(id);
                    }
                    if (ViewModel.ActiveTab != null)
                    {
                        SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                        SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                        SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                };
                closeOthersItem.IsEnabled = ViewModel.Tabs.Count > 1;
                flyout.Items.Add(closeOthersItem);

                // Close Tabs to Right
                var closeRightItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("CloseTabsToRight"),
                };
                int tabIndex = ViewModel.Tabs.IndexOf(tab);
                closeRightItem.Click += (s, args) =>
                {
                    var closedIds = ViewModel.CloseTabsToRight(tab);
                    foreach (var id in closedIds)
                    {
                        RemoveMillerPanel(id);
                        RemoveDetailsPanel(id);
                        RemoveListPanel(id);
                        RemoveIconPanel(id);
                    }
                    if (ViewModel.ActiveTab != null)
                    {
                        SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        SwitchDetailsPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.Details);
                        SwitchListPanel(ViewModel.ActiveTab.Id, ViewModel.ActiveTab.ViewMode == ViewMode.List);
                        SwitchIconPanel(ViewModel.ActiveTab.Id, Helpers.ViewModeExtensions.IsIconMode(ViewModel.ActiveTab.ViewMode));
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
                };
                closeRightItem.IsEnabled = tabIndex < ViewModel.Tabs.Count - 1;
                flyout.Items.Add(closeRightItem);

                flyout.Items.Add(new MenuFlyoutSeparator());

                // Move to New Window
                var moveToNewWindowItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("MoveToNewWindow"),
                    Icon = new FontIcon { Glyph = "\uE8A7" }
                };
                moveToNewWindowItem.Click += (s, args) =>
                {
                    TearOffTab(tab);
                };
                moveToNewWindowItem.IsEnabled = ViewModel.Tabs.Count > 1;
                flyout.Items.Add(moveToNewWindowItem);

                // Duplicate Tab
                var duplicateItem = new MenuFlyoutItem
                {
                    Text = _loc.Get("DuplicateTab"),
                    Icon = new FontIcon { Glyph = "\uE8C8" }
                };
                duplicateItem.Click += (s, args) =>
                {
                    HandleDuplicateTab(tab);
                };
                flyout.Items.Add(duplicateItem);

                flyout.Items.Add(new MenuFlyoutSeparator());
                var saveWorkspaceItem = new MenuFlyoutItem
                {
                    Text = _loc?.Get("Workspace_Save") ?? "Save tab layout...",
                    Icon = new FontIcon { Glyph = "\uE74E" }
                };
                Helpers.CursorHelper.SetHandCursor(saveWorkspaceItem);
                saveWorkspaceItem.Click += async (s, args) => await ShowSaveWorkspaceDialogAsync();
                flyout.Items.Add(saveWorkspaceItem);

                flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(fe)
                });
            }
        }

        private void HandleDuplicateTab(Models.TabItem sourceTab)
        {
            var newTab = ViewModel.DuplicateTab(sourceTab);
            CreateMillerPanelForTab(newTab);
            SwitchMillerPanel(newTab.Id);
            SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
            SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        #endregion

        // =================================================================
        //  Tab Docking (Accept tab from another window)
        // =================================================================

        #region Tab Docking

        /// <summary>
        /// Ghost tab state: tracks which tab index has a gap for the dock preview.
        /// -1 means no ghost is active.
        /// </summary>
        private int _ghostTabIndex = -1;

        /// <summary>
        /// Show a ghost tab indicator at the given screen position.
        /// Opens a gap between existing tabs and displays a translucent placeholder.
        /// Called from the drag timer on the source window via DispatcherQueue.TryEnqueue.
        /// </summary>
        public void ShowGhostTab(int screenX, int screenY)
        {
            try
            {
                if (_isClosed || TabRepeater == null || GhostTabIndicator == null) return;

                // Convert screen coords to position relative to TabRepeater
                int insertIndex = GetInsertIndexFromScreen(screenX);

                if (insertIndex == _ghostTabIndex) return; // no change

                // Remove margin from previous ghost position
                ClearTabMargins();

                _ghostTabIndex = insertIndex;
                int tabCount = ViewModel?.Tabs?.Count ?? 0;

                // 고스트 탭 너비 = 도킹 후 예상 탭 너비 (탭 1개 추가 시뮬레이션)
                double ghostWidth = CalculateTabWidthForCount(tabCount + 1);

                // Add left margin to the tab at insertIndex to create the gap
                if (insertIndex < tabCount && TabRepeater.TryGetElement(insertIndex) is FrameworkElement elem)
                {
                    elem.Margin = new Thickness(ghostWidth, 0, 0, 0);
                }

                // Position the ghost indicator overlay
                double xPos = 0;
                if (insertIndex > 0 && insertIndex <= tabCount)
                {
                    xPos = insertIndex * _calculatedTabWidth;
                }
                if (insertIndex >= tabCount)
                {
                    xPos = tabCount * _calculatedTabWidth;
                }

                GhostTabIndicator.Width = ghostWidth;
                GhostTabIndicator.Margin = new Thickness(xPos, 0, 0, 0);
                GhostTabIndicator.Visibility = Microsoft.UI.Xaml.Visibility.Visible;

                Helpers.DebugLogger.Log($"[GhostTab] Show at index={insertIndex}, x={xPos:F0}, w={ghostWidth:F0}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[GhostTab] ShowGhostTab error: {ex.Message}");
            }
        }

        /// <summary>
        /// 탭이 N개일 때의 예상 탭 너비를 계산한다 (도킹 후 크기 시뮬레이션).
        /// </summary>
        private double CalculateTabWidthForCount(int tabCount)
        {
            if (tabCount <= 0) return MAX_TAB_WIDTH;
            double availableWidth = TabScrollViewer?.ActualWidth ?? 800;
            double newTabBtnWidth = 38;
            double available = availableWidth - newTabBtnWidth;
            return Math.Max(MIN_TAB_WIDTH, Math.Min(MAX_TAB_WIDTH, available / tabCount));
        }

        /// <summary>
        /// Hide the ghost tab indicator and restore normal tab layout.
        /// </summary>
        public void HideGhostTab()
        {
            try
            {
                if (GhostTabIndicator != null)
                    GhostTabIndicator.Visibility = Microsoft.UI.Xaml.Visibility.Collapsed;

                ClearTabMargins();
                _ghostTabIndex = -1;
            }
            catch { }
        }

        /// <summary>
        /// 윈도우를 반투명으로 만든다 (도킹 가능 시각 피드백).
        /// WS_EX_LAYERED + SetLayeredWindowAttributes 사용.
        /// </summary>
        internal static void SetWindowOpacity(IntPtr hwnd, byte alpha)
        {
            int exStyle = Helpers.NativeMethods.GetWindowLong(hwnd, Helpers.NativeMethods.GWL_EXSTYLE);
            if (alpha < 255)
            {
                // Add WS_EX_LAYERED if not already set
                if ((exStyle & Helpers.NativeMethods.WS_EX_LAYERED) == 0)
                    Helpers.NativeMethods.SetWindowLong(hwnd, Helpers.NativeMethods.GWL_EXSTYLE,
                        exStyle | Helpers.NativeMethods.WS_EX_LAYERED);
                Helpers.NativeMethods.SetLayeredWindowAttributes(hwnd, 0, alpha, Helpers.NativeMethods.LWA_ALPHA);
            }
            else
            {
                // Remove WS_EX_LAYERED to restore full opacity (avoids compositing overhead)
                if ((exStyle & Helpers.NativeMethods.WS_EX_LAYERED) != 0)
                    Helpers.NativeMethods.SetWindowLong(hwnd, Helpers.NativeMethods.GWL_EXSTYLE,
                        exStyle & ~Helpers.NativeMethods.WS_EX_LAYERED);
            }
        }

        /// <summary>
        /// Remove any ghost-related margins from tab elements.
        /// </summary>
        private void ClearTabMargins()
        {
            try
            {
                int tabCount = ViewModel?.Tabs?.Count ?? 0;
                for (int i = 0; i < tabCount; i++)
                {
                    if (TabRepeater?.TryGetElement(i) is FrameworkElement elem && elem.Margin.Left > 0)
                    {
                        elem.Margin = new Thickness(0);
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// Convert screen X coordinate to a tab insertion index for this window.
        /// </summary>
        private int GetInsertIndexFromScreen(int screenX)
        {
            try
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                if (!Helpers.NativeMethods.GetWindowRect(hwnd, out var windowRect))
                    return ViewModel?.Tabs?.Count ?? 0;

                // Convert screen X to client-relative X (physical pixels)
                int clientX = screenX - windowRect.Left;

                // Convert to DIPs
                double scale = AppTitleBar?.XamlRoot?.RasterizationScale ?? 1.0;
                double dipX = clientX / scale;

                // Get TabRepeater origin in the window
                if (TabRepeater == null) return 0;
                var transform = TabRepeater.TransformToVisual(null);
                var origin = transform.TransformPoint(new Windows.Foundation.Point(0, 0));

                double relativeX = dipX - origin.X;
                if (relativeX < 0) return 0;

                int tabCount = ViewModel?.Tabs?.Count ?? 0;
                int index = (int)(relativeX / _calculatedTabWidth);
                return Math.Clamp(index, 0, tabCount);
            }
            catch
            {
                return ViewModel?.Tabs?.Count ?? 0;
            }
        }

        /// <summary>
        /// Accept a tab from another window and add it to this window's tab bar.
        /// Called by the drag timer when a torn-off window is dropped onto this window's tab bar.
        /// </summary>
        public void DockTab(Models.TabStateDto dto, int insertIndex = -1)
        {
            try
            {
                // Clear ghost indicator before docking
                HideGhostTab();

                // Create a new tab from the DTO
                var root = new FolderItem { Name = "PC", Path = "PC" };
                var fileService = App.Current.Services.GetRequiredService<Services.FileSystemService>();
                var explorer = new ExplorerViewModel(root, fileService);

                var newTab = new TabItem
                {
                    Header = dto.Header,
                    Path = dto.Path,
                    ViewMode = (ViewMode)dto.ViewMode,
                    IconSize = (ViewMode)dto.IconSize,
                    IsActive = false,
                    Explorer = explorer
                };

                // Navigate if path is not empty
                if (!string.IsNullOrEmpty(dto.Path) && (ViewMode)dto.ViewMode != ViewMode.Home)
                {
                    explorer.EnableAutoNavigation = true;
                    _ = explorer.NavigateToPath(dto.Path);
                }

                // Insert at specific position or append
                int tabCount = ViewModel.Tabs.Count;
                if (insertIndex >= 0 && insertIndex < tabCount)
                {
                    ViewModel.Tabs.Insert(insertIndex, newTab);
                }
                else
                {
                    ViewModel.Tabs.Add(newTab);
                    insertIndex = ViewModel.Tabs.Count - 1;
                }

                CreateMillerPanelForTab(newTab);
                SwitchMillerPanel(newTab.Id);
                SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
                SwitchListPanel(newTab.Id, newTab.ViewMode == ViewMode.List);
                SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
                ViewModel.SwitchToTab(insertIndex);
                ResubscribeLeftExplorer();
                UpdateViewModeVisibility();
                FocusActiveView();

                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);

                Helpers.DebugLogger.Log($"[ReDock] Tab '{dto.Header}' docked at index {insertIndex} (total: {ViewModel.Tabs.Count})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ReDock] Error docking tab: {ex.Message}");
            }
        }

        #endregion

        // =================================================================
        //  Workspace
        // =================================================================

        #region Workspace

        private async Task ShowSaveWorkspaceDialogAsync()
        {
            var nameBox = new TextBox
            {
                PlaceholderText = _loc?.Get("Workspace_NamePlaceholder") ?? "Workspace name",
                Width = 300
            };

            var dialog = new ContentDialog
            {
                Title = _loc?.Get("Workspace_SaveTitle") ?? "Save Workspace",
                Content = nameBox,
                PrimaryButtonText = _loc?.Get("Save") ?? "Save",
                CloseButtonText = _loc?.Get("Cancel") ?? "Cancel",
                XamlRoot = Content.XamlRoot,
                DefaultButton = ContentDialogButton.Primary
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            var name = nameBox.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                ViewModel.ShowToast(_loc?.Get("Workspace_NameRequired") ?? "Please enter a name.", 2000, isError: true);
                return;
            }

            var workspaceService = App.Current.Services.GetService<Services.WorkspaceService>();
            if (workspaceService == null) return;

            var tabs = ViewModel.CollectCurrentTabStates();
            var activeIndex = ViewModel.Tabs.IndexOf(ViewModel.ActiveTab);

            var workspace = new WorkspaceDto(
                Id: Guid.NewGuid().ToString(),
                Name: name,
                Tabs: tabs,
                ActiveTabIndex: Math.Max(0, activeIndex),
                CreatedAt: DateTime.UtcNow,
                LastUsedAt: DateTime.UtcNow
            );

            await workspaceService.SaveWorkspaceAsync(workspace);
            ViewModel.ShowToast($"\"{name}\" saved", 2000);
        }

        internal async Task ShowWorkspacePaletteAsync()
        {
            var workspaceService = App.Current.Services.GetService<Services.WorkspaceService>();
            if (workspaceService == null) return;

            var workspaces = await workspaceService.GetWorkspacesAsync();
            var autoSave = await workspaceService.GetAutoSaveAsync();

            var flyout = new MenuFlyout();

            // Title — 아이콘 + 볼드로 헤더 강조
            var titleItem = new MenuFlyoutItem
            {
                Text = _loc?.Get("Workspace_PaletteTitle") ?? "Workspaces",
                Icon = new FontIcon { Glyph = "\uE8F1", FontSize = 14 },
                IsEnabled = true
            };
            // 클릭해도 아무 동작 안 함 (헤더 역할만)
            titleItem.Click += (s, e) => { };
            flyout.Items.Add(titleItem);
            flyout.Items.Add(new MenuFlyoutSeparator());

            // (이전 세션 autosave 기능 제거 — UX 혼란 방지)

            if (workspaces.Count == 0)
            {
                flyout.Items.Add(new MenuFlyoutItem
                {
                    Text = _loc?.Get("Workspace_Empty") ?? "No saved workspaces",
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var ws in workspaces.OrderByDescending(w => w.LastUsedAt))
                {
                    var tabCountText = string.Format(_loc?.Get("Workspace_TabCount") ?? "{0} tabs", ws.Tabs.Count);
                    var subItem = new MenuFlyoutSubItem
                    {
                        Text = $"{ws.Name}  ({tabCountText})",
                        Icon = new FontIcon { Glyph = "\uE838" }
                    };

                    // 복원
                    var restoreItem = new MenuFlyoutItem
                    {
                        Text = _loc?.Get("Workspace_Restore") ?? "Restore",
                        Icon = new FontIcon { Glyph = "\uE777" }
                    };
                    var captured = ws;
                    restoreItem.Click += async (s, e) => await RestoreWorkspaceAsync(captured);
                    subItem.Items.Add(restoreItem);

                    // 이름 변경
                    var renameItem = new MenuFlyoutItem
                    {
                        Text = _loc?.Get("Workspace_Rename") ?? "Rename",
                        Icon = new FontIcon { Glyph = "\uE70F" }
                    };
                    renameItem.Click += async (s, e) =>
                    {
                        var nameBox = new TextBox { Text = captured.Name, Width = 250 };
                        var dlg = new ContentDialog
                        {
                            Title = _loc?.Get("Workspace_Rename") ?? "Rename",
                            Content = nameBox,
                            PrimaryButtonText = _loc?.Get("Save") ?? "Save",
                            CloseButtonText = _loc?.Get("Cancel") ?? "Cancel",
                            XamlRoot = Content.XamlRoot,
                            DefaultButton = ContentDialogButton.Primary
                        };
                        if (await dlg.ShowAsync() == ContentDialogResult.Primary && !string.IsNullOrWhiteSpace(nameBox.Text))
                            await workspaceService.RenameWorkspaceAsync(captured.Id, nameBox.Text.Trim());
                    };
                    subItem.Items.Add(renameItem);

                    // 삭제
                    var deleteItem = new MenuFlyoutItem
                    {
                        Text = _loc?.Get("Workspace_Delete") ?? "Delete",
                        Icon = new FontIcon { Glyph = "\uE74D", Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.IndianRed) }
                    };
                    deleteItem.Click += async (s, e) =>
                    {
                        await workspaceService.DeleteWorkspaceAsync(captured.Id);
                        ViewModel.ShowToast($"\"{captured.Name}\" deleted", 1500);
                    };
                    subItem.Items.Add(deleteItem);

                    flyout.Items.Add(subItem);
                }
            }

            // Add "Save current..." at the bottom
            flyout.Items.Add(new MenuFlyoutSeparator());
            var saveItem = new MenuFlyoutItem
            {
                Text = _loc?.Get("Workspace_Save") ?? "Save tab layout...",
                Icon = new FontIcon { Glyph = "\uE74E" }
            };
            Helpers.CursorHelper.SetHandCursor(saveItem);
            saveItem.Click += async (s, e) => await ShowSaveWorkspaceDialogAsync();
            flyout.Items.Add(saveItem);

            flyout.ShowAt(WorkspaceButton);
        }

        private async Task RestoreWorkspaceAsync(WorkspaceDto workspace)
        {
            try
            {
                var workspaceService = App.Current.Services.GetService<Services.WorkspaceService>();
                if (workspaceService == null) return;

                var dtos = workspace.Tabs;
                if (dtos.Count == 0) return;

                // 1단계: 기존 탭 ID 기억 (나중에 제거용)
                var oldTabIds = ViewModel.Tabs.Select(t => t.Id).ToList();

                // 2단계: 새 탭 추가 (PerformOpenInNewTab 인라인, 뷰모드 MillerColumns 강제)
                var fileService = App.Current.Services.GetRequiredService<Services.FileSystemService>();
                foreach (var dto in dtos)
                {
                    var path = dto.Path ?? "";
                    if (string.IsNullOrEmpty(path)) continue;

                    var root = new FolderItem { Name = "PC", Path = "PC" };
                    var explorer = new ExplorerViewModel(root, fileService);
                    var viewMode = Enum.IsDefined(typeof(ViewMode), dto.ViewMode)
                        ? (ViewMode)dto.ViewMode : ViewMode.MillerColumns;
                    explorer.EnableAutoNavigation = viewMode == ViewMode.Details
                        || viewMode == ViewMode.List
                        || Helpers.ViewModeExtensions.IsIconMode(viewMode);

                    var header = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
                    if (string.IsNullOrEmpty(header)) header = path;

                    var tab = new TabItem
                    {
                        Header = header,
                        Path = path,
                        ViewMode = viewMode,
                        IconSize = ViewMode.IconMedium,
                        Explorer = explorer
                    };
                    ViewModel.Tabs.Add(tab);

                    CreateMillerPanelForTab(tab);
                    if (tab.Explorer is ExplorerViewModel newExpl)
                        newExpl.TabSwitchSuppressionTicks = Environment.TickCount64 + 500;
                    SwitchMillerPanel(tab.Id);
                    SwitchDetailsPanel(tab.Id, viewMode == ViewMode.Details);
                    SwitchListPanel(tab.Id, viewMode == ViewMode.List);
                    SwitchIconPanel(tab.Id, Helpers.ViewModeExtensions.IsIconMode(viewMode));

                    ViewModel.SwitchToTab(ViewModel.Tabs.Count - 1);

                    if (!ViewModel.IsSplitViewEnabled)
                    {
                        SplitterCol.Width = new GridLength(0);
                        RightPaneCol.Width = new GridLength(0);
                        UnsubscribeRightExplorerForAddressBar();
                    }
                    ViewModel.NotifySplitViewChanged();
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    UpdateToolbarButtonStates();
                    SyncAddressBarControls(ViewModel.Explorer);

                    _ = explorer.NavigateToPath(path);
                }

                // 3단계: 이전 탭 제거 (새 탭이 이미 존재하므로 EnsureDefaultTab 트리거 안 됨)
                foreach (var oldId in oldTabIds)
                {
                    var oldTab = ViewModel.Tabs.FirstOrDefault(t => t.Id == oldId);
                    if (oldTab != null)
                    {
                        RemoveMillerPanel(oldTab.Id);
                        RemoveDetailsPanel(oldTab.Id);
                        RemoveListPanel(oldTab.Id);
                        RemoveIconPanel(oldTab.Id);
                        ViewModel.Tabs.Remove(oldTab);
                    }
                }

                // 4단계: 모든 탭 IsActive 초기화 후 활성 탭 설정
                if (ViewModel.Tabs.Count > 0)
                {
                    // 이전 탭 제거로 인덱스가 밀렸으므로 전체 IsActive 리셋
                    foreach (var t in ViewModel.Tabs)
                        t.IsActive = false;

                    var targetIdx = Math.Clamp(workspace.ActiveTabIndex, 0, ViewModel.Tabs.Count - 1);
                    ViewModel.SwitchToTab(targetIdx);
                    var finalTab = ViewModel.ActiveTab;
                    if (finalTab != null)
                    {
                        SwitchMillerPanel(finalTab.Id);
                        SwitchDetailsPanel(finalTab.Id, finalTab.ViewMode == ViewMode.Details);
                        SwitchListPanel(finalTab.Id, finalTab.ViewMode == ViewMode.List);
                        SwitchIconPanel(finalTab.Id, Helpers.ViewModeExtensions.IsIconMode(finalTab.ViewMode));
                    }
                    ResubscribeLeftExplorer();
                    UpdateViewModeVisibility();
                    FocusActiveView();
                }

                // Update last used time
                var updated = workspace with { LastUsedAt = DateTime.UtcNow };
                await workspaceService.SaveWorkspaceAsync(updated);

                ViewModel.ShowToast($"\"{workspace.Name}\"", 1500);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Workspace] Restore failed: {ex.Message}");
                ViewModel.ShowToast("Workspace restore failed", 3000, isError: true);
            }
        }

        #endregion
    }
}
