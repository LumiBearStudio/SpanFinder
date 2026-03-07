using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Span.ViewModels
{
    /// <summary>
    /// MainViewModel partial — 탭 생명주기 관리.
    /// 탭 추가/닫기/복제/전환, JSON 세션 저장/복원, 비활성 탭 지연 로딩,
    /// 탭 분리(tear-off) DTO 처리, Settings 탭 관리.
    /// </summary>
    public partial class MainViewModel
    {
        /// <summary>
        /// Localized "Home" label resolved via DI (fallback to "Home" if unavailable).
        /// </summary>
        private string HomeLabel =>
            App.Current.Services.GetService<LocalizationService>()?.Get("Home") ?? "Home";

        /// <summary>
        /// 마지막으로 닫힌 탭의 ViewMode를 기억하여 Home→드라이브 전환 시 복원.
        /// CloseTab에서 설정되며, ResolveViewModeFromHome()에서 소비 후 null로 초기화.
        /// 우선순위가 _viewModeBeforeHome보다 높음 (탭 닫기 시 사용자가 마지막 본 뷰모드).
        /// </summary>
        private ViewMode? _lastClosedViewMode;

        /// <summary>
        /// Home 모드 전환 전의 ViewMode를 기억 (사이드바 Home 클릭 등).
        /// SwitchViewMode(Home)에서 저장되며, 드라이브/즐겨찾기 클릭 시 복원에 사용.
        /// 이 값은 탐색기 뷰모드(Miller/Details/List/Icon)만 저장 — Settings/ActionLog는 제외.
        /// ResolveViewModeFromHome()에서 소비 후 null로 초기화.
        /// </summary>
        private ViewMode? _viewModeBeforeHome;

        /// <summary>
        /// 우측 패인이 Home에서 탐색기로 전환될 때 사용할 뷰모드.
        /// LoadTabsFromSettings()에서 Tab2StartupViewMode로 설정됨.
        /// ResolveViewModeFromHome()에서 우측 패인용으로 사용.
        /// </summary>
        private ViewMode? _rightPreferredViewMode;

        /// <summary>
        /// Home/ActionLog에서 탐색기로 복귀 시 이전 ViewMode 결정.
        /// 우선순위: _lastClosedViewMode > _viewModeBeforeHome > MillerColumns (기본값).
        /// _lastClosedViewMode가 우선인 이유: 탭 닫기→새 Home 탭 생성→드라이브 클릭 흐름에서
        /// 사용자가 닫기 직전에 보던 뷰모드를 유지하기 위함.
        /// 두 필드 모두 한 번 사용 후 null로 초기화 (일회성 소비).
        /// </summary>
        public ViewMode ResolveViewModeFromHome()
        {
            // 우측 패인 Home → 탐색기 전환: _rightPreferredViewMode 우선 사용
            if (IsSplitViewEnabled && ActivePane == ActivePane.Right && _rightPreferredViewMode.HasValue)
            {
                var rMode = _rightPreferredViewMode.Value;
                Helpers.DebugLogger.Log($"[ResolveViewModeFromHome] Right pane → {rMode}");
                return rMode;
            }

            // 좌측 패인: 기존 우선순위
            // _lastClosedViewMode > ActiveTab.PreferredViewMode > _viewModeBeforeHome > MillerColumns
            var preferred = ActiveTab?.PreferredViewMode;
            Helpers.DebugLogger.Log($"[ResolveViewModeFromHome] _lastClosedViewMode={_lastClosedViewMode}, preferred={preferred}, _viewModeBeforeHome={_viewModeBeforeHome}");
            var mode = _lastClosedViewMode ?? preferred ?? _viewModeBeforeHome ?? ViewMode.MillerColumns;
            Helpers.DebugLogger.Log($"[ResolveViewModeFromHome] → resolved={mode}");
            _lastClosedViewMode = null;
            _viewModeBeforeHome = null;
            // PreferredViewMode는 일회성 — 한 번 사용 후 초기화
            if (ActiveTab != null) ActiveTab.PreferredViewMode = null;
            return mode;
        }

        #region Tab Management

        /// <summary>
        /// Add a new Home tab and switch to it.
        /// </summary>
        public void AddNewTab()
        {
            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.Home);

            var tab = new TabItem
            {
                Header = HomeLabel,
                Path = "",
                ViewMode = ViewMode.Home,
                IconSize = ViewMode.IconMedium,
                IsActive = false,
                Explorer = explorer
            };
            Tabs.Add(tab);
            SwitchToTab(Tabs.Count - 1);
            Helpers.DebugLogger.Log($"[MainViewModel] New tab added (total: {Tabs.Count})");
        }

        /// <summary>
        /// Switch to a tab by index. Saves old tab state, restores new tab state.
        /// Minimizes PropertyChanged events: backing fields are set directly,
        /// and the caller (code-behind) is responsible for updating UI manually.
        /// </summary>
        public void SwitchToTab(int index)
        {
            if (index < 0 || index >= Tabs.Count)
                return;
            if (index == ActiveTabIndex && Tabs[index].IsActive)
                return;

            IsSwitchingTab = true;
            try
            {
                // 현재 탭 상태 동기화 (Path, ViewMode만)
                SaveActiveTabState();

                // Deactivate old tab
                if (ActiveTabIndex >= 0 && ActiveTabIndex < Tabs.Count)
                    Tabs[ActiveTabIndex].IsActive = false;

                // Activate new tab — backing field 직접 설정으로 PropertyChanged 방지
                _activeTabIndex = index;
                Tabs[index].IsActive = true;
                OnPropertyChanged(nameof(ActiveTab));

                // 분할뷰 상태 복원 (모든 탭 공통 — Settings/ActionLog는 기본값 false)
                _isSplitViewEnabled = Tabs[index].IsSplitEnabled;
                _rightViewMode = Tabs[index].SplitRightViewMode;

                // Settings/ActionLog 탭은 Explorer가 null — Explorer 바인딩 스킵
                if (Tabs[index].ViewMode == ViewMode.Settings)
                {
                    _currentViewMode = ViewMode.Settings;
                    _leftViewMode = ViewMode.Settings;
                }
                else if (Tabs[index].ViewMode == ViewMode.ActionLog)
                {
                    _currentViewMode = ViewMode.ActionLog;
                    _leftViewMode = ViewMode.ActionLog;
                }
                else
                {
                    // Explorer가 없으면 생성, 있지만 경로가 미로드이면 탐색 실행
                    if (Tabs[index].Explorer == null)
                    {
                        _ = InitializeTabExplorerAsync(Tabs[index]);
                    }
                    else if (!string.IsNullOrEmpty(Tabs[index].Path)
                        && Tabs[index].ViewMode != ViewMode.Home
                        && string.IsNullOrEmpty(Tabs[index].Explorer.CurrentPath))
                    {
                        // H4: 비활성 탭에서 지연된 NavigateToPath 실행
                        _ = LoadDeferredTabPathAsync(Tabs[index]);
                    }

                    // ★ LeftExplorer 필드 직접 설정 — PropertyChanged 미발생 (SetProperty 우회)
                    var old = _leftExplorer;
                    if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
                    _leftExplorer = Tabs[index].Explorer!;
                    if (_leftExplorer != null)
                    {
                        _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;
                        _leftExplorer.EnableAutoNavigation = ShouldAutoNavigate(Tabs[index].ViewMode);
                    }

                    // ★ ViewMode도 backing field 직접 설정 — PropertyChanged 미발생
                    _currentViewMode = Tabs[index].ViewMode;
                    _leftViewMode = Tabs[index].ViewMode;
                    if (Helpers.ViewModeExtensions.IsIconMode(Tabs[index].ViewMode))
                        _currentIconSize = Tabs[index].IconSize;
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Switched to tab {index}: {Tabs[index].Header}");
                UpdateStatusBar();
                SyncNavigationHistoryState();
            }
            finally
            {
                IsSwitchingTab = false;
            }
        }

        /// <summary>
        /// Close a tab by index. If it's the last tab, resets to a new Home tab.
        /// </summary>
        public event EventHandler? LastTabClosed;

        public void CloseTab(int index)
        {
            Helpers.DebugLogger.Log($"[CloseTab] index={index}, Tabs.Count={Tabs.Count}, CurrentViewMode={CurrentViewMode}, _viewModeBeforeHome={_viewModeBeforeHome}, _lastClosedViewMode={_lastClosedViewMode}");
            if (Tabs.Count <= 1)
            {
                // 마지막 탭 닫기 전에 현재 ViewMode 저장 (새로 생성되는 Home 탭에서 드라이브 클릭 시 복원용).
                // Home 상태에서 닫는 경우: Home 전환 전에 저장해둔 _viewModeBeforeHome을 사용
                //   (예: Details 모드 → Home 전환 → 탭 닫기 → 새 탭에서 드라이브 클릭 시 Details 복원)
                // 탐색기 상태에서 닫는 경우: 현재 ViewMode를 그대로 사용
                _lastClosedViewMode = (CurrentViewMode == ViewMode.Home)
                    ? _viewModeBeforeHome
                    : CurrentViewMode;
                Helpers.DebugLogger.Log($"[CloseTab] SAVED _lastClosedViewMode={_lastClosedViewMode}");

                // Last tab — reset to Home tab instead of closing window
                Tabs[0].Explorer?.Cleanup();
                Tabs.RemoveAt(0);
                _activeTabIndex = -1;
                AddNewTab();
                Helpers.DebugLogger.Log("[MainViewModel] Last tab closed — reset to Home");
                return;
            }
            if (index < 0 || index >= Tabs.Count) return;

            bool wasActive = (index == ActiveTabIndex);
            // 닫히는 탭의 Explorer 정리
            Tabs[index].Explorer?.Cleanup();
            Tabs.RemoveAt(index);

            if (wasActive)
            {
                // Switch to closest valid tab
                int newIndex = Math.Min(index, Tabs.Count - 1);
                ActiveTabIndex = -1; // Force switch
                SwitchToTab(newIndex);
            }
            else if (index < ActiveTabIndex)
            {
                // Active tab shifted left
                ActiveTabIndex--;
                OnPropertyChanged(nameof(ActiveTab));
            }

            Helpers.DebugLogger.Log($"[MainViewModel] Closed tab {index} (remaining: {Tabs.Count})");
        }

        /// <summary>
        /// Close all tabs except the specified one.
        /// Returns list of closed tab IDs so the caller can clean up panels.
        /// </summary>
        public List<string> CloseOtherTabs(TabItem keepTab)
        {
            var closedIds = new List<string>();
            // Close from right to left to maintain indices
            for (int i = Tabs.Count - 1; i >= 0; i--)
            {
                if (Tabs[i] == keepTab) continue;
                closedIds.Add(Tabs[i].Id);
                Tabs[i].Explorer?.Cleanup();
                Tabs.RemoveAt(i);
            }

            int newIndex = Tabs.IndexOf(keepTab);
            ActiveTabIndex = -1; // Force switch
            SwitchToTab(newIndex);
            Helpers.DebugLogger.Log($"[MainViewModel] Closed other tabs, remaining: {Tabs.Count}");
            return closedIds;
        }

        /// <summary>
        /// Close all tabs to the right of the specified tab.
        /// Returns list of closed tab IDs so the caller can clean up panels.
        /// </summary>
        public List<string> CloseTabsToRight(TabItem tab)
        {
            int tabIndex = Tabs.IndexOf(tab);
            if (tabIndex < 0) return new List<string>();

            var closedIds = new List<string>();
            for (int i = Tabs.Count - 1; i > tabIndex; i--)
            {
                closedIds.Add(Tabs[i].Id);
                Tabs[i].Explorer?.Cleanup();
                Tabs.RemoveAt(i);
            }

            // If active tab was removed, switch to the kept tab
            if (ActiveTabIndex > tabIndex)
            {
                ActiveTabIndex = -1;
                SwitchToTab(tabIndex);
            }

            Helpers.DebugLogger.Log($"[MainViewModel] Closed tabs to right of {tabIndex}, remaining: {Tabs.Count}");
            return closedIds;
        }

        /// <summary>
        /// Duplicate a tab: create a new tab with the same path, view mode, and icon size.
        /// Insert it right after the source tab.
        /// </summary>
        public TabItem DuplicateTab(TabItem sourceTab)
        {
            SaveActiveTabState();

            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(sourceTab.ViewMode);

            var newTab = new TabItem
            {
                Header = sourceTab.Header,
                Path = sourceTab.Path,
                ViewMode = sourceTab.ViewMode,
                IconSize = sourceTab.IconSize,
                IsActive = false,
                Explorer = explorer
            };

            int insertIndex = Tabs.IndexOf(sourceTab) + 1;
            Tabs.Insert(insertIndex, newTab);

            // Navigate the new explorer to the source path
            if (!string.IsNullOrEmpty(sourceTab.Path) && sourceTab.ViewMode != ViewMode.Home)
            {
                _ = explorer.NavigateToPath(sourceTab.Path);
            }

            SwitchToTab(insertIndex);
            Helpers.DebugLogger.Log($"[MainViewModel] Duplicated tab '{sourceTab.Header}' at index {insertIndex}");
            return newTab;
        }

        /// <summary>
        /// Copy current explorer state into the active tab.
        /// </summary>
        public void SaveActiveTabState()
        {
            var tab = ActiveTab;
            if (tab == null) return;
            if (tab.ViewMode == ViewMode.Settings || tab.ViewMode == ViewMode.ActionLog) return; // 특수 탭은 상태 저장 불필요

            if (tab.ViewMode != CurrentViewMode)
                tab.ViewMode = CurrentViewMode;
            if (tab.IconSize != CurrentIconSize)
                tab.IconSize = CurrentIconSize;
            tab.Path = tab.Explorer?.CurrentPath ?? "";

            // 분할뷰 상태 저장
            tab.IsSplitEnabled = IsSplitViewEnabled;
            tab.SplitRightViewMode = RightViewMode;

            // Header도 동기화 (Home 모드 전환 후 저장 시 Header 불일치 방지)
            if (CurrentViewMode == ViewMode.Home)
                tab.Header = HomeLabel;
            else
                tab.Header = tab.Explorer?.CurrentFolderName ?? HomeLabel;
        }

        /// <summary>
        /// 탭에 ExplorerViewModel을 최초 생성 (앱 시작/세션 복원 시).
        /// 이미 Explorer가 있으면 아무것도 하지 않음.
        /// </summary>
        private async Task InitializeTabExplorerAsync(TabItem tab)
        {
            if (tab.Explorer != null) return;

            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
            tab.Explorer = explorer;

            if (!string.IsNullOrEmpty(tab.Path) && tab.ViewMode != ViewMode.Home)
            {
                try
                {
                    if (System.IO.Directory.Exists(tab.Path))
                    {
                        await explorer.NavigateToPath(tab.Path);
                    }
                    else
                    {
                        tab.Path = "";
                        tab.ViewMode = ViewMode.Home;
                        Helpers.DebugLogger.Log($"[MainViewModel] Tab path not found, falling back to Home");
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[MainViewModel] InitializeTabExplorer error: {ex.Message}");
                    tab.Path = "";
                    tab.ViewMode = ViewMode.Home;
                }
            }
        }

        /// <summary>
        /// H4: 비활성 탭의 지연된 NavigateToPath 실행 (최초 전환 시)
        /// </summary>
        private async Task LoadDeferredTabPathAsync(TabItem tab)
        {
            if (tab.Explorer == null || string.IsNullOrEmpty(tab.Path)) return;

            try
            {
                if (System.IO.Directory.Exists(tab.Path))
                {
                    await tab.Explorer.NavigateToPath(tab.Path);
                }
                else
                {
                    tab.Path = "";
                    tab.ViewMode = ViewMode.Home;
                    Helpers.DebugLogger.Log($"[MainViewModel] Deferred tab path not found, falling back to Home");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] LoadDeferredTabPath error: {ex.Message}");
                tab.Path = "";
                tab.ViewMode = ViewMode.Home;
            }
        }

        /// <summary>
        /// SwitchToTab에서 PropertyChanged를 우회한 후, XAML x:Bind가 필요로 하는
        /// 최소한의 PropertyChanged만 일괄 발생시킨다.
        /// code-behind에서 ResubscribeLeftExplorer() 호출 후 사용.
        /// Explorer/ActiveExplorer는 ResubscribeLeftExplorer가 이미 처리하므로 제외.
        /// </summary>
        public void NotifyViewModeChanged()
        {
            // LeftViewMode는 XAML x:Bind에서 사용하지 않으므로 제거 (불필요한 바인딩 평가 방지)
            OnPropertyChanged(nameof(CurrentViewMode));
        }

        /// <summary>
        /// Sync the active tab's header/icon with the current explorer state.
        /// </summary>
        public void UpdateActiveTabHeader()
        {
            var tab = ActiveTab;
            if (tab == null) return;
            if (tab.ViewMode == ViewMode.Settings || tab.ViewMode == ViewMode.ActionLog) return; // 특수 탭 헤더 보호

            if (CurrentViewMode == ViewMode.Home)
            {
                tab.Header = HomeLabel;
                tab.ViewMode = ViewMode.Home;
            }
            else
            {
                tab.Header = tab.Explorer?.CurrentFolderName ?? HomeLabel;
                tab.ViewMode = CurrentViewMode;
            }
        }

        /// <summary>
        /// Save all tab states to settings (JSON persistence).
        /// </summary>
        public void SaveTabsToSettings()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                var dtos = Tabs
                    .Where(t => t.ViewMode != ViewMode.Settings && t.ViewMode != ViewMode.ActionLog) // 특수 탭은 세션 저장 제외
                    .Select(t => new TabStateDto(
                        t.Id, t.Header, t.Path, (int)t.ViewMode, (int)t.IconSize
                    )).ToList();

                settings.TabsJson = JsonSerializer.Serialize(dtos);
                settings.ActiveTabIndex = ActiveTabIndex;
                Helpers.DebugLogger.Log($"[MainViewModel] Saved {dtos.Count} tabs to settings");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving tabs: {ex.Message}");
            }
        }

        /// <summary>
        /// Load tab states from settings. Replaces current tabs.
        /// Uses per-tab startup settings (Tab1StartupBehavior, Tab2StartupBehavior).
        /// Behavior per tab: 0=Home, 1=RestoreSession, 2=CustomPath.
        /// </summary>
        public void LoadTabsFromSettings()
        {
            try
            {
                var settings = App.Current.Services.GetRequiredService<SettingsService>();
                var tab1Behavior = settings.Tab1StartupBehavior;
                var tab2Behavior = settings.Tab2StartupBehavior;

                // Load saved session data for RestoreSession mode
                List<TabStateDto>? savedDtos = null;
                var json = settings.TabsJson;
                if (!string.IsNullOrEmpty(json))
                {
                    try { savedDtos = JsonSerializer.Deserialize<List<TabStateDto>>(json); }
                    catch { savedDtos = null; }
                }

                Tabs.Clear();

                // Create Tab 1
                var tab1 = CreateStartupTab(tab1Behavior, settings.Tab1StartupPath,
                    settings.Tab1StartupViewMode, savedDtos, 0);
                tab1.Explorer ??= CreateDefaultExplorer(tab1.ViewMode);

                // Assign LeftExplorer for Tab 1
                var oldLeft = _leftExplorer;
                if (oldLeft != null) oldLeft.PropertyChanged -= OnLeftExplorerPropertyChanged;
                _leftExplorer = tab1.Explorer;
                _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;

                Tabs.Add(tab1);

                // Tab 2 설정은 Split View 우측 패인 전용 — 탭 바에 추가하지 않음.
                // RightViewMode와 RightExplorer만 아래에서 설정.

                _activeTabIndex = 0;
                tab1.IsActive = true;
                _currentViewMode = tab1.ViewMode;
                _leftViewMode = tab1.ViewMode;
                if (Helpers.ViewModeExtensions.IsIconMode(tab1.ViewMode))
                    _currentIconSize = tab1.IconSize;

                // Tab 1 AutoNavigation을 뷰모드에 맞게 설정
                if (tab1.Explorer != null)
                    tab1.Explorer.EnableAutoNavigation = ShouldAutoNavigate(tab1.ViewMode);

                // Tab 2 / Split view 우측 뷰모드: 시작 설정 적용
                {
                    var tab2VM = settings.Tab2StartupViewMode switch
                    {
                        1 => ViewMode.Details,
                        2 => ViewMode.List,
                        3 => ViewMode.IconMedium,
                        _ => ViewMode.MillerColumns
                    };

                    // behavior=0(Home): 홈 화면으로 시작, 드라이브 클릭 시 tab2VM으로 전환
                    if (tab2Behavior == 0)
                    {
                        RightViewMode = ViewMode.Home;
                        _rightPreferredViewMode = tab2VM;
                    }
                    else
                    {
                        RightViewMode = tab2VM;
                        _rightPreferredViewMode = null;
                    }
                    RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(tab2VM);
                    Helpers.DebugLogger.Log($"[LoadTabsFromSettings] Tab2: behavior={tab2Behavior}, viewMode={tab2VM}, rightVM={RightViewMode}, path={settings.Tab2StartupPath}");
                }

                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(Explorer));
                OnPropertyChanged(nameof(ActiveExplorer));
                OnPropertyChanged(nameof(CurrentViewMode));

                // Navigate custom path tabs (deferred)
                // 모든 탭에 LoadDeferredTabPathAsync 사용 — CreateStartupTab이
                // Explorer를 미리 생성하므로 InitializeTabExplorerAsync는 early return됨
                for (int i = 0; i < Tabs.Count; i++)
                {
                    var tab = Tabs[i];
                    if (!string.IsNullOrEmpty(tab.Path) && tab.ViewMode != ViewMode.Home)
                    {
                        _ = LoadDeferredTabPathAsync(tab);
                    }
                }

                for (int t = 0; t < Tabs.Count; t++)
                    Helpers.DebugLogger.Log($"[LoadTabsFromSettings] Tab[{t}]: id={Tabs[t].Id}, header='{Tabs[t].Header}', viewMode={Tabs[t].ViewMode}, preferred={Tabs[t].PreferredViewMode}, path='{Tabs[t].Path}'");
                Helpers.DebugLogger.Log($"[MainViewModel] LoadTabsFromSettings: {Tabs.Count} tabs created (tab1={tab1Behavior}, tab2={tab2Behavior})");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading tabs: {ex.Message}");
                EnsureDefaultTab();
                Tabs[0].Explorer = LeftExplorer;
            }
        }

        /// <summary>
        /// Create a tab based on startup behavior.
        /// 0=Home, 1=RestoreSession, 2=CustomPath.
        /// </summary>
        private TabItem CreateStartupTab(int behavior, string customPath, int viewModeInt,
            List<TabStateDto>? savedDtos, int tabIndex)
        {
            // Resolve view mode from setting (0=Miller, 1=Details, 2=List, 3=Icon)
            var startupViewMode = viewModeInt switch
            {
                1 => ViewMode.Details,
                2 => ViewMode.List,
                3 => ViewMode.IconMedium,
                _ => ViewMode.MillerColumns
            };

            switch (behavior)
            {
                case 1: // Restore session — 경로는 세션에서, 뷰모드는 시작 설정에서
                    if (savedDtos != null && tabIndex < savedDtos.Count)
                    {
                        var dto = savedDtos[tabIndex];
                        var tabIconSize = System.Enum.IsDefined(typeof(ViewMode), dto.IconSize)
                            ? (ViewMode)dto.IconSize : ViewMode.IconMedium;

                        var root = new FolderItem { Name = "PC", Path = "PC" };
                        var explorer = new ExplorerViewModel(root, _fileService);
                        explorer.EnableAutoNavigation = ShouldAutoNavigate(startupViewMode);

                        return new TabItem
                        {
                            Id = dto.Id,
                            Header = startupViewMode == ViewMode.Home ? HomeLabel : dto.Header,
                            Path = dto.Path,
                            ViewMode = startupViewMode,
                            IconSize = tabIconSize,
                            IsActive = false,
                            Explorer = explorer
                        };
                    }
                    // Fallback to Home if no saved session for this tab
                    goto case 0;

                case 2: // Custom path
                    if (!string.IsNullOrEmpty(customPath) && System.IO.Directory.Exists(customPath))
                    {
                        var folderName = System.IO.Path.GetFileName(customPath.TrimEnd('\\'));
                        if (string.IsNullOrEmpty(folderName)) folderName = customPath;

                        var root2 = new FolderItem { Name = "PC", Path = "PC" };
                        var explorer2 = new ExplorerViewModel(root2, _fileService);
                        explorer2.EnableAutoNavigation = ShouldAutoNavigate(startupViewMode);

                        return new TabItem
                        {
                            Header = folderName,
                            Path = customPath,
                            ViewMode = startupViewMode,
                            IconSize = ViewMode.IconMedium,
                            IsActive = false,
                            Explorer = explorer2
                        };
                    }
                    // Fallback to Home if path invalid
                    goto case 0;

                case 0: // Home
                default:
                {
                    var root0 = new FolderItem { Name = "PC", Path = "PC" };
                    var explorer0 = new ExplorerViewModel(root0, _fileService);
                    explorer0.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.Home);

                    return new TabItem
                    {
                        Header = HomeLabel,
                        Path = "",
                        ViewMode = ViewMode.Home,
                        IconSize = ViewMode.IconMedium,
                        IsActive = false,
                        Explorer = explorer0,
                        // 시작 뷰모드를 저장 — 홈에서 드라이브 클릭 시 이 뷰모드로 전환
                        PreferredViewMode = startupViewMode != ViewMode.MillerColumns ? startupViewMode : null
                    };
                }
            }
        }

        /// <summary>
        /// Create a default ExplorerViewModel for a given view mode.
        /// </summary>
        private ExplorerViewModel CreateDefaultExplorer(ViewMode mode)
        {
            var root = new FolderItem { Name = "PC", Path = "PC" };
            var explorer = new ExplorerViewModel(root, _fileService);
            explorer.EnableAutoNavigation = ShouldAutoNavigate(mode);
            return explorer;
        }

        /// <summary>
        /// Load a single tab from a tear-off DTO. Replaces all existing tabs.
        /// Used when creating a new window from a torn-off tab.
        /// </summary>
        public async Task LoadSingleTabFromDtoAsync(TabStateDto dto)
        {
            try
            {
                Tabs.Clear();

                var tearViewMode = System.Enum.IsDefined(typeof(ViewMode), dto.ViewMode)
                    ? (ViewMode)dto.ViewMode : ViewMode.MillerColumns;
                var tearIconSize = System.Enum.IsDefined(typeof(ViewMode), dto.IconSize)
                    ? (ViewMode)dto.IconSize : ViewMode.IconMedium;

                var tab = new TabItem
                {
                    Id = dto.Id,
                    Header = dto.Header,
                    Path = dto.Path,
                    ViewMode = tearViewMode,
                    IconSize = tearIconSize,
                    IsActive = true
                };

                // Create explorer and assign
                var root = new FolderItem { Name = "PC", Path = "PC" };
                var explorer = new ExplorerViewModel(root, _fileService);
                explorer.EnableAutoNavigation = ShouldAutoNavigate(tab.ViewMode);
                tab.Explorer = explorer;

                Tabs.Add(tab);

                // Set LeftExplorer directly
                var old = _leftExplorer;
                if (old != null) old.PropertyChanged -= OnLeftExplorerPropertyChanged;
                _leftExplorer = explorer;
                _leftExplorer.PropertyChanged += OnLeftExplorerPropertyChanged;

                _activeTabIndex = 0;
                _currentViewMode = tab.ViewMode;
                _leftViewMode = tab.ViewMode;
                if (Helpers.ViewModeExtensions.IsIconMode(tab.ViewMode))
                    _currentIconSize = tab.IconSize;

                OnPropertyChanged(nameof(ActiveTab));
                OnPropertyChanged(nameof(Explorer));
                OnPropertyChanged(nameof(ActiveExplorer));
                OnPropertyChanged(nameof(CurrentViewMode));

                // Navigate to path if not Home
                if (tab.ViewMode != ViewMode.Home && !string.IsNullOrEmpty(tab.Path))
                {
                    await explorer.NavigateToPath(tab.Path);
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Loaded tear-off tab: {tab.Header} @ {tab.Path}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error loading tear-off tab: {ex.Message}");
                EnsureDefaultTab();
                Tabs[0].Explorer = LeftExplorer;
            }
        }

        /// <summary>
        /// Get the path to navigate the right pane to when split view is activated.
        /// Tries: saved right pane path → first available drive → user profile folder.
        /// </summary>
        public string GetRightPaneInitialPath()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue("RightPanePath", out var savedPath) && savedPath is string path)
                {
                    if (System.IO.Directory.Exists(path))
                        return path;
                }
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[MainViewModel] RightPanePath load failed: {ex.Message}"); }

            // Fallback: first available drive
            if (Drives.Count > 0)
            {
                return Drives[0].Path;
            }

            // Last resort: user profile
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        #endregion
    }
}
