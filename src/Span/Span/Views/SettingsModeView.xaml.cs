using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Helpers;
using Span.Services;
using System;
using Windows.Storage.Pickers;

namespace Span.Views;

/// <summary>
/// 설정 페이지 UserControl.
/// NavigationView 기반의 섹션별 설정 UI(General, Appearance, Browsing,
/// Tools, Advanced, About, OpenSource)를 제공한다.
/// 설정값을 SettingsService와 양방향 동기화하고, 다국어 UI를 지원한다.
/// </summary>
public sealed partial class SettingsModeView : UserControl
{
    private static readonly string[] FontOptions =
    [
        "Segoe UI Variable", "Arial", "Verdana", "Calibri",
        "Cascadia Code", "Consolas", "Courier New",
        "Malgun Gothic", "Microsoft YaHei UI", "Yu Gothic UI"
    ];

    private readonly ScrollViewer[] _sections;
    private readonly Services.SettingsService _settings;
    private LocalizationService? _loc;
    private DispatcherTimer? _updateTimer;
    private int _updateStage;
    private bool _isLoading = true;
    // 절대값 기반 스케일 (MainWindow.BaselineFontSizes 사용) — _previousScaleLevel 불필요

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독)
    /// </summary>
    public event EventHandler? BackRequested;

    public SettingsModeView()
    {
        this.InitializeComponent();

        _settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
        _sections = new ScrollViewer[]
        {
            GeneralSection,
            AppearanceSection,
            BrowsingSection,
            ToolsSection,
            AdvancedSection,
            AboutSection,
            OpenSourceSection
        };

        LoadSettingsToUI();
        WireEvents();

        _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
        LocalizeUI();
        if (_loc != null) _loc.LanguageChanged += LocalizeUI;
        this.Unloaded += (s, e) => { if (_loc != null) _loc.LanguageChanged -= LocalizeUI; };
    }

    /// <summary>
    /// 설정 페이지가 다시 표시될 때 최신 설정값으로 새로고침
    /// </summary>
    public void RefreshSettings()
    {
        LoadSettingsToUI();
    }

    // ── Back button ──

    private void OnBackClick(object sender, RoutedEventArgs e)
    {
        BackRequested?.Invoke(this, EventArgs.Empty);
    }

    // ── Load saved settings into UI controls ──

    private void LoadSettingsToUI()
    {
        _isLoading = true;
        try
        {
            // General
            var lang = _settings.Language;
            LanguageCombo.SelectedIndex = lang switch
            {
                "en" => 1,
                "ko" => 2,
                "ja" => 3,
                "zh-Hans" => 4,
                "zh-Hant" => 5,
                "de" => 6,
                "es" => 7,
                "fr" => 8,
                "pt-BR" => 9,
                _ => 0
            };

            // Per-tab startup behavior
            var tab1Startup = _settings.Tab1StartupBehavior;
            Tab1StartupHome.IsChecked = tab1Startup == 0;
            Tab1StartupRestore.IsChecked = tab1Startup == 1;
            Tab1StartupCustom.IsChecked = tab1Startup == 2;
            Tab1CustomPathBox.Text = _settings.Tab1StartupPath;

            var tab2Startup = _settings.Tab2StartupBehavior;
            Tab2StartupHome.IsChecked = tab2Startup == 0;
            Tab2StartupRestore.IsChecked = tab2Startup == 1;
            Tab2StartupCustom.IsChecked = tab2Startup == 2;
            Tab2CustomPathBox.Text = _settings.Tab2StartupPath;

            // Per-tab startup view mode
            Tab1ViewModeCombo.SelectedIndex = Math.Clamp(_settings.Tab1StartupViewMode, 0, 3);
            Tab2ViewModeCombo.SelectedIndex = Math.Clamp(_settings.Tab2StartupViewMode, 0, 3);

            // Default preview
            DefaultPreviewToggle.IsOn = _settings.DefaultPreviewEnabled;

            FavoritesTreeToggle.IsOn = _settings.ShowFavoritesTree;
            SystemTrayToggle.IsOn = _settings.MinimizeToTray;
            WindowPositionToggle.IsOn = _settings.RememberWindowPosition;

            // Appearance
            var theme = _settings.Theme;
            ThemeSystem.IsChecked = theme == "system";
            ThemeLight.IsChecked = theme == "light";
            ThemeDark.IsChecked = theme == "dark";
            ThemeDracula.IsChecked = theme == "dracula";
            ThemeTokyoNight.IsChecked = theme == "tokyonight";
            ThemeCatppuccin.IsChecked = theme == "catppuccin";
            ThemeGruvbox.IsChecked = theme == "gruvbox";
            ThemeSolarizedLight.IsChecked = theme == "solarized-light";
            ThemeNord.IsChecked = theme == "nord";
            ThemeOneDark.IsChecked = theme == "onedark";
            ThemeMonokai.IsChecked = theme == "monokai";

            // Density: 숫자(0~5) 또는 레거시 이름
            var density = _settings.Density;
            int densityLevel = density switch
            {
                "compact" => 0,
                "comfortable" => 2,
                "spacious" => 4,
                _ => int.TryParse(density, out var n) ? Math.Clamp(n, 0, 5) : 2
            };
            var densityButtons = new[] { Density0, Density1, Density2, Density3, Density4, Density5 };
            densityButtons[densityLevel].IsChecked = true;

            // Icon & Font Scale
            var iconFontScale = _settings.IconFontScale;
            int scaleLevel = int.TryParse(iconFontScale, out var sl) ? Math.Clamp(sl, 0, 5) : 0;
            var scaleButtons = new[] { Scale0, Scale1, Scale2, Scale3, Scale4, Scale5 };
            scaleButtons[scaleLevel].IsChecked = true;

            var iconPack = _settings.IconPack;
            IconPackCombo.SelectedIndex = iconPack switch
            {
                "phosphor" => 1,
                "tabler" => 2,
                _ => 0
            };

            var font = _settings.FontFamily;
            var fontIdx = Array.IndexOf(FontOptions, font);
            FontCombo.SelectedIndex = fontIdx >= 0 ? fontIdx : 0;

            // Browsing
            ShowHiddenToggle.IsOn = _settings.ShowHiddenFiles;
            ShowExtensionsToggle.IsOn = _settings.ShowFileExtensions;
            CheckboxToggle.IsOn = _settings.ShowCheckboxes;
            MillerClickCombo.SelectedIndex = _settings.MillerClickBehavior == "double" ? 1 : 0;
            ThumbnailToggle.IsOn = _settings.ShowThumbnails;
            QuickLookToggle.IsOn = _settings.EnableQuickLook;
            ConfirmDeleteToggle.IsOn = _settings.ConfirmDelete;

            var undoSize = _settings.UndoHistorySize;
            UndoHistoryCombo.SelectedIndex = undoSize switch
            {
                10 => 0,
                20 => 1,
                100 => 3,
                _ => 2
            };

            // Tools
            var terminal = _settings.DefaultTerminal;
            TerminalCombo.SelectedIndex = terminal switch
            {
                "powershell" => 1,
                "cmd" => 2,
                _ => 0
            };
            ShellExtrasToggle.IsOn = _settings.ShowWindowsShellExtras;
            ShellExtensionsToggle.IsOn = _settings.ShowShellExtensions;
            DeveloperMenuToggle.IsOn = _settings.ShowDeveloperMenu;
            GitIntegrationToggle.IsOn = _settings.ShowGitIntegration;
            HexPreviewToggle.IsOn = _settings.ShowHexPreview;
            CopilotMenuToggle.IsOn = _settings.ShowCopilotMenu;
            ContextMenuToggle.IsOn = _settings.ShowContextMenu;
            CrashReportToggle.IsOn = _settings.EnableCrashReporting;

            // Git 설치 상태 표시
            try
            {
                var gitSvc = App.Current.Services.GetService<Services.GitStatusService>();
                if (gitSvc != null && gitSvc.IsAvailable)
                {
                    GitVersionLabel.Text = string.Format(_loc?.Get("Settings_GitDetected") ?? "Git {0} detected", gitSvc.GitVersion);
                }
                else
                {
                    GitVersionLabel.Text = _loc?.Get("Settings_GitNotInstalled") ?? "Git is not installed";
                    GitIntegrationToggle.IsEnabled = false;
                }
            }
            catch
            {
                GitVersionLabel.Text = "";
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] LoadSettingsToUI error: {ex.Message}");
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ── Wire UI events to save settings ──

    private void WireEvents()
    {
        // Tab 1 startup behavior
        Tab1StartupHome.Checked += (s, e) => { if (!_isLoading) _settings.Tab1StartupBehavior = 0; };
        Tab1StartupRestore.Checked += (s, e) => { if (!_isLoading) _settings.Tab1StartupBehavior = 1; };
        Tab1StartupCustom.Checked += (s, e) => { if (!_isLoading) _settings.Tab1StartupBehavior = 2; };
        Tab1CustomPathBox.TextChanged += (s, e) => { if (!_isLoading) _settings.Tab1StartupPath = Tab1CustomPathBox.Text; };
        Tab1BrowseBtn.Click += async (s, e) => await BrowseFolder(Tab1CustomPathBox);

        // Tab 2 startup behavior
        Tab2StartupHome.Checked += (s, e) => { if (!_isLoading) _settings.Tab2StartupBehavior = 0; };
        Tab2StartupRestore.Checked += (s, e) => { if (!_isLoading) _settings.Tab2StartupBehavior = 1; };
        Tab2StartupCustom.Checked += (s, e) => { if (!_isLoading) _settings.Tab2StartupBehavior = 2; };
        Tab2CustomPathBox.TextChanged += (s, e) => { if (!_isLoading) _settings.Tab2StartupPath = Tab2CustomPathBox.Text; };
        Tab2BrowseBtn.Click += async (s, e) => await BrowseFolder(Tab2CustomPathBox);

        // Per-tab startup view mode
        Tab1ViewModeCombo.SelectionChanged += (s, e) => { if (!_isLoading) _settings.Tab1StartupViewMode = Tab1ViewModeCombo.SelectedIndex; };
        Tab2ViewModeCombo.SelectionChanged += (s, e) => { if (!_isLoading) _settings.Tab2StartupViewMode = Tab2ViewModeCombo.SelectedIndex; };

        // Default preview
        DefaultPreviewToggle.Toggled += (s, e) => { if (!_isLoading) _settings.DefaultPreviewEnabled = DefaultPreviewToggle.IsOn; };

        FavoritesTreeToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowFavoritesTree = FavoritesTreeToggle.IsOn; };
        SystemTrayToggle.Toggled += (s, e) => { if (!_isLoading) _settings.MinimizeToTray = SystemTrayToggle.IsOn; };
        WindowPositionToggle.Toggled += (s, e) => { if (!_isLoading) _settings.RememberWindowPosition = WindowPositionToggle.IsOn; };

        ThemeSystem.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "system"; };
        ThemeLight.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "light"; };
        ThemeDark.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "dark"; };
        ThemeDracula.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "dracula"; };
        ThemeTokyoNight.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "tokyonight"; };
        ThemeCatppuccin.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "catppuccin"; };
        ThemeGruvbox.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "gruvbox"; };
        ThemeSolarizedLight.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "solarized-light"; };
        ThemeNord.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "nord"; };
        ThemeOneDark.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "onedark"; };
        ThemeMonokai.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "monokai"; };

        Density0.Checked += (s, e) => { if (!_isLoading) _settings.Density = "0"; };
        Density1.Checked += (s, e) => { if (!_isLoading) _settings.Density = "1"; };
        Density2.Checked += (s, e) => { if (!_isLoading) _settings.Density = "2"; };
        Density3.Checked += (s, e) => { if (!_isLoading) _settings.Density = "3"; };
        Density4.Checked += (s, e) => { if (!_isLoading) _settings.Density = "4"; };
        Density5.Checked += (s, e) => { if (!_isLoading) _settings.Density = "5"; };

        Scale0.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "0"; };
        Scale1.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "1"; };
        Scale2.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "2"; };
        Scale3.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "3"; };
        Scale4.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "4"; };
        Scale5.Checked += (s, e) => { if (!_isLoading) _settings.IconFontScale = "5"; };

        IconPackCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            _settings.IconPack = IconPackCombo.SelectedIndex switch
            {
                1 => "phosphor",
                2 => "tabler",
                _ => "remix"
            };
            if (IconPackRestartNotice != null)
                IconPackRestartNotice.Visibility = Visibility.Visible;
        };

        FontCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            var idx = FontCombo.SelectedIndex;
            _settings.FontFamily = idx >= 0 && idx < FontOptions.Length ? FontOptions[idx] : FontOptions[0];
        };

        ShowHiddenToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowHiddenFiles = ShowHiddenToggle.IsOn; };
        ShowExtensionsToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowFileExtensions = ShowExtensionsToggle.IsOn; };
        CheckboxToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowCheckboxes = CheckboxToggle.IsOn; };
        ThumbnailToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowThumbnails = ThumbnailToggle.IsOn; };
        QuickLookToggle.Toggled += (s, e) => { if (!_isLoading) _settings.EnableQuickLook = QuickLookToggle.IsOn; };
        ConfirmDeleteToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ConfirmDelete = ConfirmDeleteToggle.IsOn; };

        MillerClickCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            _settings.MillerClickBehavior = MillerClickCombo.SelectedIndex == 1 ? "double" : "single";
        };

        UndoHistoryCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            _settings.UndoHistorySize = UndoHistoryCombo.SelectedIndex switch
            {
                0 => 10,
                1 => 20,
                3 => 100,
                _ => 50
            };
        };

        TerminalCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            _settings.DefaultTerminal = TerminalCombo.SelectedIndex switch
            {
                1 => "powershell",
                2 => "cmd",
                _ => "wt"
            };
        };
        ShellExtrasToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowWindowsShellExtras = ShellExtrasToggle.IsOn; };
        ShellExtensionsToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowShellExtensions = ShellExtensionsToggle.IsOn; };
        DeveloperMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowDeveloperMenu = DeveloperMenuToggle.IsOn; };
        GitIntegrationToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowGitIntegration = GitIntegrationToggle.IsOn; };
        HexPreviewToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowHexPreview = HexPreviewToggle.IsOn; };
        CopilotMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowCopilotMenu = CopilotMenuToggle.IsOn; };
        ContextMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowContextMenu = ContextMenuToggle.IsOn; };
        CrashReportToggle.Toggled += (s, e) => { if (!_isLoading) _settings.EnableCrashReporting = CrashReportToggle.IsOn; };

        // Hand cursor on all clickable card items
        foreach (var rb in new[] {
            ThemeSystem, ThemeLight, ThemeDark, ThemeDracula,
            ThemeTokyoNight, ThemeCatppuccin, ThemeGruvbox,
            ThemeSolarizedLight, ThemeNord, ThemeOneDark, ThemeMonokai,
            Tab1StartupHome, Tab1StartupRestore, Tab1StartupCustom,
            Tab2StartupHome, Tab2StartupRestore, Tab2StartupCustom,
            Density0, Density1, Density2, Density3, Density4, Density5,
            Scale0, Scale1, Scale2, Scale3, Scale4, Scale5 })
            Helpers.CursorHelper.SetHandCursor(rb);

        foreach (var toggle in new[] {
            FavoritesTreeToggle, SystemTrayToggle, WindowPositionToggle,
            ShellExtrasToggle, ShellExtensionsToggle, DeveloperMenuToggle, GitIntegrationToggle,
            HexPreviewToggle, CopilotMenuToggle, ContextMenuToggle, CrashReportToggle,
            DefaultPreviewToggle })
            Helpers.CursorHelper.SetHandCursor(toggle);

        Helpers.CursorHelper.SetHandCursor(IconPackCombo);
        Helpers.CursorHelper.SetHandCursor(LanguageCombo);
        Helpers.CursorHelper.SetHandCursor(TerminalCombo);
        Helpers.CursorHelper.SetHandCursor(Tab1ViewModeCombo);
        Helpers.CursorHelper.SetHandCursor(Tab2ViewModeCombo);
    }

    // ── Responsive layout ──

    private void SettingsNav_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        var mode = width < 500
            ? NavigationViewPaneDisplayMode.Top
            : NavigationViewPaneDisplayMode.Left;

        if (SettingsNav.PaneDisplayMode != mode)
        {
            SettingsNav.PaneDisplayMode = mode;
            SettingsNav.IsPaneOpen = true;
        }
    }

    // ── Navigation ──

    private void SettingsNav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is NavigationViewItem item && item.Tag is string tag)
        {
            ShowSection(tag);
        }
    }

    private void ShowSection(string tag)
    {
        foreach (var section in _sections)
            section.Visibility = Visibility.Collapsed;

        var target = tag switch
        {
            "General" => GeneralSection,
            "Appearance" => AppearanceSection,
            "Browsing" => BrowsingSection,
            "Tools" => ToolsSection,
            "Advanced" => AdvancedSection,
            "About" => AboutSection,
            "OpenSource" => OpenSourceSection,
            _ => GeneralSection
        };

        target.Visibility = Visibility.Visible;
    }

    // ── Language change restart notice ──

    private void LanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading) return;

        var lang = LanguageCombo.SelectedIndex switch
        {
            1 => "en",
            2 => "ko",
            3 => "ja",
            4 => "zh-Hans",
            5 => "zh-Hant",
            6 => "de",
            7 => "es",
            8 => "fr",
            9 => "pt-BR",
            _ => "system"
        };
        _settings.Language = lang;

        if (LangRestartNotice != null)
        {
            LangRestartNotice.Visibility = LanguageCombo.SelectedIndex != 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    // ── Localization ──

    private void LocalizeUI()
    {
        if (_loc == null) return;

        try
        {
            // Header
            SettingsTitle.Text = _loc.Get("Settings");
            // Navigation
            NavGeneral.Content = _loc.Get("Settings_General");
            NavAppearance.Content = _loc.Get("Settings_Appearance");
            NavBrowsing.Content = _loc.Get("Settings_Browsing");
            NavTools.Content = _loc.Get("Settings_Tools");
            NavAdvanced.Content = _loc.Get("Settings_Advanced");
            NavAbout.Content = _loc.Get("Settings_AboutNav");
            NavOpenSource.Content = _loc.Get("Settings_OpenSourceNav");

            // General
            GeneralTitle.Text = _loc.Get("Settings_General");
            LangLabel.Text = _loc.Get("Settings_Language");
            LangDesc.Text = _loc.Get("Settings_LanguageDesc");
            LangSystem.Content = _loc.Get("Settings_SystemDefault");
            LangRestartText.Text = _loc.Get("Settings_RestartNotice");
            StartupLabel.Text = _loc.Get("Settings_StartupBehavior");
            StartupDesc.Text = _loc.Get("Settings_StartupBehaviorDesc");
            // Tab 1
            Tab1Label.Text = _loc.Get("Settings_Tab1");
            Tab1HomeText.Text = _loc.Get("Settings_OpenHome");
            Tab1RestoreText.Text = _loc.Get("Settings_RestoreSession");
            Tab1CustomText.Text = _loc.Get("Settings_StartupPath");
            Tab1BrowseBtn.Content = "...";
            // Tab 2
            Tab2Label.Text = _loc.Get("Settings_Tab2");
            Tab2HomeText.Text = _loc.Get("Settings_OpenHome");
            Tab2RestoreText.Text = _loc.Get("Settings_RestoreSession");
            Tab2CustomText.Text = _loc.Get("Settings_StartupPath");
            Tab2BrowseBtn.Content = "...";
            // View mode (now inline within each explorer card)
            Tab1ViewLabel.Text = _loc.Get("Settings_StartupViewMode") + ":";
            Tab2ViewLabel.Text = _loc.Get("Settings_StartupViewMode") + ":";
            Tab1ViewMiller.Content = _loc.Get("Settings_ViewMiller");
            Tab1ViewDetails.Content = _loc.Get("Settings_ViewDetails");
            Tab1ViewList.Content = _loc.Get("Settings_ViewList");
            Tab1ViewIcon.Content = _loc.Get("Settings_ViewIcon");
            Tab2ViewMiller.Content = _loc.Get("Settings_ViewMiller");
            Tab2ViewDetails.Content = _loc.Get("Settings_ViewDetails");
            Tab2ViewList.Content = _loc.Get("Settings_ViewList");
            Tab2ViewIcon.Content = _loc.Get("Settings_ViewIcon");
            // WinUI 3: ComboBox 닫힌 상태에서 Item.Content 변경 시 표시 텍스트가 캐시되어 갱신 안 됨
            // SelectedIndex를 다시 설정해서 표시 텍스트 강제 갱신
            RefreshComboDisplay(Tab1ViewModeCombo);
            RefreshComboDisplay(Tab2ViewModeCombo);
            // Preview
            DefaultPreviewLabel.Text = _loc.Get("Settings_DefaultPreview");
            DefaultPreviewDesc.Text = _loc.Get("Settings_DefaultPreviewDesc");
            FavTreeLabel.Text = _loc.Get("Settings_FavoritesTree");
            FavTreeDesc.Text = _loc.Get("Settings_FavoritesTreeDesc");
            SysTrayLabel.Text = _loc.Get("Settings_SystemTray");
            SysTrayDesc.Text = _loc.Get("Settings_SystemTrayDesc");
            WinPosLabel.Text = _loc.Get("Settings_WindowPosition");
            WinPosDesc.Text = _loc.Get("Settings_WindowPositionDesc");

            // Appearance
            AppearanceTitle.Text = _loc.Get("Settings_Appearance");
            ThemeLabel.Text = _loc.Get("Settings_AppTheme");
            ThemeDesc.Text = _loc.Get("Settings_ThemeDesc");
            ThemeSystemText.Text = _loc.Get("Settings_System");
            ThemeLightText.Text = _loc.Get("Settings_Light");
            ThemeDarkText.Text = _loc.Get("Settings_Dark");
            DensityLabel.Text = _loc.Get("Settings_LayoutDensity");
            DensityDesc.Text = _loc.Get("Settings_LayoutDensityDesc");
            IconFontScaleLabel.Text = _loc.Get("Settings_IconFontScale");
            IconFontScaleDesc.Text = _loc.Get("Settings_IconFontScaleDesc");
            IconPackLabel.Text = _loc.Get("Settings_IconPack");
            IconPackDesc.Text = _loc.Get("Settings_IconPackDesc");
            IconPackRestartText.Text = _loc.Get("Settings_IconPackRestart");
            FontLabel.Text = _loc.Get("Settings_Font");
            FontDesc.Text = _loc.Get("Settings_FontDesc");
            // Custom themes
            CustomThemesLabel.Text = _loc.Get("Settings_CustomThemes");
            CustomThemesDesc.Text = _loc.Get("Settings_CustomThemesDesc");
            DraculaDescText.Text = _loc.Get("Theme_DraculaDesc");
            TokyoNightDescText.Text = _loc.Get("Theme_TokyoNightDesc");
            CatppuccinDescText.Text = _loc.Get("Theme_CatppuccinDesc");
            GruvboxDescText.Text = _loc.Get("Theme_GruvboxDesc");
            SolarizedLightDescText.Text = _loc.Get("Theme_SolarizedLightDesc");
            NordDescText.Text = _loc.Get("Theme_NordDesc");
            OneDarkDescText.Text = _loc.Get("Theme_OneDarkDesc");
            MonokaiDescText.Text = _loc.Get("Theme_MonokaiDesc");

            // Browsing
            BrowsingTitle.Text = _loc.Get("Settings_Browsing");
            ViewOptionsLabel.Text = _loc.Get("Settings_ViewOptions");
            ViewOptionsDesc.Text = _loc.Get("Settings_ViewOptionsDesc");
            ShowHiddenLabel.Text = _loc.Get("Settings_ShowHidden");
            ShowExtLabel.Text = _loc.Get("Settings_ShowExtensions");
            CheckboxLabel.Text = _loc.Get("Settings_CheckboxSelection");
            MillerLabel.Text = _loc.Get("Settings_MillerBehavior");
            MillerDesc.Text = _loc.Get("Settings_MillerBehaviorDesc");
            SingleClickItem.Content = _loc.Get("Settings_SingleClick");
            DoubleClickItem.Content = _loc.Get("Settings_DoubleClick");
            RefreshComboDisplay(MillerClickCombo);
            ThumbnailLabel.Text = _loc.Get("Settings_Thumbnails");
            ThumbnailDesc.Text = _loc.Get("Settings_ThumbnailsDesc");
            QuickLookLabel.Text = _loc.Get("Settings_QuickLook");
            QuickLookDesc.Text = _loc.Get("Settings_QuickLookDesc");
            DeleteConfirmLabel.Text = _loc.Get("Settings_DeleteConfirm");
            DeleteConfirmDesc.Text = _loc.Get("Settings_DeleteConfirmDesc");
            UndoLabel.Text = _loc.Get("Settings_UndoHistory");
            UndoDesc.Text = _loc.Get("Settings_UndoHistoryDesc");
            // Undo history items
            Undo10.Content = string.Format(_loc.Get("Settings_UndoCount"), 10);
            Undo20.Content = string.Format(_loc.Get("Settings_UndoCount"), 20);
            Undo50.Content = string.Format(_loc.Get("Settings_UndoCount"), 50);
            Undo100.Content = string.Format(_loc.Get("Settings_UndoCount"), 100);
            RefreshComboDisplay(UndoHistoryCombo);

            // Tools
            ToolsTitle.Text = _loc.Get("Settings_Tools");
            ShellExtLabel.Text = _loc.Get("Settings_ShellExtras");
            ShellExtDesc.Text = _loc.Get("Settings_ShellExtrasDesc");
            ShellExtensionsLabel.Text = _loc.Get("Settings_ShellExtensions");
            ShellExtensionsDesc.Text = _loc.Get("Settings_ShellExtensionsDesc");
            CopilotLabel.Text = _loc.Get("Settings_CopilotMenu");
            CopilotDesc.Text = _loc.Get("Settings_CopilotMenuDesc");
            CtxMenuLabel.Text = _loc.Get("Settings_ContextMenu");
            CtxMenuDesc.Text = _loc.Get("Settings_ContextMenuDesc");

            // Advanced
            AdvancedTitle.Text = _loc.Get("Settings_Advanced");
            TerminalLabel.Text = _loc.Get("Settings_TerminalApp");
            TerminalDesc.Text = _loc.Get("Settings_TerminalAppDesc");
            DevMenuLabel.Text = _loc.Get("Settings_DeveloperMenu");
            DevMenuDesc.Text = _loc.Get("Settings_DeveloperMenuDesc");
            CrashReportLabel.Text = _loc.Get("Settings_CrashReport");
            CrashReportDesc.Text = _loc.Get("Settings_CrashReportDesc");
            GitIntegrationLabel.Text = _loc.Get("Settings_GitIntegration");
            GitIntegrationDesc.Text = _loc.Get("Settings_GitIntegrationDesc");
            HexPreviewLabel.Text = _loc.Get("Settings_HexPreview");
            HexPreviewDesc.Text = _loc.Get("Settings_HexPreviewDesc");

            // About
            AboutTitle.Text = _loc.Get("Settings_AboutNav");
            CopyrightLabel.Text = "© 2026 LumiBear Studio. All rights reserved.";
            UpdateText.Text = _loc.Get("Settings_CheckUpdate");
            LinksLabel.Text = _loc.Get("Settings_Links");
            GitHubText.Text = _loc.Get("Settings_GitHub");
            BugReportText.Text = _loc.Get("Settings_BugReport");
            PrivacyText.Text = _loc.Get("Settings_Privacy");
            CoffeeLabel.Text = _loc.Get("Settings_BuyMeCoffee");
            CoffeeDesc.Text = _loc.Get("Settings_BuyMeCoffeeDesc");

            // Open Source
            OpenSourceTitle.Text = _loc.Get("Settings_OpenSourceNav");
            OpenSourceDesc.Text = _loc.Get("Settings_OpenSourceDesc");
            FullLicenseLink.Text = _loc.Get("Settings_FullLicenseLink");
            LibraryLabel.Text = _loc.Get("OpenSource_Libraries");
            IconFontLabel.Text = _loc.Get("OpenSource_IconFonts");
            DefaultIconPackText.Text = _loc.Get("OpenSource_DefaultPack");
            AvailableText1.Text = _loc.Get("OpenSource_Available");
            AvailableText2.Text = _loc.Get("OpenSource_Available");
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[SettingsModeView] LocalizeUI error: {ex.Message}");
        }
    }

    /// <summary>
    /// WinUI 3: ComboBox가 닫힌 상태에서 ComboBoxItem.Content를 변경하면
    /// 선택된 아이템의 표시 텍스트가 캐시되어 이전 언어 그대로 보이는 문제.
    /// SelectedIndex를 -1 → 원래값으로 재설정하여 표시 텍스트 강제 갱신.
    /// </summary>
    private void RefreshComboDisplay(ComboBox combo)
    {
        var idx = combo.SelectedIndex;
        if (idx < 0) return;
        var prev = _isLoading;
        _isLoading = true;
        combo.SelectedIndex = -1;
        combo.SelectedIndex = idx;
        _isLoading = prev;
    }

    // ── Update check animation ──

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateTimer != null) return;

        UpdateButton.IsEnabled = false;
        _updateStage = 0;

        UpdateIcon.Glyph = "\uE895";
        UpdateText.Text = _loc?.Get("Settings_Checking") ?? "Checking...";

        _updateTimer = new DispatcherTimer();
        _updateTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _updateTimer.Tick += UpdateTimer_Tick;
        _updateTimer.Start();
    }

    private void UpdateTimer_Tick(object? sender, object e)
    {
        try
        {
            _updateStage++;

            if (_updateStage == 1)
            {
                UpdateIcon.Glyph = "\uE73E";
                UpdateText.Text = _loc?.Get("Settings_UpToDate") ?? "Up to date";
                _updateTimer!.Interval = TimeSpan.FromMilliseconds(3000);
            }
            else
            {
                UpdateIcon.Glyph = "\uE72C";
                UpdateText.Text = _loc?.Get("Settings_CheckUpdate") ?? "Check for updates";
                UpdateButton.IsEnabled = true;
                _updateTimer!.Stop();
                _updateTimer = null;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] Timer error: {ex.Message}");
        }
    }

    // ── Folder browse helper ──

    private async System.Threading.Tasks.Task BrowseFolder(TextBox targetBox)
    {
        try
        {
            var picker = new FolderPicker();
            picker.SuggestedStartLocation = PickerLocationId.Desktop;
            picker.FileTypeFilter.Add("*");

            // WinUI 3: Initialize with window handle
            var windows = ((App)App.Current).GetRegisteredWindows();
            if (windows.Count == 0) return;
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(windows[0]);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null)
            {
                targetBox.Text = folder.Path;
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] BrowseFolder error: {ex.Message}");
        }
    }

    // ── Icon & Font Scale for Settings page ──

    /// <summary>
    /// 설정 페이지 내부의 TextBlock/FontIcon 크기를 스케일 레벨에 맞춰 조정한다.
    /// 절대값 기반: baseline + level = 최종 FontSize.
    /// ConditionalWeakTable에 원본 폰트 크기를 저장하므로 레벨 변경 순서에 무관하게 정확.
    /// </summary>
    public void ApplyIconFontScale(int level)
    {
        // Settings: TextBlock 8~24, FontIcon 10~24 범위의 baseline만 스케일
        // (40px 앱 아이콘은 자동 제외)
        MainWindow.ApplyAbsoluteScaleToTree(this, level, 8, 24);
    }
}
