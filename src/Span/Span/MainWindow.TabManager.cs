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
                    // Record drag start for tear-off detection
                    _tabDragStartPoint = e.GetCurrentPoint(null).Position;
                    _draggingTab = tab;
                    _isTabDragging = false; // Will become true if threshold exceeded

                    // Capture pointer so PointerMoved fires even outside the tab element
                    if (ViewModel.Tabs.Count > 1)
                        fe.CapturePointer(e.Pointer);

                    // Settings 탭은 Miller/Details/Icon 패널 없음
                    if (tab.ViewMode != ViewMode.Settings)
                    {
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
            }

            // Check if cursor is outside the window → tear off
            if (IsCursorOutsideWindow())
            {
                // Don't tear off the last tab
                if (ViewModel.Tabs.Count <= 1) return;

                var tabToTearOff = _draggingTab;
                _draggingTab = null;
                _isTabDragging = false;

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
                }
            }
        }

        /// <summary>
        /// 탭 아이템 PointerReleased 이벤트. 드래그 상태를 초기화한다.
        /// </summary>
        private void OnTabItemPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _draggingTab = null;
            _isTabDragging = false;
            if (sender is UIElement element)
            {
                try { element.ReleasePointerCaptures(); } catch { }
            }
            // Update title bar input regions since tabs may have been reordered
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        /// <summary>
        /// Returns the tab index at the given point (relative to the window).
        /// Tab width is 200px with 1px spacing between tabs.
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

                // Each tab is 200px wide + 1px spacing
                int index = (int)(relativeX / 201);
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

            dragTimer.Tick += (s, e) =>
            {
                if (_isClosed)
                {
                    dragTimer.Stop();
                    return;
                }

                // 1. 마우스 왼쪽 버튼 하드웨어 상태 확인 (메시지 큐와 무관)
                bool mouseDown = (Helpers.NativeMethods.GetAsyncKeyState(
                    Helpers.NativeMethods.VK_LBUTTON) & 0x8000) != 0;

                if (!mouseDown)
                {
                    // 마우스 놓음 → 드래그 종료
                    dragTimer.Stop();

                    // Check for re-docking: is the cursor over another Span window's tab bar?
                    Helpers.NativeMethods.GetCursorPos(out var dropPos);
                    var targetWindow = App.Current.FindWindowAtPoint(dropPos.X, dropPos.Y, this);

                    // Find the new torn-off window by HWND
                    MainWindow? newWindow = null;
                    foreach (var w in ((App)App.Current).GetRegisteredWindows())
                    {
                        if (w is MainWindow mw && WinRT.Interop.WindowNative.GetWindowHandle(mw) == targetHwnd)
                        {
                            newWindow = mw;
                            break;
                        }
                    }

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

                            // Close the new (torn-off) window
                            newWindow._forceClose = true;
                            newWindow._isClosed = true;
                            App.Current.UnregisterWindow(newWindow);
                            newWindow.Close();

                            // Dock the tab into the target window
                            targetWindow.DockTab(dockDto);
                            Helpers.DebugLogger.Log($"[ReDock] Tab '{dockDto.Header}' merged into target window");
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
                (int)Math.Round(rect.Width * scale),
                (int)Math.Round(rect.Height * scale));
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
        /// Accept a tab from another window and add it to this window's tab bar.
        /// Called by the drag timer when a torn-off window is dropped onto this window's tab bar.
        /// </summary>
        public void DockTab(Models.TabStateDto dto)
        {
            try
            {
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

                // Add the tab and switch to it
                ViewModel.Tabs.Add(newTab);
                CreateMillerPanelForTab(newTab);
                SwitchMillerPanel(newTab.Id);
                SwitchDetailsPanel(newTab.Id, newTab.ViewMode == ViewMode.Details);
                SwitchListPanel(newTab.Id, newTab.ViewMode == ViewMode.List);
                SwitchIconPanel(newTab.Id, Helpers.ViewModeExtensions.IsIconMode(newTab.ViewMode));
                ViewModel.SwitchToTab(ViewModel.Tabs.Count - 1);
                ResubscribeLeftExplorer();
                UpdateViewModeVisibility();
                FocusActiveView();

                DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);

                Helpers.DebugLogger.Log($"[ReDock] Tab '{dto.Header}' docked into window (total: {ViewModel.Tabs.Count})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ReDock] Error docking tab: {ex.Message}");
            }
        }

        #endregion
    }
}
