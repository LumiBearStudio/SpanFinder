using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Windowing;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Span
{
    /// <summary>
    /// MainWindow의 키보드 단축키 처리 부분 클래스.
    /// 글로벌 키보드 이벤트(Ctrl 조합, F 키, 방향키 등)를 가로채어
    /// 탐색, 파일 작업, 탭 관리, 뷰 모드 전환, 설정 열기, 터미널 실행 등
    /// 다양한 단축키 동작을 실행한다.
    /// Miller Column 내 키보드 탐색과 타입 어헤드 검색도 포함한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 컨텍스트 메뉴(MenuFlyout)가 열려 있는지 확인.
        /// 열려 있으면 키보드 이벤트를 가로채지 않아야 AccessKey가 동작함.
        /// </summary>
        private bool IsContextMenuOpen()
        {
            try
            {
                var popups = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(Content.XamlRoot);
                foreach (var popup in popups)
                {
                    if (popup.Child is Microsoft.UI.Xaml.Controls.FlyoutPresenter
                        or Microsoft.UI.Xaml.Controls.MenuFlyoutPresenter)
                        return true;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[KeyboardHandler] IsContextMenuOpen error: {ex.Message}");
            }
            return false;
        }

        #region Global Keyboard Shortcuts (OnGlobalKeyDown)

        /// <summary>
        /// 윈도우 전체 글로벌 키보드 이벤트 핸들러.
        /// Ctrl/Shift/Alt 조합에 따라 파일 작업, 탐색, 탭 관리, 뷰 모드 전환,
        /// 설정 열기, 터미널 실행, 검색 등 다양한 단축키를 실행한다.
        /// </summary>
        private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 컨텍스트 메뉴 열려 있으면: 수식키 없는 단독 문자 키 → AccessKey 동작 실행
            if (IsContextMenuOpen())
            {
                string? key = null;
                if (e.Key >= Windows.System.VirtualKey.A && e.Key <= Windows.System.VirtualKey.Z)
                    key = e.Key.ToString();
                else if (e.Key >= Windows.System.VirtualKey.Number0 && e.Key <= Windows.System.VirtualKey.Number9)
                    key = ((int)e.Key - (int)Windows.System.VirtualKey.Number0).ToString();

                if (key != null)
                {
                    var ctrlHeld = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                                   .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
                    if (!ctrlHeld && _contextMenuService.TryInvokeAccessKey(key))
                    {
                        e.Handled = true;
                    }
                }
                return;
            }

            // 이름 변경 중이면 F2(선택 영역 순환)만 허용, 나머지 글로벌 단축키 무시
            var selected = GetCurrentSelected();
            if (selected != null && selected.IsRenaming && e.Key != Windows.System.VirtualKey.F2) return;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

            // F11: Toggle fullscreen (window-level, works in all modes)
            if (e.Key == Windows.System.VirtualKey.F11 && !ctrl && !shift && !alt)
            {
                ToggleFullScreen();
                e.Handled = true;
                return;
            }

            // Help 오버레이: 열려있으면 Esc/아무 키로 닫기
            if (_isHelpOpen)
            {
                _isHelpOpen = false;
                HelpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
                return;
            }

            // F1 또는 Shift+? (OEM_2 = /) — Help 오버레이 토글 (어디서든 동작)
            if (e.Key == Windows.System.VirtualKey.F1 ||
                (shift && !ctrl && !alt && e.Key == (Windows.System.VirtualKey)191)) // VK_OEM_2 = /? key
            {
                ToggleHelpOverlay();
                e.Handled = true;
                return;
            }

            // ESC: Quick Look 창이 열려있으면 닫기 (어디서든 동작)
            if (e.Key == Windows.System.VirtualKey.Escape && _quickLookWindow != null)
            {
                CloseQuickLookWindow();
                e.Handled = true;
                return;
            }

            // Settings/Home/ActionLog 모드: 파일 조작 단축키 차단, 뷰 전환/탭/Escape만 허용
            if (ViewModel.CurrentViewMode == ViewMode.Settings || ViewModel.CurrentViewMode == ViewMode.Home || ViewModel.CurrentViewMode == ViewMode.ActionLog)
            {
                if (e.Key == Windows.System.VirtualKey.Escape && ViewModel.CurrentViewMode == ViewMode.Settings)
                {
                    CloseCurrentSettingsTab();
                    e.Handled = true;
                    return;
                }
                if (e.Key == Windows.System.VirtualKey.Escape && ViewModel.CurrentViewMode == ViewMode.ActionLog)
                {
                    CloseCurrentActionLogTab();
                    e.Handled = true;
                    return;
                }
                if (ctrl)
                {
                    switch (e.Key)
                    {
                        case Windows.System.VirtualKey.Number1: // Ctrl+1: Miller
                        case Windows.System.VirtualKey.Number2: // Ctrl+2: Details
                        case Windows.System.VirtualKey.Number3: // Ctrl+3: List
                        case Windows.System.VirtualKey.Number4: // Ctrl+4: Icons
                        case (Windows.System.VirtualKey)188:    // Ctrl+,: Settings
                        case (Windows.System.VirtualKey)192:    // Ctrl+`: Terminal (VK_OEM_3)
                        case (Windows.System.VirtualKey)222:    // Ctrl+': Terminal (VK_OEM_7)
                        case Windows.System.VirtualKey.T:       // Ctrl+T: New Tab
                        case Windows.System.VirtualKey.W:       // Ctrl+W: Close Tab
                        case Windows.System.VirtualKey.L:       // Ctrl+L: Address Bar
                        case Windows.System.VirtualKey.H:       // Ctrl+H: Toggle Hidden
                        case Windows.System.VirtualKey.N:       // Ctrl+N: New Window
                            break; // 허용 — fall through to main handler
                        default:
                            // 한국어 키보드: backtick(41), single quote(40), comma(51) 허용
                            if (e.KeyStatus.ScanCode == 41 || e.KeyStatus.ScanCode == 40 || e.KeyStatus.ScanCode == 51) break;
                            return; // 그 외 Ctrl 단축키 차단
                    }
                }
                else if (!alt)
                {
                    return; // Ctrl/Alt 없는 키(Delete, F2, F5 등) 차단
                }
                // Alt 키 조합(Alt+Left/Right 등)은 허용
            }

            // Escape: 잘라내기 반투명 효과 해제 (클립보드 초기화)
            if (e.Key == Windows.System.VirtualKey.Escape && !ctrl && !shift && !alt)
            {
                if (_cutItems.Count > 0)
                {
                    ClearCutState();
                    _clipboardPaths.Clear();
                    _isCutOperation = false;
                    UpdateToolbarButtonStates();
                    e.Handled = true;
                    return;
                }
            }

            // Alt+Left/Right: Back/Forward navigation (highest priority)
            // Alt+Enter: Show Properties dialog
            // Alt+D: Address bar focus (Explorer 호환)
            if (alt && !ctrl && !shift)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Left:
                        _ = GoBackAndFocusAsync();
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Right:
                        _ = GoForwardAndFocusAsync();
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Up:
                        // Alt+Up: Navigate to parent directory (Explorer/Finder 표준)
                        ViewModel.ActiveExplorer?.NavigateUp();
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Enter:
                        HandleShowProperties();
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.D:
                        // Alt+D: Address bar focus (Explorer 호환 — Ctrl+L과 동일)
                        if (ViewModel.CurrentViewMode != ViewMode.Home)
                        {
                            ShowAddressBarEditMode();
                        }
                        e.Handled = true;
                        return;
                }
            }

            if (ctrl)
            {
                Helpers.DebugLogger.Log($"[Keyboard] Ctrl+Key: Key={(int)e.Key} ({e.Key}), OriginalKey={(int)e.OriginalKey} ({e.OriginalKey}), ScanCode={e.KeyStatus.ScanCode}");

                // TextBox/RichEditBox에 포커스가 있으면 텍스트 편집 단축키(C/X/V/A)를 네이티브 처리에 위임
                if (e.Key is Windows.System.VirtualKey.C or Windows.System.VirtualKey.X
                    or Windows.System.VirtualKey.V or Windows.System.VirtualKey.A)
                {
                    var focused = FocusManager.GetFocusedElement(this.Content.XamlRoot);
                    if (focused is TextBox || focused is RichEditBox || focused is PasswordBox)
                        return; // 텍스트 컨트롤의 네이티브 Ctrl+C/X/V/A 동작 유지
                }

                switch (e.Key)
                {
                    case Windows.System.VirtualKey.E:
                        if (shift)
                        {
                            ToggleSplitView();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.P:
                        if (shift)
                        {
                            TogglePreviewPanel();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.Tab:
                        // Ctrl+Tab: switch between panes
                        if (ViewModel.IsSplitViewEnabled)
                        {
                            ViewModel.ActivePane = ViewModel.ActivePane == ActivePane.Left
                                ? ActivePane.Right : ActivePane.Left;
                            FocusActivePane();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.Enter:
                        // Ctrl+Enter: 선택된 폴더를 새 탭으로 열기
                        {
                            var openInTabItems = GetCurrentSelectedItems();
                            foreach (var item in openInTabItems)
                            {
                                if (item is ViewModels.FolderViewModel folder
                                    && !string.IsNullOrEmpty(folder.Path) && folder.Name != "..")
                                {
                                    ((Services.IContextMenuHost)this).PerformOpenInNewTab(folder.Path);
                                }
                            }
                            if (openInTabItems.Count > 0) e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.T:
                        ViewModel.AddNewTab();
                        if (ViewModel.ActiveTab != null)
                        {
                            CreateMillerPanelForTab(ViewModel.ActiveTab);
                            SwitchMillerPanel(ViewModel.ActiveTab.Id);
                        }
                        ResubscribeLeftExplorer();
                        UpdateViewModeVisibility();
                        FocusActiveView();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.W:
                        if (ViewModel.ActiveTab?.ViewMode == ViewMode.Settings)
                        {
                            CloseCurrentSettingsTab();
                        }
                        else if (ViewModel.ActiveTab?.ViewMode == ViewMode.ActionLog)
                        {
                            CloseCurrentActionLogTab();
                        }
                        else
                        {
                            var closingTab = ViewModel.ActiveTab;
                            if (closingTab != null) RemoveMillerPanel(closingTab.Id);
                            ViewModel.CloseTab(ViewModel.ActiveTabIndex);
                            if (ViewModel.ActiveTab != null)
                                SwitchMillerPanel(ViewModel.ActiveTab.Id);
                            ResubscribeLeftExplorer();
                            UpdateViewModeVisibility();
                            FocusActiveView();
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.L:
                        // Home 모드에서도 Ctrl+L 허용: MillerColumns로 전환 후 주소바 편집
                        if (ViewModel.CurrentViewMode == ViewMode.Home)
                        {
                            ViewModel.SwitchViewMode(ViewMode.MillerColumns);
                            UpdateViewModeVisibility();
                        }
                        ShowAddressBarEditMode();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F:
                        if (shift)
                        {
                            ToggleFilterBar();  // Ctrl+Shift+F → 필터 바
                        }
                        else
                        {
                            SearchBox.Focus(FocusState.Keyboard);  // Ctrl+F → 검색
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.C:
                        HandleCopy();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.X:
                        HandleCut();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.V:
                        if (shift)
                        {
                            // Ctrl+Shift+V: 바로가기로 붙여넣기
                            HandlePasteAsShortcut();
                        }
                        else
                        {
                            HandlePaste();
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.N:
                        if (shift)
                        {
                            HandleNewFolder();
                            e.Handled = true;
                        }
                        else
                        {
                            OpenNewWindow();
                            e.Handled = true;
                        }
                        break;

                    case Windows.System.VirtualKey.A:
                        if (shift)
                        {
                            // Ctrl+Shift+A: Select None
                            HandleSelectNone();
                        }
                        else
                        {
                            HandleSelectAll();
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.H:
                        // Ctrl+H: Toggle hidden files visibility
                        {
                            var settingsSvc = App.Current.Services.GetService<Services.ISettingsService>();
                            if (settingsSvc != null)
                            {
                                settingsSvc.ShowHiddenFiles = !settingsSvc.ShowHiddenFiles;
                                ViewModel.ShowToast(settingsSvc.ShowHiddenFiles ? _loc.Get("Toast_HiddenFilesShown") : _loc.Get("Toast_HiddenFilesHidden"));
                            }
                        }
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.I:
                        // Ctrl+I: Invert Selection
                        HandleInvertSelection();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.D:
                        // Ctrl+D: Duplicate selected file/folder
                        HandleDuplicateFile();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Z:
                        // Undo
                        _ = ViewModel.UndoCommand.ExecuteAsync(null);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Y:
                        // Redo
                        _ = ViewModel.RedoCommand.ExecuteAsync(null);
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)192: // VK_OEM_3 = Ctrl+` (backtick)
                    case (Windows.System.VirtualKey)222: // VK_OEM_7 = Ctrl+' (single quote)
                        // Ctrl+` or Ctrl+': Open terminal
                        HandleOpenTerminal();
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)188: // VK_OEM_COMMA
                        // Ctrl+,: Settings (별도 탭으로 열기)
                        OpenSettingsTab();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number1:
                        // Ctrl+1: Miller Columns
                        ViewModel.SwitchViewMode(Models.ViewMode.MillerColumns);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number2:
                        // Ctrl+2: Details
                        ViewModel.SwitchViewMode(Models.ViewMode.Details);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number3:
                        // Ctrl+3: List
                        ViewModel.SwitchViewMode(Models.ViewMode.List);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Number4:
                        // Ctrl+4: Icon (마지막 Icon 크기)
                        ViewModel.SwitchViewMode(ViewModel.CurrentIconSize);
                        GetActiveIconView()?.UpdateIconSize(ViewModel.CurrentIconSize);
                        e.Handled = true;
                        break;

                    case (Windows.System.VirtualKey)187: // VK_OEM_PLUS = =/+ key
                        if (shift)
                        {
                            // Ctrl+Shift+=: Equalize all columns to the same width (220 default)
                            if (ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns)
                            {
                                ApplyWidthToAllColumns(ColumnWidth);
                                var eqCtl = GetActiveMillerColumnsControl();
                                eqCtl.InvalidateMeasure();
                                eqCtl.UpdateLayout();
                                GetActiveMillerScrollViewer().InvalidateMeasure();
                                ViewModel.ShowToast(_loc.Get("Toast_ColumnsEqualized"));
                            }
                            e.Handled = true;
                        }
                        break;

                    case (Windows.System.VirtualKey)189: // VK_OEM_MINUS = -/_ key
                        if (shift)
                        {
                            // Ctrl+Shift+-: Auto-fit all columns to their content
                            if (ViewModel.CurrentViewMode == Models.ViewMode.MillerColumns)
                            {
                                AutoFitAllColumns();
                                ViewModel.ShowToast(_loc.Get("Toast_ColumnsAutoFit"));
                            }
                            e.Handled = true;
                        }
                        break;

                    default:
                        // 한국어 키보드 대응: VK_OEM 코드가 다른 VirtualKey로 매핑될 수 있음
                        // 물리 키 scan code로 판별
                        if (e.KeyStatus.ScanCode == 41 || e.KeyStatus.ScanCode == 40) // backtick(41) or single quote(40)
                        {
                            HandleOpenTerminal();
                            e.Handled = true;
                        }
                        else if (e.KeyStatus.ScanCode == 51) // comma 위치
                        {
                            OpenSettingsTab();
                            e.Handled = true;
                        }
                        break;
                }
            }
            else if (shift)
            {
                // Shift without Ctrl
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Delete:
                        HandlePermanentDelete();
                        e.Handled = true;
                        break;
                }
            }
            else
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.F5:
                        HandleRefresh();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F2:
                        HandleRename();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F3:
                        // F3: Focus search box (Explorer 표준 단축키)
                        SearchBox.Focus(FocusState.Keyboard);
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F4:
                        // F4: Address bar edit mode (Explorer 전통 단축키)
                        if (ViewModel.CurrentViewMode != ViewMode.Home)
                            ShowAddressBarEditMode();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.Delete:
                        HandleDelete(); // Send to Recycle Bin
                        e.Handled = true;
                        break;
                }
            }
        }

        #endregion

        #region Mouse Back/Forward Buttons (XButton1/XButton2)

        private void OnGlobalPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            var properties = e.GetCurrentPoint(this.Content).Properties;
            if (properties.IsXButton1Pressed)
            {
                // Mouse Back button (XButton1)
                _ = GoBackAndFocusAsync();
                e.Handled = true;
            }
            else if (properties.IsXButton2Pressed)
            {
                // Mouse Forward button (XButton2)
                _ = GoForwardAndFocusAsync();
                e.Handled = true;
            }
            // Middle-click → 새 탭: Ctrl+Enter 단축키로 대체 (WinUI 3 ScrollViewer 제약)
            else if (properties.IsLeftButtonPressed)
            {
                // 좌클릭: 빈 영역 클릭 시에도 진행 중인 리네임 취소
                // (SelectionChanged/GotFocus는 빈 영역에서 발생하지 않으므로 여기서 보완)
                // 단, 리네임 TextBox 내부 클릭은 제외
                var source = e.OriginalSource as DependencyObject;
                while (source != null)
                {
                    if (source is TextBox tb && tb.DataContext is ViewModels.FileSystemViewModel fsvm && fsvm.IsRenaming)
                        return; // 리네임 TextBox 클릭 — 취소하지 않음
                    source = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(source);
                }
                CancelAnyActiveRename();
            }
        }

        #endregion

        #region Miller Columns Keyboard (ItemsControl level)

        /// <summary>
        /// Miller Column 전용 키보드 이벤트 핸들러 (ItemsControl 레벨).
        /// 방향키 탐색(좌/우), Enter 실행, Backspace 뒤로, Home/End 이동,
        /// 타입 어헤드 검색, 스페이스바 QuickLook 등을 처리한다.
        /// 컨텍스트 메뉴/리네임 중이거나 Ctrl/Alt 조합이면 처리를 건너뛴다.
        /// </summary>
        private void OnMillerKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // ★ 컨텍스트 메뉴 열려 있으면 AccessKey 처리에 맡김
            if (IsContextMenuOpen()) return;

            // ★ 이름 변경 직후의 Enter/Esc가 파일 실행으로 이어지는 것을 방지
            if (_justFinishedRename)
            {
                _justFinishedRename = false;
                e.Handled = true;
                return;
            }

            // ★ 이름 변경 중이면 밀러 키보드 처리 안 함
            var currentSelected = GetCurrentSelected();
            if (currentSelected != null && currentSelected.IsRenaming) return;

            // ★ Ctrl/Alt 조합이면 type-ahead 처리 안 하고 글로벌 핸들러에 맡김
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl || alt) return;

            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return;
            var columns = explorer.Columns;
            if (columns.Count == 0) return;

            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Right:
                    HandleRightArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Left:
                    HandleLeftArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Enter:
                    HandleEnter(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    HandleLeftArrow(activeIndex);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Home:
                    HandleHomeEnd(activeIndex, first: true);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.End:
                    HandleHomeEnd(activeIndex, first: false);
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Space:
                    if (_settings.EnableQuickLook)
                    {
                        HandleQuickLook(activeIndex);
                        e.Handled = true;
                    }
                    else
                    {
                        HandleTypeAhead(e, activeIndex);
                    }
                    break;

                default:
                    HandleTypeAhead(e, activeIndex);
                    break;
            }
        }

        #endregion

        #region Navigation (Arrow Keys, Enter, Backspace)

        /// <summary>
        /// 오른쪽 화살표 키 처리: 선택된 폴더의 첫 번째 자식 항목에 포커스를 이동하거나
        /// 파일을 연다.
        /// </summary>
        private void HandleRightArrow(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer?.Columns;
            if (columns == null) return;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel selectedFolder)
            {
                if (activeIndex + 1 < columns.Count)
                {
                    // Child column exists — just focus it
                    FocusColumnAsync(activeIndex + 1);
                }
                else
                {
                    // Child column not yet created (auto-selected without navigation)
                    // Force navigation by resetting SelectedChild
                    currentColumn.SelectedChild = null;
                    currentColumn.SelectedChild = selectedFolder;
                    // Focus the new column after a brief delay for async load
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
                        () =>
                        {
                            if (activeIndex + 1 < (ViewModel.ActiveExplorer?.Columns.Count ?? 0))
                                FocusColumnAsync(activeIndex + 1);
                        });
                }
            }
        }

        /// <summary>
        /// 왼쪽 화살표 키 처리: 부모 컬럼으로 포커스를 이동한다.
        /// </summary>
        private void HandleLeftArrow(int activeIndex)
        {
            if (activeIndex > 0)
            {
                FocusColumnAsync(activeIndex - 1);
            }
        }

        /// <summary>
        /// Enter 키 처리: 선택된 파일을 열거나, MillerClickBehavior 설정에 따라
        /// 폴더 자동 탐색을 실행한다.
        /// </summary>
        private void HandleEnter(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer?.Columns;
            if (columns == null) return;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel selectedFolder)
            {
                HandleRightArrow(activeIndex);
            }
            else if (currentColumn.SelectedChild is FileViewModel fileVm)
            {
                if (Helpers.ArchivePathHelper.IsArchiveFile(fileVm.Path))
                {
                    // Archive already navigated on selection; Enter is no-op
                    Helpers.DebugLogger.Log($"[Keyboard] Enter on archive: {fileVm.Name} (already navigated)");
                }
                else if (Helpers.ArchivePathHelper.IsArchivePath(fileVm.Path))
                {
                    _ = OpenArchiveEntryAsync(fileVm.Path);
                }
                else
                {
                    var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                    shellService.OpenFile(fileVm.Path);
                }
            }
        }

        /// <summary>
        /// Home/End 키 처리: Home은 첫 번째, End는 마지막 항목으로 이동한다.
        /// </summary>
        private void HandleHomeEnd(int activeIndex, bool first)
        {
            var columns = ViewModel.ActiveExplorer?.Columns;
            if (columns == null || activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            if (column.Children.Count == 0) return;

            var target = first ? column.Children[0] : column.Children[column.Children.Count - 1];
            column.SelectedChild = target;

            var listView = GetListViewForColumn(activeIndex);
            listView?.ScrollIntoView(target);
        }

        #endregion

        #region Type-Ahead Search

        /// <summary>
        /// KeyDown에서 타입 어헤드를 처리한 경우 CharacterReceived에서 중복 처리 방지용 플래그.
        /// </summary>
        private bool _typeAheadHandledInKeyDown;

        /// <summary>
        /// 타입 어헤드 검색 (KeyDown 경로): Latin 문자를 VirtualKey에서 변환하여 검색한다.
        /// 비라틴 문자(한글/일본어/중국어)는 KeyToChar가 '\0'을 반환하므로
        /// CharacterReceived 핸들러에서 처리된다.
        /// </summary>
        private void HandleTypeAhead(KeyRoutedEventArgs e, int activeIndex)
        {
            char ch = KeyToChar(e.Key);
            if (ch == '\0') return; // Non-Latin → CharacterReceived will handle

            DoTypeAheadSearch(ch, activeIndex);
            e.Handled = true;
            _typeAheadHandledInKeyDown = true;
        }

        /// <summary>
        /// CharacterReceived 핸들러: 비라틴 문자(한글, 일본어, 중국어 등) 타입 어헤드 지원.
        /// IME 입력 완성 후 실제 유니코드 문자를 받아 검색한다.
        /// Latin 문자는 HandleTypeAhead(KeyDown)에서 이미 처리되므로 플래그로 중복 방지.
        /// </summary>
        internal void OnMillerCharacterReceived(UIElement sender, CharacterReceivedRoutedEventArgs args)
        {
            // 컨텍스트 메뉴 열려 있으면 AccessKey 처리에 맡김
            if (IsContextMenuOpen()) return;

            // KeyDown에서 이미 처리된 Latin 문자면 건너뛰기
            if (_typeAheadHandledInKeyDown)
            {
                _typeAheadHandledInKeyDown = false;
                return;
            }

            char ch = args.Character;
            if (char.IsControl(ch)) return;

            // 이름 변경 중이면 무시
            var currentSelected = GetCurrentSelected();
            if (currentSelected?.IsRenaming == true) return;

            // Help 오버레이 열려 있으면 무시
            if (_isHelpOpen) return;

            // Settings/Home/ActionLog 모드면 무시
            if (ViewModel.CurrentViewMode == ViewMode.Settings ||
                ViewModel.CurrentViewMode == ViewMode.Home ||
                ViewModel.CurrentViewMode == ViewMode.ActionLog) return;

            var columns = ViewModel?.ActiveExplorer?.Columns;
            if (columns == null || columns.Count == 0) return;

            int activeIndex = GetActiveColumnIndex();
            if (activeIndex < 0) activeIndex = columns.Count - 1;

            DoTypeAheadSearch(ch, activeIndex);
            args.Handled = true;
        }

        /// <summary>
        /// 타입 어헤드 공통 검색 로직: 문자를 버퍼에 추가하고 매칭 항목을 찾아 선택한다.
        /// </summary>
        private void DoTypeAheadSearch(char ch, int activeIndex)
        {
            _typeAheadBuffer += ch;
            _typeAheadTimer?.Stop();
            _typeAheadTimer?.Start();

            var columns = ViewModel.ActiveExplorer?.Columns;
            if (columns == null || activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            var match = column.Children.FirstOrDefault(c =>
                c.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                column.SelectedChild = match;
                var listView = GetListViewForColumn(activeIndex);
                listView?.ScrollIntoView(match);
            }
        }

        /// <summary>
        /// VirtualKey를 문자로 변환한다. Latin 문자 전용 (A-Z, 0-9, 기호).
        /// 비라틴 문자(한글/일본어/중국어)는 '\0'을 반환하며 CharacterReceived에서 처리된다.
        /// </summary>
        private static char KeyToChar(Windows.System.VirtualKey key)
        {
            if (key >= Windows.System.VirtualKey.A && key <= Windows.System.VirtualKey.Z)
                return (char)('a' + (key - Windows.System.VirtualKey.A));
            if (key >= Windows.System.VirtualKey.Number0 && key <= Windows.System.VirtualKey.Number9)
                return (char)('0' + (key - Windows.System.VirtualKey.Number0));
            if (key >= Windows.System.VirtualKey.NumberPad0 && key <= Windows.System.VirtualKey.NumberPad9)
                return (char)('0' + (key - Windows.System.VirtualKey.NumberPad0));
            if (key == Windows.System.VirtualKey.Space) return ' ';
            if (key == (Windows.System.VirtualKey)190) return '.';
            if (key == (Windows.System.VirtualKey)189) return '-';
            return '\0';
        }

        #endregion

        #region Fullscreen Toggle (F11)

        private bool _isFullScreen = false;

        /// <summary>
        /// F11 전체 화면 토글. OverlappedPresenter(기본)와 FullScreen 간 전환.
        /// </summary>
        private void ToggleFullScreen()
        {
            _isFullScreen = !_isFullScreen;
            if (_isFullScreen)
            {
                this.AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                this.AppWindow.SetPresenter(AppWindowPresenterKind.Default);
            }
        }

        #endregion

        #region Back/Forward Async Helpers

        /// <summary>
        /// GoBack + focus — async/await 패턴으로 UI 스레드 유지.
        /// ContinueWith(ThreadPool) → DispatcherQueue 접근 패턴의 간헐적 포커스 유실 방지.
        /// </summary>
        private async Task GoBackAndFocusAsync()
        {
            try
            {
                await ViewModel.GoBackAsync();
                FocusLastColumnAfterNavigation();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] GoBack error: {ex.Message}");
            }
        }

        /// <summary>
        /// GoForward + focus — async/await 패턴으로 UI 스레드 유지.
        /// </summary>
        private async Task GoForwardAndFocusAsync()
        {
            try
            {
                await ViewModel.GoForwardAsync();
                FocusLastColumnAfterNavigation();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[Navigation] GoForward error: {ex.Message}");
            }
        }

        #endregion

        #region Quick Look (Space key → floating Quick Look window)

        /// <summary>
        /// 스페이스바로 Quick Look 플로팅 윈도우를 토글한다 (macOS Finder Quick Look 방식).
        /// 파일 선택 시 윈도우를 열고, 다시 누르면 닫는다.
        /// 폴더 선택 시에는 열려 있는 Quick Look을 닫는다.
        /// </summary>
        private void HandleQuickLook(int activeIndex)
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer == null) return;

            var columns = explorer.Columns;
            if (columns == null || activeIndex < 0 || activeIndex >= columns.Count) return;

            var selectedItem = columns[activeIndex].SelectedChild;
            if (selectedItem == null)
            {
                CloseQuickLookWindow();
                return;
            }

            // Quick Look이 이미 열려 있으면 닫기 (토글)
            if (_quickLookWindow != null)
            {
                CloseQuickLookWindow();
                return;
            }

            // Quick Look 윈도우 열기 (파일 + 폴더 모두 지원)
            OpenQuickLookWindow(selectedItem);
        }

        /// <summary>
        /// Quick Look 플로팅 윈도우를 열고 선택된 파일의 미리보기를 표시한다.
        /// </summary>
        private void OpenQuickLookWindow(FileSystemViewModel selectedItem)
        {
            _quickLookWindow = new Views.QuickLookWindow();
            _quickLookWindow.SetMainWindow(this.AppWindow);
            _quickLookWindow.WindowClosed += OnQuickLookWindowClosed;
            _quickLookWindow.UpdateContent(selectedItem);
            _quickLookWindow.Activate();

            Helpers.DebugLogger.Log($"[QuickLook] Opened for: {selectedItem.Name}");
        }

        /// <summary>
        /// Quick Look 윈도우를 닫는다.
        /// </summary>
        internal void CloseQuickLookWindow()
        {
            if (_quickLookWindow != null)
            {
                try
                {
                    _quickLookWindow.WindowClosed -= OnQuickLookWindowClosed;
                    _quickLookWindow.Close();
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[QuickLook] Close error: {ex.Message}");
                }
                _quickLookWindow = null;
                Helpers.DebugLogger.Log("[QuickLook] Closed");
            }
        }

        /// <summary>
        /// Quick Look 윈도우가 사용자에 의해 닫힐 때 (X 버튼 등) 호출.
        /// </summary>
        private void OnQuickLookWindowClosed()
        {
            _quickLookWindow = null;
            Helpers.DebugLogger.Log("[QuickLook] Window closed by user");
        }

        /// <summary>
        /// Quick Look이 열려 있으면 선택된 항목의 미리보기를 업데이트한다.
        /// 화살표 키로 파일 선택이 변경될 때 호출된다.
        /// </summary>
        internal void UpdateQuickLookContent(FileSystemViewModel? selectedItem)
        {
            if (_quickLookWindow == null) return;

            if (selectedItem == null)
            {
                CloseQuickLookWindow();
                return;
            }

            // 파일/폴더 모두 미리보기 업데이트
            _quickLookWindow.UpdateContent(selectedItem);
        }

        #endregion
    }
}
