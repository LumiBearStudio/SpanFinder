using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using System;
using System.Linq;

namespace Span
{
    public sealed partial class MainWindow
    {
        // =================================================================
        //  #region Theme Application
        // =================================================================

        private void ApplyTheme(string theme)
        {
            bool isCustom = _customThemes.Contains(theme);

            if (this.Content is FrameworkElement root)
            {
                var targetTheme = theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ when isCustom => ElementTheme.Dark, // 커스텀 테마는 Dark 기반
                    _ => ElementTheme.Default
                };

                // 커스텀 테마: 리소스 설정 후 테마 토글로 {ThemeResource} 바인딩 강제 갱신
                if (isCustom)
                {
                    // 1) 먼저 Light로 전환하여 기존 Dark 리소스 해제
                    root.RequestedTheme = ElementTheme.Light;
                    // 2) 커스텀 리소스 오버라이드 적용
                    ApplyCustomThemeOverrides(root, theme);
                    // 3) Dark로 복귀 → 모든 {ThemeResource} 바인딩 재평가
                    root.RequestedTheme = ElementTheme.Dark;
                }
                else
                {
                    // 비커스텀: 오버라이드 제거 후 테마 적용
                    ApplyCustomThemeOverrides(root, theme);
                    // 반대 테마로 한 번 토글하여 {ThemeResource} 바인딩 강제 갱신
                    // (커스텀(Dark기반) → dark 전환 시 동일 ElementTheme이면 갱신 안 됨)
                    root.RequestedTheme = targetTheme == ElementTheme.Light
                        ? ElementTheme.Dark : ElementTheme.Light;
                    root.RequestedTheme = targetTheme;
                }
            }

            // 캡션 버튼 색상
            var titleBar = this.AppWindow.TitleBar;

            if (isCustom)
            {
                var cap = GetCaptionColors(theme);
                titleBar.ButtonForegroundColor = cap.fg;
                titleBar.ButtonHoverForegroundColor = cap.hoverFg;
                titleBar.ButtonHoverBackgroundColor = cap.hoverBg;
                titleBar.ButtonPressedForegroundColor = cap.pressedFg;
                titleBar.ButtonPressedBackgroundColor = cap.pressedBg;
                titleBar.ButtonInactiveForegroundColor = cap.inactiveFg;
            }
            else
            {
                bool isLight = theme == "light" ||
                               (theme == "system" && App.Current.RequestedTheme == ApplicationTheme.Light);

                if (isLight)
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 26, 26, 26);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(20, 0, 0, 0);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 0, 0, 0);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(40, 0, 0, 0);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 140, 140, 140);
                }
                else
                {
                    titleBar.ButtonForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonHoverBackgroundColor = Windows.UI.Color.FromArgb(15, 255, 255, 255);
                    titleBar.ButtonPressedForegroundColor = Windows.UI.Color.FromArgb(255, 255, 255, 255);
                    titleBar.ButtonPressedBackgroundColor = Windows.UI.Color.FromArgb(20, 255, 255, 255);
                    titleBar.ButtonInactiveForegroundColor = Windows.UI.Color.FromArgb(255, 120, 120, 120);
                }
            }
            titleBar.ButtonBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
            titleBar.ButtonInactiveBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
        }

        // 원본 Dark ThemeDictionary 백업 (최초 한 번만)
        private ResourceDictionary? _originalDarkThemeDict;

        private void ApplyCustomThemeOverrides(FrameworkElement root, string theme)
        {
            // 원본 백업 (최초 1회)
            if (_originalDarkThemeDict == null && root.Resources.ThemeDictionaries.ContainsKey("Dark"))
            {
                var orig = (ResourceDictionary)root.Resources.ThemeDictionaries["Dark"];
                _originalDarkThemeDict = new ResourceDictionary();
                foreach (var kvp in orig)
                    _originalDarkThemeDict[kvp.Key] = kvp.Value;
            }

            if (!_customThemes.Contains(theme))
            {
                // 원본 Dark ThemeDictionary 복원
                if (_originalDarkThemeDict != null)
                {
                    var restored = new ResourceDictionary();
                    foreach (var kvp in _originalDarkThemeDict)
                        restored[kvp.Key] = kvp.Value;
                    root.Resources.ThemeDictionaries["Dark"] = restored;
                }
                return;
            }

            var p = GetThemePalette(theme);

            // 원본 Dark dict를 기반으로 커스텀 값 덮어쓰기
            var darkDict = new ResourceDictionary();
            if (_originalDarkThemeDict != null)
            {
                foreach (var kvp in _originalDarkThemeDict)
                    darkDict[kvp.Key] = kvp.Value;
            }

            // Color 리소스
            darkDict["SpanBgMica"]        = p.bgMica;
            darkDict["SpanBgLayer1"]      = p.bgLayer1;
            darkDict["SpanBgLayer2"]      = p.bgLayer2;
            darkDict["SpanBgLayer3"]      = p.bgLayer3;
            darkDict["SpanAccent"]        = p.accent;
            darkDict["SpanAccentHover"]   = p.accentHover;
            darkDict["SpanTextPrimary"]   = p.textPri;
            darkDict["SpanTextSecondary"] = p.textSec;
            darkDict["SpanTextTertiary"]  = p.textTer;
            darkDict["SpanBgSelected"]    = p.bgSel;
            darkDict["SpanBorderSubtle"]  = p.border;

            // Brush 리소스
            darkDict["SpanBgMicaBrush"]        = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgMica);
            darkDict["SpanBgLayer1Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer1);
            darkDict["SpanBgLayer2Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer2);
            darkDict["SpanBgLayer3Brush"]      = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer3);
            darkDict["SpanAccentBrush"]        = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accent);
            darkDict["SpanAccentHoverBrush"]   = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accentHover);
            darkDict["SpanTextPrimaryBrush"]   = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textPri);
            darkDict["SpanTextSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textSec);
            darkDict["SpanTextTertiaryBrush"]  = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textTer);
            darkDict["SpanBgSelectedBrush"]    = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgSel);
            darkDict["SpanBorderSubtleBrush"]  = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.border);

            // ListView/GridView 선택 색상
            darkDict["ListViewItemBackgroundSelected"]            = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSel);
            darkDict["ListViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelHover);
            darkDict["ListViewItemBackgroundSelectedPressed"]     = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelPressed);
            darkDict["GridViewItemBackgroundSelected"]            = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSel);
            darkDict["GridViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelHover);
            darkDict["GridViewItemBackgroundSelectedPressed"]     = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.listSelPressed);

            root.Resources.ThemeDictionaries["Dark"] = darkDict;
        }

        private static (
            Windows.UI.Color bgMica, Windows.UI.Color bgLayer1, Windows.UI.Color bgLayer2, Windows.UI.Color bgLayer3,
            Windows.UI.Color accent, Windows.UI.Color accentHover,
            Windows.UI.Color textPri, Windows.UI.Color textSec, Windows.UI.Color textTer,
            Windows.UI.Color bgSel, Windows.UI.Color border,
            Windows.UI.Color listSel, Windows.UI.Color listSelHover, Windows.UI.Color listSelPressed
        ) GetThemePalette(string theme) => theme switch
        {
            "dracula" => (
                Clr("#282a36"), Clr("#1e2029"), Clr("#282a36"), Clr("#44475a"),  // Background layers
                Clr("#bd93f9"), Clr("#caa8ff"),                                   // Purple accent
                Clr("#f8f8f2"), Clr("#6272a4"), Clr("#44475a"),                   // Foreground text
                Clr("#4Dbd93f9"), Clr("#33f8f8f2"),                               // Selection/border
                Clr("#99bd93f9"), Clr("#B3bd93f9"), Clr("#80bd93f9")              // List selection
            ),
            "tokyonight" => (
                Clr("#16161e"), Clr("#1a1b26"), Clr("#292e42"), Clr("#414868"),   // Tokyo Night bg layers
                Clr("#7aa2f7"), Clr("#7dcfff"),                                   // Blue + Cyan accent
                Clr("#c0caf5"), Clr("#a9b1d6"), Clr("#565f89"),                   // fg layers
                Clr("#4D7aa2f7"), Clr("#333b4261"),                               // Selection/border
                Clr("#997aa2f7"), Clr("#B37aa2f7"), Clr("#807aa2f7")              // List selection
            ),
            "catppuccin" => (
                Clr("#11111b"), Clr("#1e1e2e"), Clr("#181825"), Clr("#313244"),   // Crust→Base→Mantle→Surface0
                Clr("#cba6f7"), Clr("#b4befe"),                                   // Mauve + Lavender
                Clr("#cdd6f4"), Clr("#bac2de"), Clr("#7f849c"),                   // Text→Subtext1→Overlay1
                Clr("#4Dcba6f7"), Clr("#33585b70"),                               // Selection/border
                Clr("#99cba6f7"), Clr("#B3cba6f7"), Clr("#80cba6f7")              // List selection
            ),
            "gruvbox" => (
                Clr("#1d2021"), Clr("#282828"), Clr("#3c3836"), Clr("#504945"),   // bg0_h→bg0→bg1→bg2
                Clr("#fe8019"), Clr("#fabd2f"),                                   // Orange + Yellow accent
                Clr("#ebdbb2"), Clr("#d5c4a1"), Clr("#a89984"),                   // fg→fg2→fg4
                Clr("#4Dfe8019"), Clr("#33ebdbb2"),                               // Selection/border
                Clr("#99fe8019"), Clr("#B3fe8019"), Clr("#80fe8019")              // List selection
            ),
            _ => default
        };

        private static (
            Windows.UI.Color fg, Windows.UI.Color hoverFg, Windows.UI.Color hoverBg,
            Windows.UI.Color pressedFg, Windows.UI.Color pressedBg, Windows.UI.Color inactiveFg
        ) GetCaptionColors(string theme) => theme switch
        {
            "dracula" => (
                Clr("#f8f8f2"), Clr("#bd93f9"), Clr("#33bd93f9"),
                Clr("#caa8ff"), Clr("#4Dbd93f9"), Clr("#6272a4")
            ),
            "tokyonight" => (
                Clr("#a9b1d6"), Clr("#c0caf5"), Clr("#26394b70"),
                Clr("#c0caf5"), Clr("#40394b70"), Clr("#737aa2")
            ),
            "catppuccin" => (
                Clr("#a6adc8"), Clr("#cdd6f4"), Clr("#40585b70"),
                Clr("#bac2de"), Clr("#5945475a"), Clr("#6c7086")
            ),
            "gruvbox" => (
                Clr("#a89984"), Clr("#ebdbb2"), Clr("#1Febdbb2"),
                Clr("#fe8019"), Clr("#33fe8019"), Clr("#665c54")
            ),
            _ => (
                Clr("#FFFFFF"), Clr("#FFFFFF"), Clr("#0FFFFFFF"),
                Clr("#FFFFFF"), Clr("#14FFFFFF"), Clr("#787878")
            )
        };

        private static Windows.UI.Color Clr(string hex)
        {
            hex = hex.TrimStart('#');
            byte a = 255, r, g, b;
            if (hex.Length == 8)
            {
                a = Convert.ToByte(hex[..2], 16);
                r = Convert.ToByte(hex[2..4], 16);
                g = Convert.ToByte(hex[4..6], 16);
                b = Convert.ToByte(hex[6..8], 16);
            }
            else
            {
                r = Convert.ToByte(hex[..2], 16);
                g = Convert.ToByte(hex[2..4], 16);
                b = Convert.ToByte(hex[4..6], 16);
            }
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }

        // #endregion Theme Application

        // =================================================================
        //  #region Setting Changed Handlers
        // =================================================================

        private void OnSettingChanged(string key, object? value)
        {
            if (_isClosed) return;

            switch (key)
            {
                case "Theme":
                    DispatcherQueue.TryEnqueue(() => ApplyTheme(value as string ?? "system"));
                    break;

                case "FontFamily":
                    DispatcherQueue.TryEnqueue(() => ApplyFontFamily(value as string ?? "Segoe UI Variable"));
                    break;

                case "Density":
                    DispatcherQueue.TryEnqueue(() => ApplyDensity(value as string ?? "comfortable"));
                    break;

                case "ShowHiddenFiles":
                case "ShowFileExtensions":
                    // Refresh current folder contents to apply filter change
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        RefreshCurrentView();
                    });
                    break;

                case "Language":
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        _loc.Language = value as string ?? "en";
                    });
                    break;

                case "MillerClickBehavior":
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        bool isDouble = (value as string) == "double";
                        bool leftIsMiller = ViewModel.LeftViewMode == Models.ViewMode.MillerColumns;
                        bool rightIsMiller = ViewModel.RightViewMode == Models.ViewMode.MillerColumns;
                        ViewModel.Explorer.EnableAutoNavigation = leftIsMiller && !isDouble;
                        ViewModel.RightExplorer.EnableAutoNavigation = rightIsMiller && !isDouble;
                    });
                    break;

                case "ShowCheckboxes":
                    DispatcherQueue.TryEnqueue(() => ApplyMillerCheckboxMode(value is bool cb && cb));
                    break;

                case "ShowThumbnails":
                    DispatcherQueue.TryEnqueue(() => ToggleThumbnails(value is bool st && st));
                    break;

                case "ShowFavoritesTree":
                    DispatcherQueue.TryEnqueue(() => ApplyFavoritesTreeMode(value is bool v && v));
                    break;

                case "ShowGitIntegration":
                    // Git 통합 ON/OFF 시 모든 로드된 컬럼 새로고침 (git 감지 재실행)
                    DispatcherQueue.TryEnqueue(() => RefreshAllColumnsForGit());
                    break;
            }
        }

        private void ApplyFontFamily(string fontFamily)
        {
            if (this.Content is FrameworkElement root && root.Resources != null)
            {
                var font = new FontFamily(fontFamily);
                root.Resources["ContentControlThemeFontFamily"] = font;

                if (root is Microsoft.UI.Xaml.Controls.Control control)
                {
                    control.FontFamily = font;
                }
            }
        }

        private void ApplyDensity(string density)
        {
            _densityPadding = density switch
            {
                "compact" => new Thickness(12, 0, 12, 0),
                "spacious" => new Thickness(12, 4, 12, 4),
                _ => new Thickness(12, 2, 12, 2) // comfortable
            };

            // Apply to all visible Miller Column ListViews
            foreach (var kvp in _tabMillerPanels)
                ApplyDensityToItemsControl(kvp.Value.items);
            ApplyDensityToItemsControl(MillerColumnsControlRight);

            // Apply to Details/Icon views via their public methods
            foreach (var kvp in _tabDetailsPanels)
                kvp.Value.ApplyDensity(density);
            foreach (var kvp in _tabIconPanels)
                kvp.Value.ApplyDensity(density);
        }

        private void ApplyDensityToItemsControl(ItemsControl? millerControl)
        {
            if (millerControl?.ItemsPanelRoot == null) return;
            foreach (var columnContainer in millerControl.ItemsPanelRoot.Children)
            {
                var listView = FindChild<ListView>(columnContainer);
                if (listView?.ItemsPanelRoot == null) continue;
                for (int i = 0; i < listView.Items.Count; i++)
                {
                    if (listView.ContainerFromIndex(i) is ListViewItem item)
                    {
                        var cp = FindChild<ContentPresenter>(item);
                        if (cp != null)
                        {
                            var grid = FindChild<Grid>(cp);
                            if (grid != null) grid.Padding = _densityPadding;
                        }
                    }
                }
            }
        }

        private void ApplyMillerCheckboxMode(bool showCheckboxes)
        {
            _millerSelectionMode = showCheckboxes
                ? ListViewSelectionMode.Multiple
                : ListViewSelectionMode.Extended;

            // Apply to all visible Miller Column ListViews in both panes
            // 모든 탭의 Miller 패널에도 적용
            foreach (var kvp in _tabMillerPanels)
                ApplyCheckboxToItemsControl(kvp.Value.items, _millerSelectionMode);
            ApplyCheckboxToItemsControl(MillerColumnsControlRight, _millerSelectionMode);
        }

        private void ToggleThumbnails(bool showThumbnails)
        {
            var explorer = ViewModel.ActiveExplorer;
            if (explorer?.CurrentFolder == null) return;

            foreach (var child in explorer.CurrentFolder.Children)
            {
                if (child is FileViewModel fileVm)
                {
                    if (showThumbnails && fileVm.IsThumbnailSupported)
                        _ = fileVm.LoadThumbnailAsync();
                    else
                        fileVm.UnloadThumbnail();
                }
            }
        }

        private void ApplyCheckboxToItemsControl(ItemsControl? control, ListViewSelectionMode mode)
        {
            if (control?.ItemsPanelRoot == null) return;
            for (int i = 0; i < control.Items.Count; i++)
            {
                var listView = GetListViewFromItemsControl(control, i);
                if (listView != null)
                {
                    listView.SelectionMode = mode;
                }
            }
        }

        private ListView? GetListViewFromItemsControl(ItemsControl control, int index)
        {
            var container = control.ContainerFromIndex(index) as ContentPresenter;
            if (container == null) return null;
            return FindChild<ListView>(container);
        }

        // #endregion Setting Changed Handlers

        // =================================================================
        //  #region Terminal, Settings Tab, Refresh
        // =================================================================

        private void HandleOpenTerminal()
        {
            var explorer = ViewModel.ActiveExplorer;
            var path = explorer?.CurrentPath;
            if (string.IsNullOrEmpty(path) || path == "PC")
            {
                ViewModel.ShowToast("유효한 폴더에서만 터미널을 열 수 있습니다");
                return;
            }
            if (!System.IO.Directory.Exists(path))
            {
                ViewModel.ShowToast("경로가 존재하지 않습니다");
                return;
            }
            var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
            shellService.OpenTerminal(path, _settings.DefaultTerminal);
        }

        /// <summary>
        /// Settings 탭을 닫고 이전 탭으로 복귀.
        /// 유일한 탭이면 Home 탭을 먼저 생성.
        /// </summary>
        private void CloseCurrentSettingsTab()
        {
            var tab = ViewModel.ActiveTab;
            if (tab == null || tab.ViewMode != ViewMode.Settings) return;

            int index = ViewModel.ActiveTabIndex;

            if (ViewModel.Tabs.Count <= 1)
            {
                // 유일한 탭이면 Home 탭 먼저 생성
                ViewModel.AddNewTab(); // Home 탭 추가 + 자동 SwitchToTab
                var newTab = ViewModel.ActiveTab;
                if (newTab != null)
                {
                    CreateMillerPanelForTab(newTab);
                    SwitchMillerPanel(newTab.Id);
                }
                // Settings 탭은 이제 인덱스 0
                ViewModel.CloseTab(0);
            }
            else
            {
                ViewModel.CloseTab(index);
                if (ViewModel.ActiveTab != null)
                    SwitchMillerPanel(ViewModel.ActiveTab.Id);
            }

            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            FocusActiveView();
        }

        /// <summary>
        /// Settings 탭을 열거나 기존 탭으로 전환 (UI 연동 포함).
        /// </summary>
        private void OpenSettingsTab()
        {
            ViewModel.OpenOrSwitchToSettingsTab();
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            // Tab count changed — update passthrough region
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        /// <summary>
        /// Git 통합 설정 변경 시 양쪽 패인의 모든 컬럼을 새로고침.
        /// 일반 RefreshCurrentView()는 마지막 컬럼만 새로고침하므로 부족.
        /// </summary>
        private void RefreshAllColumnsForGit()
        {
            Helpers.DebugLogger.Log("[Git.Setting] ShowGitIntegration changed, refreshing all columns");
            // 양쪽 Explorer의 모든 컬럼을 리로드
            foreach (var explorer in new[] { ViewModel.Explorer, ViewModel.RightExplorer })
            {
                foreach (var col in explorer.Columns.ToArray())
                {
                    col.ResetLoadState();
                    _ = col.EnsureChildrenLoadedAsync();
                }
            }
        }

        private void RefreshCurrentView()
        {
            // Refresh only the leaf (last) column in the active pane.
            // Refreshing ALL columns causes cascading destruction: Children.Clear()
            // sets SelectedChild=null which removes subsequent columns.
            var explorer = ViewModel.ActiveExplorer;
            if (explorer.Columns.Count > 0)
            {
                var lastCol = explorer.Columns[explorer.Columns.Count - 1];
                _ = lastCol.RefreshAsync();
            }
        }

        // #endregion Terminal, Settings Tab, Refresh

        // =================================================================
        //  #region Help Overlay, Settings/Log Button Handlers
        // =================================================================

        private bool _isHelpOpen = false;

        private void ToggleHelpOverlay()
        {
            _isHelpOpen = !_isHelpOpen;
            HelpOverlay.Visibility = _isHelpOpen ? Visibility.Visible : Visibility.Collapsed;
        }

        private void OnHelpClick(object sender, RoutedEventArgs e)
        {
            ToggleHelpOverlay();
        }

        private void HelpOverlay_PointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (_isHelpOpen)
            {
                _isHelpOpen = false;
                HelpOverlay.Visibility = Visibility.Collapsed;
                e.Handled = true;
            }
        }

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            OpenSettingsTab();
        }

        private Views.LogFlyoutContent? _logFlyout;
        private bool _isLogOpen = false;

        private void OnLogClick(object sender, RoutedEventArgs e)
        {
            if (_isLogOpen)
            {
                LogButton.Flyout?.Hide();
                _isLogOpen = false;
                return;
            }

            var logService = App.Current.Services.GetRequiredService<Services.ActionLogService>();
            if (LogButton.Flyout == null)
            {
                _logFlyout = new Views.LogFlyoutContent(logService);
                var flyout = new Flyout
                {
                    Content = _logFlyout,
                    Placement = Microsoft.UI.Xaml.Controls.Primitives.FlyoutPlacementMode.BottomEdgeAlignedRight
                };
                flyout.Closed += (s, args) => _isLogOpen = false;
                flyout.Opening += (s, args) => _logFlyout.Refresh();
                LogButton.Flyout = flyout;
            }
            else
            {
                _logFlyout?.Refresh();
            }

            LogButton.Flyout.ShowAt(LogButton);
            _isLogOpen = true;
        }

        // #endregion Help Overlay, Settings/Log Button Handlers
    }
}
