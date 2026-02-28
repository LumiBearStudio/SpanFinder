using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.ViewModels;
using System;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Span
{
    /// <summary>
    /// MainWindow의 설정 처리 부분 클래스.
    /// 테마 적용(Light/Dark/커스텀 테마 오버라이드), 폰트 패밀리·밀도 설정,
    /// 숨김 파일·체크박스·즐겨찾기 트리 표시 전환,
    /// Miller Column 클릭 동작 설정, 미리보기 패널 활성화,
    /// 로컬라이제이션 문자열 적용 등 설정 변경 이벤트 처리를 담당한다.
    /// </summary>
    public sealed partial class MainWindow
    {
        // =================================================================
        //  #region Theme Application
        // =================================================================

        /// <summary>
        /// 테마를 적용한다. Light/Dark/System/커스텀 테마를 처리하고,
        /// 커스텀 테마인 경우 색상 오버라이드를 적용한다.
        /// </summary>
        private void ApplyTheme(string theme)
        {
            bool isCustom = _customThemes.Contains(theme);

            if (this.Content is FrameworkElement root)
            {
                var targetTheme = theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ when isCustom && theme == "solarized-light" => ElementTheme.Light,
                    _ when isCustom => ElementTheme.Dark, // 커스텀 테마는 Dark 기반
                    _ => ElementTheme.Default
                };

                // 커스텀 테마: 리소스 설정 후 테마 토글로 {ThemeResource} 바인딩 강제 갱신
                if (isCustom)
                {
                    bool isLightCustom = theme == "solarized-light";
                    // 1) 반대 테마로 전환하여 기존 리소스 해제
                    root.RequestedTheme = isLightCustom ? ElementTheme.Dark : ElementTheme.Light;
                    // 2) 커스텀 리소스 오버라이드 적용
                    ApplyCustomThemeOverrides(root, theme);
                    // 3) 대상 테마로 복귀 → 모든 {ThemeResource} 바인딩 재평가
                    root.RequestedTheme = isLightCustom ? ElementTheme.Light : ElementTheme.Dark;
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

            // PathHighlight 캐시 무효화 (테마 색상 변경 반영)
            ViewModels.FileSystemViewModel.InvalidatePathHighlightCache();

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

            // DWM 윈도우 보더 색상 → 테마 배경색에 맞춰 최대화 시 흰색 라인 방지
            UpdateDwmBorderColor(theme, isCustom);
        }

        /// <summary>
        /// DWM 윈도우 프레임 보더 색상을 현재 테마 배경에 맞춘다.
        /// 최대화 시 1px 흰색 라인이 보이는 WinUI 3 이슈를 방지한다.
        /// </summary>
        private void UpdateDwmBorderColor(string theme, bool isCustom)
        {
            if (_hwnd == IntPtr.Zero) return;

            Windows.UI.Color bgColor;
            if (isCustom)
            {
                var p = GetThemePalette(theme);
                bgColor = p.bgMica;
            }
            else
            {
                bool isLight = theme == "light" ||
                               (theme == "system" && App.Current.RequestedTheme == ApplicationTheme.Light);
                bgColor = isLight
                    ? Windows.UI.Color.FromArgb(255, 243, 243, 243)   // #F3F3F3
                    : Windows.UI.Color.FromArgb(255, 32, 32, 32);     // #202020
            }

            // COLORREF = 0x00BBGGRR (BGR 순서)
            int colorRef = bgColor.R | (bgColor.G << 8) | (bgColor.B << 16);
            Helpers.NativeMethods.DwmSetWindowAttribute(
                _hwnd, Helpers.NativeMethods.DWMWA_BORDER_COLOR, ref colorRef, sizeof(int));
            // 캡션(타이틀바) 색상도 동일하게 설정 — 최대화 시 상단 흰색 라인 방지
            Helpers.NativeMethods.DwmSetWindowAttribute(
                _hwnd, Helpers.NativeMethods.DWMWA_CAPTION_COLOR, ref colorRef, sizeof(int));
        }

        private void ApplyCustomThemeOverrides(FrameworkElement root, string theme)
        {
            if (!_customThemes.Contains(theme))
            {
                // root 레벨 Dark/Light 오버라이드 제거 → App.xaml 원본 dict가 자동 적용
                root.Resources.ThemeDictionaries.Remove("Dark");
                root.Resources.ThemeDictionaries.Remove("Light");
                return;
            }

            var p = GetThemePalette(theme);

            // 커스텀 오버라이드만 설정 (미설정 키는 App.xaml Dark dict에서 fallback)
            var darkDict = new ResourceDictionary();

            // Color 리소스
            darkDict["SpanBgMica"] = p.bgMica;
            darkDict["SpanBgLayer1"] = p.bgLayer1;
            darkDict["SpanBgLayer2"] = p.bgLayer2;
            darkDict["SpanBgLayer3"] = p.bgLayer3;
            darkDict["SpanAccent"] = p.accent;
            darkDict["SpanAccentHover"] = p.accentHover;
            darkDict["SpanTextPrimary"] = p.textPri;
            darkDict["SpanTextSecondary"] = p.textSec;
            darkDict["SpanTextTertiary"] = p.textTer;
            darkDict["SpanBgSelected"] = p.bgSel;
            darkDict["SpanBorderSubtle"] = p.border;

            // Brush 리소스
            darkDict["SpanBgMicaBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgMica);
            darkDict["SpanBgLayer1Brush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer1);
            darkDict["SpanBgLayer2Brush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer2);
            darkDict["SpanBgLayer3Brush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgLayer3);
            darkDict["SpanAccentBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accent);
            darkDict["SpanAccentHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.accentHover);
            darkDict["SpanTextPrimaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textPri);
            darkDict["SpanTextSecondaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textSec);
            darkDict["SpanTextTertiaryBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.textTer);
            darkDict["SpanBgSelectedBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.bgSel);
            darkDict["SpanBorderSubtleBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(p.border);

            // AccentDim = accent 색상에 70% 투명도 (탭/밀러컬럼 테두리용)
            var accentDim = Windows.UI.Color.FromArgb(0xB3, p.accent.R, p.accent.G, p.accent.B);
            darkDict["SpanAccentDimColor"] = accentDim;
            darkDict["SpanAccentDimBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentDim);

            // Accent-tinted selection (Windows Explorer 스타일 통일)
            var accentHover = Windows.UI.Color.FromArgb(0x0F, p.accent.R, p.accent.G, p.accent.B);
            var accentActive = Windows.UI.Color.FromArgb(0x1A, p.accent.R, p.accent.G, p.accent.B);
            var accentSelected = Windows.UI.Color.FromArgb(0x25, p.accent.R, p.accent.G, p.accent.B);
            var accentSelHover = Windows.UI.Color.FromArgb(0x30, p.accent.R, p.accent.G, p.accent.B);
            var pathHighlight = Windows.UI.Color.FromArgb(0x20, p.accent.R, p.accent.G, p.accent.B);
            darkDict["SpanBgHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentHover);
            darkDict["SpanBgActiveBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentActive);
            darkDict["SpanBgSelectedBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelected);
            darkDict["SpanBgSelectedHoverBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelHover);
            darkDict["SpanPathHighlightBrush"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(pathHighlight);

            // ListView/GridView 선택 색상 (accent 기반 통일)
            darkDict["ListViewItemBackgroundSelected"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelected);
            darkDict["ListViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelHover);
            darkDict["ListViewItemBackgroundSelectedPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentActive);
            darkDict["GridViewItemBackgroundSelected"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelected);
            darkDict["GridViewItemBackgroundSelectedPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentSelHover);
            darkDict["GridViewItemBackgroundSelectedPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(accentActive);

            var dictKey = theme == "solarized-light" ? "Light" : "Dark";
            root.Resources.ThemeDictionaries[dictKey] = darkDict;
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
            "nord" => (
                Clr("#2e3440"), Clr("#3b4252"), Clr("#434c5e"), Clr("#4c566a"),
                Clr("#88c0d0"), Clr("#81a1c1"),
                Clr("#d8dee9"), Clr("#e5e9f0"), Clr("#4c566a"),
                Clr("#4D88c0d0"), Clr("#334c566a"),
                Clr("#9988c0d0"), Clr("#B388c0d0"), Clr("#8088c0d0")
            ),
            "onedark" => (
                Clr("#21252b"), Clr("#282c34"), Clr("#2c313a"), Clr("#3e4451"),
                Clr("#61afef"), Clr("#c678dd"),
                Clr("#abb2bf"), Clr("#5c6370"), Clr("#4b5263"),
                Clr("#4D61afef"), Clr("#333e4451"),
                Clr("#9961afef"), Clr("#B361afef"), Clr("#8061afef")
            ),
            "monokai" => (
                Clr("#1e1f1c"), Clr("#272822"), Clr("#2d2e2a"), Clr("#49483e"),
                Clr("#f92672"), Clr("#a6e22e"),
                Clr("#f8f8f2"), Clr("#a59f85"), Clr("#75715e"),
                Clr("#4Df92672"), Clr("#33f8f8f2"),
                Clr("#99f92672"), Clr("#B3f92672"), Clr("#80f92672")
            ),
            "solarized-light" => (
                Clr("#fdf6e3"), Clr("#eee8d5"), Clr("#fdf6e3"), Clr("#d3cbb7"),
                Clr("#268bd2"), Clr("#2aa198"),
                Clr("#586e75"), Clr("#657b83"), Clr("#93a1a1"),
                Clr("#4D268bd2"), Clr("#33586e75"),
                Clr("#99268bd2"), Clr("#B3268bd2"), Clr("#80268bd2")
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
            "nord" => (
                Clr("#d8dee9"), Clr("#88c0d0"), Clr("#2688c0d0"),
                Clr("#81a1c1"), Clr("#4088c0d0"), Clr("#4c566a")
            ),
            "onedark" => (
                Clr("#abb2bf"), Clr("#61afef"), Clr("#2661afef"),
                Clr("#c678dd"), Clr("#4061afef"), Clr("#5c6370")
            ),
            "monokai" => (
                Clr("#f8f8f2"), Clr("#f92672"), Clr("#26f92672"),
                Clr("#a6e22e"), Clr("#40f92672"), Clr("#75715e")
            ),
            "solarized-light" => (
                Clr("#586e75"), Clr("#268bd2"), Clr("#26268bd2"),
                Clr("#2aa198"), Clr("#40268bd2"), Clr("#93a1a1")
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

                case "IconFontScale":
                    DispatcherQueue.TryEnqueue(() => ApplyIconFontScale(value as string ?? "0"));
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
                        var lang = value as string ?? "system";
                        _loc.Language = lang;
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

        /// <summary>
        /// 폰트 패밀리를 모든 뷰에 적용한다.
        /// </summary>
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

        /// <summary>
        /// 밀도 설정(Compact/Standard/Comfortable)을 모든 뷰에 적용한다.
        /// </summary>
        private void ApplyDensity(string density)
        {
            // 숫자 문자열(0~6) 또는 레거시 이름 지원
            int level = density switch
            {
                "compact" => 0,
                "comfortable" => 2,
                "spacious" => 4,
                _ => int.TryParse(density, out var n) ? Math.Clamp(n, 0, 5) : 2
            };

            _densityPadding = new Thickness(12, level, 12, level);
            _densityMinHeight = 20.0 + level;

            var densityStr = level.ToString();

            // Apply to all visible Miller Column ListViews
            foreach (var kvp in _tabMillerPanels)
                ApplyDensityToItemsControl(kvp.Value.items);
            ApplyDensityToItemsControl(MillerColumnsControlRight);

            // Apply to Details/List/Icon views via their public methods
            foreach (var kvp in _tabDetailsPanels)
                kvp.Value.ApplyDensity(densityStr);
            foreach (var kvp in _tabListPanels)
                kvp.Value.ApplyDensity(densityStr);
            foreach (var kvp in _tabIconPanels)
                kvp.Value.ApplyDensity(densityStr);
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
                            if (grid != null)
                            {
                                grid.Padding = _densityPadding;
                                grid.MinHeight = _densityMinHeight;
                            }
                        }
                    }
                }
            }
        }

        // =================================================================
        //  #region Icon & Font Scale
        // =================================================================

        private int _iconFontScaleLevel = 0;

        /// <summary>
        /// 각 UI 요소의 원래(XAML 기본) FontSize를 저장하는 약한 참조 테이블.
        /// 절대값 기반 스케일링의 핵심: baseline + level = target FontSize.
        /// 요소가 GC되면 자동 정리됨.
        /// </summary>
        internal static readonly ConditionalWeakTable<DependencyObject, double[]> BaselineFontSizes = new();

        /// <summary>
        /// 아이콘/폰트 스케일(0~5)을 사이드바, 밀러, 리스트/상세 뷰에 적용한다.
        /// 레벨 0 = 기본 크기(아이콘 16px, 텍스트 13px), 각 레벨 +1px.
        /// </summary>
        private void ApplyIconFontScale(string scale)
        {
            _iconFontScaleLevel = int.TryParse(scale, out var n) ? Math.Clamp(n, 0, 5) : 0;

            double itemFont = 13.0 + _iconFontScaleLevel;
            double iconFont = 16.0 + _iconFontScaleLevel;

            // Sidebar width scaling (base 200 + level * 6)
            double sidebarWidth = 200 + _iconFontScaleLevel * 6;
            if (!_sidebarHiddenForSpecialMode)
                SidebarCol.Width = new GridLength(sidebarWidth);
            else
                _savedSidebarWidth = sidebarWidth; // Settings 모드 해제 시 복원될 값 갱신

            // Sidebar font/icon
            ApplyIconFontScaleToSidebar(itemFont, iconFont);

            // Miller columns
            foreach (var kvp in _tabMillerPanels)
                ApplyIconFontScaleToMillerControl(kvp.Value.items, itemFont, iconFont);
            ApplyIconFontScaleToMillerControl(MillerColumnsControlRight, itemFont, iconFont);

            // Details / List views
            var scaleStr = _iconFontScaleLevel.ToString();
            foreach (var kvp in _tabDetailsPanels)
                kvp.Value.ApplyIconFontScale(scaleStr);
            foreach (var kvp in _tabListPanels)
                kvp.Value.ApplyIconFontScale(scaleStr);

            // Global UI (toolbar, tab bar, status bar)
            ApplyIconFontScaleToGlobalUI(_iconFontScaleLevel);

            // Settings page — Collapsed 상태에선 VisualTree 순회 불가하므로 Visible일 때만 적용
            if (SettingsView.Visibility == Visibility.Visible)
                SettingsView.ApplyIconFontScale(_iconFontScaleLevel);

            // Home page — 동일하게 Visible일 때만 적용
            if (HomeView.Visibility == Visibility.Visible)
                HomeView.ApplyIconFontScale(_iconFontScaleLevel);

            // Address bars — ScaleLevel 설정으로 ElementPrepared 자동 스케일 + 기존 element 즉시 적용
            MainAddressBar.ScaleLevel = _iconFontScaleLevel;
            LeftAddressBar.ScaleLevel = _iconFontScaleLevel;
            RightAddressBar.ScaleLevel = _iconFontScaleLevel;

            // Sidebar width: reset custom width on scale change
            try
            {
                var appSettings = Windows.Storage.ApplicationData.Current.LocalSettings;
                appSettings.Values.Remove("CustomSidebarWidth");
            }
            catch { }
        }

        private void ApplyIconFontScaleToSidebar(double itemFont, double iconFont)
        {
            // Home item
            if (SidebarHomeText != null) SidebarHomeText.FontSize = itemFont;
            var homeGrid = SidebarHomeText?.Parent as Grid;
            if (homeGrid != null)
            {
                var homeIcon = FindChild<FontIcon>(homeGrid);
                if (homeIcon != null) homeIcon.FontSize = iconFont;
            }

            // Favorites list
            ApplyIconFontScaleToItemsControl(FavoritesFlatList, itemFont, iconFont);

            // Drive sections (Local, Cloud, Network) — find ItemsControls in sidebar
            var sidebarScroll = FindChild<ScrollViewer>(SidebarBorder);
            if (sidebarScroll?.Content is StackPanel sidebarStack)
            {
                foreach (var child in sidebarStack.Children)
                {
                    // Direct ItemsControls (local drives)
                    if (child is ItemsControl ic && child is not Microsoft.UI.Xaml.Controls.ListView)
                        ApplyIconFontScaleToItemsControl(ic, itemFont, iconFont);

                    // StackPanels wrapping cloud/network sections
                    if (child is StackPanel sp)
                    {
                        foreach (var spChild in sp.Children)
                        {
                            if (spChild is ItemsControl sic && spChild is not Microsoft.UI.Xaml.Controls.ListView)
                                ApplyIconFontScaleToItemsControl(sic, itemFont, iconFont);
                        }
                    }
                }
            }
        }

        private void ApplyIconFontScaleToItemsControl(ItemsControl? itemsControl, double itemFont, double iconFont)
        {
            if (itemsControl?.ItemsPanelRoot == null) return;
            foreach (var container in itemsControl.ItemsPanelRoot.Children)
            {
                ApplyIconFontScaleToContainer(container, itemFont, iconFont);
            }
        }

        private void ApplyIconFontScaleToMillerControl(ItemsControl? millerControl, double itemFont, double iconFont)
        {
            if (millerControl?.ItemsPanelRoot == null) return;
            double columnWidth = 220 + _iconFontScaleLevel * 6;
            foreach (var columnContainer in millerControl.ItemsPanelRoot.Children)
            {
                // ItemsControl + ItemTemplate → ContentPresenter가 DataTemplate 루트를 래핑
                Grid? columnGrid = columnContainer as Grid
                    ?? FindChild<Grid>(columnContainer);
                if (columnGrid != null && columnGrid.Width >= 220 && columnGrid.Width <= 250)
                    columnGrid.Width = columnWidth;

                // Miller 컬럼 내부의 ListView 찾기 (ListView 타입 명시 — x:Name "ListView" 필드와 충돌 방지)
                var listView = FindChild<Microsoft.UI.Xaml.Controls.ListView>(columnContainer);
                if (listView?.ItemsPanelRoot == null) continue;
                for (int i = 0; i < listView.Items.Count; i++)
                {
                    if (listView.ContainerFromIndex(i) is ListViewItem item)
                    {
                        // ListViewItem → ContentPresenter → DataTemplate Grid 경로 사용
                        var cp = FindChild<ContentPresenter>(item);
                        if (cp != null)
                        {
                            var grid = FindChild<Grid>(cp);
                            if (grid != null)
                                ApplyScaleToTemplateGrid(grid, itemFont, iconFont);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// DataTemplate의 루트 Grid에서 텍스트(13~18px)와 아이콘(16~21px) 크기를 조정한다.
        /// RemixIcons 폰트를 사용하는 TextBlock은 아이콘으로 취급하여 iconFont를 적용한다.
        /// ContentPresenter를 통해 찾은 Grid에만 적용하여 WinUI 내부 Grid와 혼동 방지.
        /// </summary>
        private static void ApplyScaleToTemplateGrid(Grid grid, double itemFont, double iconFont)
        {
            for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(grid); i++)
            {
                var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(grid, i);
                if (child is TextBlock tb)
                {
                    // RemixIcons 폰트 사용 TextBlock은 아이콘 → iconFont 적용
                    bool isIcon = tb.FontFamily?.Source?.Contains("Remix") == true;
                    if (isIcon && tb.FontSize >= 13 && tb.FontSize <= 21)
                        tb.FontSize = iconFont;
                    else if (!isIcon && tb.FontSize >= 13 && tb.FontSize <= 18)
                        tb.FontSize = itemFont;
                }
                else if (child is FontIcon fi && fi.FontSize >= 16 && fi.FontSize <= 21)
                    fi.FontSize = iconFont;
                // Icon inside a nested Grid (e.g., file icon container Grid wrapping FontIcon)
                else if (child is Grid iconGrid)
                {
                    var nestedIcon = FindChild<FontIcon>(iconGrid);
                    if (nestedIcon != null && nestedIcon.FontSize >= 16 && nestedIcon.FontSize <= 21)
                        nestedIcon.FontSize = iconFont;
                    // 아이콘 Grid 크기도 스케일에 맞춰 조정 (기본 16x16)
                    if (iconGrid.Width >= 16 && iconGrid.Width <= 21)
                        iconGrid.Width = iconFont;
                    if (iconGrid.Height >= 16 && iconGrid.Height <= 21)
                        iconGrid.Height = iconFont;
                }
            }
        }

        /// <summary>
        /// Sidebar 등 비 ListView 컨테이너(ContentPresenter 직접 자식)에 스케일 적용.
        /// </summary>
        private static void ApplyIconFontScaleToContainer(DependencyObject container, double itemFont, double iconFont)
        {
            // ContentControl(ListViewItem 등): ContentPresenter 경유
            if (container is ContentControl)
            {
                var cp = FindChild<ContentPresenter>(container);
                if (cp != null)
                {
                    var grid = FindChild<Grid>(cp);
                    if (grid != null) { ApplyScaleToTemplateGrid(grid, itemFont, iconFont); return; }
                }
            }
            // ContentPresenter 또는 일반 요소: 직접 Grid 탐색 (사이드바 ItemsControl용)
            var directGrid = FindChild<Grid>(container);
            if (directGrid != null)
                ApplyScaleToTemplateGrid(directGrid, itemFont, iconFont);
        }

        /// <summary>
        /// 탭 바(Row 0), 툴바(Row 1), 상태바(Row 3)의 TextBlock/FontIcon/TextBox에
        /// 절대값 기반 폰트 스케일을 적용한다. baseline + level = 최종 FontSize.
        /// </summary>
        private void ApplyIconFontScaleToGlobalUI(int level)
        {
            // AppTitleBar (Tab bar, Row 0)
            if (AppTitleBar != null)
                ApplyAbsoluteScaleToTree(AppTitleBar, level, 8, 20, 10, 16);

            // "SPAN Finder" 타이틀 TextBlock: ConditionalWeakTable 순회에서 누락될 수 있으므로
            // baseline(12) 기준으로 직접 설정하여 스케일 전환 시 크기가 남아있는 버그 방지
            if (AppTitleText != null)
                AppTitleText.FontSize = Math.Max(7, 12.0 + level);

            // Row 1 (Toolbar) and Row 3 (StatusBar) — find from root Grid
            if (this.Content is Grid rootGrid)
            {
                foreach (var child in rootGrid.Children)
                {
                    if (child is FrameworkElement fe)
                    {
                        int row = Grid.GetRow(fe);
                        if (row == 1 || row == 3)
                            ApplyAbsoluteScaleToTree(fe, level, 8, 20, 10, 16);
                    }
                }
            }
        }

        /// <summary>
        /// 절대값 기반 폰트 스케일: 각 요소의 원래 FontSize(baseline)를 ConditionalWeakTable에 저장하고,
        /// baseline + level로 최종 FontSize를 설정한다.
        /// range 필터는 baseline 기준이므로 스케일 레벨 변경 후에도 정확하게 동작.
        /// </summary>
        /// <param name="parent">순회 시작점</param>
        /// <param name="level">스케일 레벨 (0~5)</param>
        /// <param name="tbMin">TextBlock/FontIcon baseline 최소값</param>
        /// <param name="tbMax">TextBlock/FontIcon baseline 최대값</param>
        /// <param name="tboxMin">TextBox baseline 최소값 (-1이면 TextBox 무시)</param>
        /// <param name="tboxMax">TextBox baseline 최대값</param>
        internal static void ApplyAbsoluteScaleToTree(
            DependencyObject parent, int level,
            double tbMin, double tbMax,
            double tboxMin = -1, double tboxMax = -1)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);

                // AddressBarControl은 자체 ScaleLevel 속성으로 스케일을 관리하므로 skip.
                // Global 순회가 내부까지 진입하면 이중 적용(double traversal) 버그 발생.
                if (child is Controls.AddressBarControl)
                    continue;

                if (child is TextBlock tb)
                {
                    if (!BaselineFontSizes.TryGetValue(tb, out var stored))
                    {
                        stored = new[] { tb.FontSize };
                        BaselineFontSizes.Add(tb, stored);
                    }
                    if (stored[0] >= tbMin && stored[0] <= tbMax)
                        tb.FontSize = Math.Max(7, stored[0] + level);
                }
                else if (child is FontIcon fi)
                {
                    if (!BaselineFontSizes.TryGetValue(fi, out var stored))
                    {
                        stored = new[] { fi.FontSize };
                        BaselineFontSizes.Add(fi, stored);
                    }
                    if (stored[0] >= tbMin && stored[0] <= tbMax)
                        fi.FontSize = Math.Max(7, stored[0] + level);
                }
                else if (tboxMin >= 0 && child is TextBox tbox)
                {
                    if (!BaselineFontSizes.TryGetValue(tbox, out var stored))
                    {
                        stored = new[] { tbox.FontSize };
                        BaselineFontSizes.Add(tbox, stored);
                    }
                    if (stored[0] >= tboxMin && stored[0] <= tboxMax)
                        tbox.FontSize = Math.Max(9, stored[0] + level);
                    // TextBox 내부로 재귀하지 않음: 내부 PlaceholderText TextBlock이
                    // 이미 스케일된 FontSize를 상속받아 baseline 오염 발생 방지.
                    // TextBox.FontSize 설정만으로 내부 요소에 자동 전파됨.
                    continue;
                }

                ApplyAbsoluteScaleToTree(child, level, tbMin, tbMax, tboxMin, tboxMax);
            }
        }

        // #endregion Icon & Font Scale

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

        /// <summary>
        /// 터미널 열기 처리. 현재 활성 경로에서 설정된 터미널 애플리케이션을 실행한다.
        /// </summary>
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

        /// <summary>
        /// 현재 활성 뷰를 새로고침한다.
        /// </summary>
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

        /// <summary>
        /// 단축키 도움말 오버레이를 토글한다.
        /// </summary>
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

        /// <summary>
        /// 설정 버튼 클릭 이벤트. 설정 탭을 열다.
        /// </summary>
        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            OpenSettingsTab();
        }

        private void OnLogClick(object sender, RoutedEventArgs e)
        {
            OpenLogTab();
        }

        /// <summary>
        /// 작업 로그 탭을 열거나 기존 탭으로 전환 (UI 연동 포함).
        /// </summary>
        private void OpenLogTab()
        {
            ViewModel.OpenOrSwitchToActionLogTab();
            ResubscribeLeftExplorer();
            UpdateViewModeVisibility();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, UpdateTitleBarRegions);
        }

        /// <summary>
        /// 작업 로그 탭을 닫고 이전 탭으로 복귀.
        /// 유일한 탭이면 Home 탭을 먼저 생성.
        /// </summary>
        private void CloseCurrentLogTab()
        {
            var tab = ViewModel.ActiveTab;
            if (tab == null || tab.ViewMode != ViewMode.ActionLog) return;

            int index = ViewModel.ActiveTabIndex;

            if (ViewModel.Tabs.Count <= 1)
            {
                ViewModel.AddNewTab();
                var newTab = ViewModel.ActiveTab;
                if (newTab != null)
                {
                    CreateMillerPanelForTab(newTab);
                    SwitchMillerPanel(newTab.Id);
                }
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
        /// ActionLog 탭을 탐색기 탭으로 변환한다.
        /// Explorer가 null인 ActionLog 탭에 ExplorerViewModel을 생성하고
        /// Miller 패널을 만들어 정상 탐색이 가능하도록 초기화한다.
        /// </summary>
        private void ConvertLogTabToExplorer()
        {
            var tab = ViewModel.ActiveTab;
            if (tab == null) return;

            // Explorer가 없으면 생성
            if (tab.Explorer == null)
            {
                var root = new Models.FolderItem { Name = "PC", Path = "PC" };
                var explorer = new ExplorerViewModel(root,
                    App.Current.Services.GetRequiredService<Services.FileSystemService>());
                tab.Explorer = explorer;

                // ViewModel의 LeftExplorer 갱신
                ViewModel.SetLeftExplorer(explorer);
            }

            // Miller 패널 생성 및 전환
            CreateMillerPanelForTab(tab);
            SwitchMillerPanel(tab.Id);
            ResubscribeLeftExplorer();
        }

        // #endregion Help Overlay, Settings/Log Button Handlers
    }
}
