using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Span
{
    public sealed partial class MainWindow
    {
        #region Global Keyboard Shortcuts (OnGlobalKeyDown)

        private void OnGlobalKeyDown(object sender, KeyRoutedEventArgs e)
        {
            // 이름 변경 중이면 F2(선택 영역 순환)만 허용, 나머지 글로벌 단축키 무시
            var selected = GetCurrentSelected();
            if (selected != null && selected.IsRenaming && e.Key != Windows.System.VirtualKey.F2) return;

            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                        .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

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

            // Settings/Home 모드: 파일 조작 단축키 차단, 뷰 전환/탭/Escape만 허용
            if (ViewModel.CurrentViewMode == ViewMode.Settings || ViewModel.CurrentViewMode == ViewMode.Home)
            {
                if (e.Key == Windows.System.VirtualKey.Escape && ViewModel.CurrentViewMode == ViewMode.Settings)
                {
                    CloseCurrentSettingsTab();
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

            // Alt+Left/Right: Back/Forward navigation (highest priority)
            // Alt+Enter: Show Properties dialog
            if (alt && !ctrl && !shift)
            {
                switch (e.Key)
                {
                    case Windows.System.VirtualKey.Left:
                        _ = ViewModel.GoBackAsync().ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                            System.Threading.Tasks.TaskScheduler.Default);
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Right:
                        _ = ViewModel.GoForwardAsync().ContinueWith(_ =>
                            DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                            System.Threading.Tasks.TaskScheduler.Default);
                        e.Handled = true;
                        return;

                    case Windows.System.VirtualKey.Enter:
                        HandleShowProperties();
                        e.Handled = true;
                        return;
                }
            }

            if (ctrl)
            {
                Helpers.DebugLogger.Log($"[Keyboard] Ctrl+Key: Key={(int)e.Key} ({e.Key}), OriginalKey={(int)e.OriginalKey} ({e.OriginalKey}), ScanCode={e.KeyStatus.ScanCode}");

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
                        if (ViewModel.CurrentViewMode != ViewMode.Home)
                            ShowAddressBarEditMode();
                        e.Handled = true;
                        break;

                    case Windows.System.VirtualKey.F:
                        SearchBox.Focus(FocusState.Keyboard);
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
                        HandlePaste();
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
                                ViewModel.ShowToast("All columns equalized to default width");
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
                                ViewModel.ShowToast("All columns auto-fitted to content");
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
                _ = ViewModel.GoBackAsync().ContinueWith(_ =>
                    DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                    System.Threading.Tasks.TaskScheduler.Default);
                e.Handled = true;
            }
            else if (properties.IsXButton2Pressed)
            {
                // Mouse Forward button (XButton2)
                _ = ViewModel.GoForwardAsync().ContinueWith(_ =>
                    DispatcherQueue.TryEnqueue(() => FocusLastColumnAfterNavigation()),
                    System.Threading.Tasks.TaskScheduler.Default);
                e.Handled = true;
            }
            else if (properties.IsLeftButtonPressed)
            {
                // 좌클릭: 빈 영역 클릭 시에도 진행 중인 리네임 취소
                // (SelectionChanged/GotFocus는 빈 영역에서 발생하지 않으므로 여기서 보완)
                CancelAnyActiveRename();
            }
        }

        #endregion

        #region Miller Columns Keyboard (ItemsControl level)

        private void OnMillerKeyDown(object sender, KeyRoutedEventArgs e)
        {
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

            var columns = ViewModel.ActiveExplorer.Columns;
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

        private void HandleRightArrow(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel && activeIndex + 1 < columns.Count)
            {
                FocusColumnAsync(activeIndex + 1);
            }
        }

        private void HandleLeftArrow(int activeIndex)
        {
            if (activeIndex > 0)
            {
                FocusColumnAsync(activeIndex - 1);
            }
        }

        private void HandleEnter(int activeIndex)
        {
            var columns = ViewModel.ActiveExplorer.Columns;
            var currentColumn = columns[activeIndex];

            if (currentColumn.SelectedChild is FolderViewModel)
            {
                HandleRightArrow(activeIndex);
            }
            else if (currentColumn.SelectedChild is FileViewModel fileVm)
            {
                var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                shellService.OpenFile(fileVm.Path);
            }
        }

        #endregion

        #region Type-Ahead Search

        private void HandleTypeAhead(KeyRoutedEventArgs e, int activeIndex)
        {
            char ch = KeyToChar(e.Key);
            if (ch == '\0') return;

            _typeAheadBuffer += ch;
            _typeAheadTimer?.Stop();
            _typeAheadTimer?.Start();

            var columns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var column = columns[activeIndex];
            var match = column.Children.FirstOrDefault(c =>
                c.Name.StartsWith(_typeAheadBuffer, StringComparison.OrdinalIgnoreCase));

            if (match != null)
            {
                column.SelectedChild = match;
                var listView = GetListViewForColumn(activeIndex);
                listView?.ScrollIntoView(match);
            }

            e.Handled = true;
        }

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

        #region Quick Look (Space key preview)

        private bool _isQuickLookOpen = false;

        private async void HandleQuickLook(int activeIndex)
        {
            if (_isQuickLookOpen) return;

            var columns = ViewModel.ActiveExplorer.Columns;
            if (activeIndex < 0 || activeIndex >= columns.Count) return;

            var selected = columns[activeIndex].SelectedChild;
            if (selected == null) return;

            _isQuickLookOpen = true;
            try
            {
                var previewService = App.Current.Services.GetRequiredService<Services.PreviewService>();
                bool isFolder = selected is FolderViewModel;
                var previewType = previewService.GetPreviewType(selected.Path, isFolder);

                // Build dialog content
                var content = await BuildQuickLookContentAsync(previewService, previewType, selected);

                var dialog = new ContentDialog
                {
                    Title = selected.Name,
                    Content = content,
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                // Set dialog size constraints
                dialog.Resources["ContentDialogMaxWidth"] = 800.0;

                await dialog.ShowAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] Error: {ex.Message}");
            }
            finally
            {
                _isQuickLookOpen = false;
                FocusColumnAsync(activeIndex);
            }
        }

        private async Task<FrameworkElement> BuildQuickLookContentAsync(
            Services.PreviewService previewService, Models.PreviewType previewType,
            ViewModels.FileSystemViewModel selected)
        {
            var meta = previewService.GetBasicMetadata(selected.Path);
            using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));

            switch (previewType)
            {
                case Models.PreviewType.Image:
                    var bitmap = await previewService.LoadImagePreviewAsync(selected.Path, 800, cts.Token);
                    if (bitmap != null)
                    {
                        var img = new Image
                        {
                            Source = bitmap,
                            MaxWidth = 760,
                            MaxHeight = 500,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        };
                        return WrapWithMetadata(img, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Text:
                    var text = await previewService.LoadTextPreviewAsync(selected.Path, cts.Token);
                    if (text != null)
                    {
                        var textBlock = new TextBlock
                        {
                            Text = text,
                            TextWrapping = TextWrapping.Wrap,
                            FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                            FontSize = 12,
                            IsTextSelectionEnabled = true,
                            MaxHeight = 400
                        };
                        var scroller = new ScrollViewer
                        {
                            Content = textBlock,
                            MaxHeight = 400,
                            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                        };
                        return WrapWithMetadata(scroller, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Pdf:
                    var pdfBitmap = await previewService.LoadPdfPreviewAsync(selected.Path, cts.Token);
                    if (pdfBitmap != null)
                    {
                        var pdfImg = new Image
                        {
                            Source = pdfBitmap,
                            MaxWidth = 760,
                            MaxHeight = 500,
                            Stretch = Microsoft.UI.Xaml.Media.Stretch.Uniform
                        };
                        return WrapWithMetadata(pdfImg, meta);
                    }
                    return CreateGenericPreview(meta);

                case Models.PreviewType.Folder:
                    int count = previewService.GetFolderItemCount(selected.Path);
                    var folderInfo = new TextBlock
                    {
                        Text = $"{count} items",
                        FontSize = 16,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        Margin = new Thickness(0, 20, 0, 10)
                    };
                    var folderMeta = new TextBlock
                    {
                        Text = $"{_loc.Get("Date")}: {meta.Modified:g}",
                        FontSize = 12,
                        Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    var folderStack = new StackPanel { MinWidth = 300 };
                    folderStack.Children.Add(folderInfo);
                    folderStack.Children.Add(folderMeta);
                    return folderStack;

                default: // Generic
                    return CreateGenericPreview(meta);
            }
        }

        private StackPanel WrapWithMetadata(FrameworkElement preview, Services.FilePreviewMetadata meta)
        {
            var stack = new StackPanel { Spacing = 8, MinWidth = 300 };
            stack.Children.Add(preview);

            var metaText = new TextBlock
            {
                Text = $"{meta.SizeFormatted}  |  {meta.Modified:g}",
                FontSize = 11,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(metaText);
            return stack;
        }

        private StackPanel CreateGenericPreview(Services.FilePreviewMetadata meta)
        {
            var stack = new StackPanel { Spacing = 8, MinWidth = 300 };

            var icon = new FontIcon
            {
                Glyph = "\uE7C3", // generic file icon
                FontSize = 48,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 20, 0, 10)
            };
            stack.Children.Add(icon);

            var nameText = new TextBlock
            {
                Text = meta.FileName,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(nameText);

            var details = new TextBlock
            {
                Text = $"{meta.SizeFormatted}  |  {meta.Extension}  |  {meta.Modified:g}",
                FontSize = 12,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SpanTextSecondaryBrush"],
                HorizontalAlignment = HorizontalAlignment.Center
            };
            stack.Children.Add(details);

            return stack;
        }

        #endregion
    }
}
