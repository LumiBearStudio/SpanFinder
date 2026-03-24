using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Span.Helpers;
using Span.Services;
using System;
using System.Collections.Generic;
using System.Linq;
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
    private readonly Grid[] _navItems;
    private Grid? _selectedNavItem;
    private readonly Services.SettingsService _settings;
    private LocalizationService? _loc;
    private DispatcherTimer? _updateTimer;
    private int _updateStage;
    private bool _isLoading = true;
    // 절대값 기반 스케일 (MainWindow.BaselineFontSizes 사용) — _previousScaleLevel 불필요

    // Shortcuts editor state
    private Services.KeyBindingService? _keyBindingService;
    private Dictionary<string, List<string>>? _editingBindings;  // 편집 사본
    private Dictionary<string, List<string>>? _savedBindings;    // 마지막 저장 상태
    private bool _shortcutsLoaded;
    private string? _recordingCommandId;
    private ContentDialog? _recordingDialog;

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독)
    /// </summary>
    public event EventHandler? BackRequested;

    public SettingsModeView()
    {
        this.InitializeComponent();

        // Set version from Package manifest + auto-generated build date
        var v = Windows.ApplicationModel.Package.Current.Id.Version;
        VersionLabel.Text = $"v{v.Major}.{v.Minor}.{v.Build} (Build {BuildInfo.BuildDate})";

        _settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
        _sections = new ScrollViewer[]
        {
            GeneralSection,
            AppearanceSection,
            BrowsingSection,
            ToolsSection,
            ShortcutsSection,
            AdvancedSection,
            AboutSection,
            OpenSourceSection
        };
        _navItems = new Grid[]
        {
            NavGeneral, NavAppearance, NavBrowsing, NavTools, NavShortcuts,
            NavAdvanced, NavAbout, NavOpenSource
        };
        _selectedNavItem = NavGeneral;

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

            // Preview: show folder info
            PreviewFolderInfoToggle.IsOn = _settings.PreviewShowFolderInfo;

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

            // List view item width
            ListWidthSlider.Value = _settings.ListColumnWidth;
            ListWidthValue.Text = $"{_settings.ListColumnWidth}px";

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


        // Preview: show folder info
        PreviewFolderInfoToggle.Toggled += (s, e) => { if (!_isLoading) _settings.PreviewShowFolderInfo = PreviewFolderInfoToggle.IsOn; };

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

        // List view item width
        ListWidthSlider.ValueChanged += (s, e) =>
        {
            if (_isLoading) return;
            int width = (int)e.NewValue;
            _settings.ListColumnWidth = width;
            ListWidthValue.Text = $"{width}px";
        };

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
            DefaultPreviewToggle, PreviewFolderInfoToggle })
            Helpers.CursorHelper.SetHandCursor(toggle);

        Helpers.CursorHelper.SetHandCursor(IconPackCombo);
        Helpers.CursorHelper.SetHandCursor(LanguageCombo);
        Helpers.CursorHelper.SetHandCursor(TerminalCombo);
        Helpers.CursorHelper.SetHandCursor(Tab1ViewModeCombo);
        Helpers.CursorHelper.SetHandCursor(Tab2ViewModeCombo);
    }

    // ── 커스텀 사이드바 (탐색기 사이드바와 동일 패턴) ──

    private void OnNavItemTapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is Grid grid && grid.Tag is string tag)
        {
            SelectNavItem(grid);
            ShowSection(tag);
        }
    }

    private void SelectNavItem(Grid item)
    {
        // 이전 선택 해제
        if (_selectedNavItem != null)
            _selectedNavItem.Background = new SolidColorBrush(Colors.Transparent);

        // 새 선택 적용
        _selectedNavItem = item;
        item.Background = (Brush)Application.Current.Resources["SpanBgSelectedBrush"];
    }

    private void OnNavItemPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedNavItem)
            grid.Background = (Brush)Application.Current.Resources["SpanBgHoverBrush"];
    }

    private void OnNavItemPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (sender is Grid grid && grid != _selectedNavItem)
            grid.Background = new SolidColorBrush(Colors.Transparent);
    }

    private static void SetNavText(Grid grid, string text)
    {
        foreach (var child in grid.Children)
        {
            if (child is TextBlock tb && Grid.GetColumn(tb) == 1)
            {
                tb.Text = text;
                return;
            }
        }
    }

    private void ShowSection(string tag)
    {
        foreach (var section in _sections)
            section.Visibility = Visibility.Collapsed;

        ScrollViewer? target = tag switch
        {
            "General" => GeneralSection,
            "Appearance" => AppearanceSection,
            "Browsing" => BrowsingSection,
            "Tools" => ToolsSection,
            "Shortcuts" => ShortcutsSection,
            "Advanced" => AdvancedSection,
            "About" => AboutSection,
            "OpenSource" => OpenSourceSection,
            _ => GeneralSection
        };

        target.Visibility = Visibility.Visible;

        if (tag == "Shortcuts")
            LoadShortcutsSection();
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
            // Navigation (커스텀 Grid 사이드바)
            SetNavText(NavGeneral, _loc.Get("Settings_General"));
            SetNavText(NavAppearance, _loc.Get("Settings_Appearance"));
            SetNavText(NavBrowsing, _loc.Get("Settings_Browsing"));
            SetNavText(NavTools, _loc.Get("Settings_Tools"));
            SetNavText(NavShortcuts, _loc.Get("Settings_Shortcuts") ?? "단축키");
            SetNavText(NavAdvanced, _loc.Get("Settings_Advanced"));
            SetNavText(NavAbout, _loc.Get("Settings_AboutNav"));
            SetNavText(NavOpenSource, _loc.Get("Settings_OpenSourceNav"));

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
            ListWidthLabel.Text = _loc.Get("Settings_ListWidth") ?? "List view item width";
            ListWidthDesc.Text = _loc.Get("Settings_ListWidthDesc") ?? "Width of each item in list view mode";
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

            // Shortcuts
            ShortcutsTitle.Text = _loc.Get("Settings_Shortcuts") ?? "단축키";
            ShortcutsResetAllBtn.Content = _loc.Get("Settings_ResetAll") ?? "초기화";
            ShortcutsCancelBtn.Content = _loc.Get("Common_Cancel") ?? "취소";
            ShortcutsSaveBtn.Content = _loc.Get("Common_Save") ?? "저장";

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

    // ── Shortcuts Section ──

    private void LoadShortcutsSection()
    {
        if (_shortcutsLoaded) return;
        _keyBindingService = App.Current.Services.GetService<Services.KeyBindingService>();
        if (_keyBindingService == null) return;

        _savedBindings = _keyBindingService.CloneCurrentBindings();
        _editingBindings = _keyBindingService.CloneCurrentBindings();
        _shortcutsLoaded = true;

        RebuildShortcutItemsUI();
    }

    private void RebuildShortcutItemsUI()
    {
        if (ShortcutItemsPanel == null || _editingBindings == null) return;
        ShortcutItemsPanel.Children.Clear();

        var categories = Models.ShortcutCommands.GetAllCategories();

        foreach (var category in categories)
        {
            // 카테고리 헤더
            var header = new TextBlock
            {
                Text = _loc?.Get($"Shortcuts_{category}") ?? category,
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Foreground = (Brush)Application.Current.Resources["SpanTextPrimaryBrush"],
                Margin = new Thickness(0, 16, 0, 8)
            };
            ShortcutItemsPanel.Children.Add(header);

            // 해당 카테고리의 커맨드 목록
            var commands = Models.ShortcutCommands.GetCommandsByCategory(category);

            foreach (var commandId in commands)
            {
                var row = CreateShortcutRow(commandId);
                ShortcutItemsPanel.Children.Add(row);
            }
        }

        UpdateSaveButtonState();
    }

    private Grid CreateShortcutRow(string commandId)
    {
        var grid = new Grid { Height = 36, Padding = new Thickness(12, 0, 12, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // 명령 이름
        var isModified = IsBindingModified(commandId);
        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        if (isModified)
        {
            var dot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = (Brush)Application.Current.Resources["SpanAccentBrush"],
                VerticalAlignment = VerticalAlignment.Center
            };
            namePanel.Children.Add(dot);
        }
        namePanel.Children.Add(new TextBlock
        {
            Text = Models.ShortcutCommands.GetDisplayName(commandId),
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumn(namePanel, 0);
        grid.Children.Add(namePanel);

        // 키 배지 패널
        var keysPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        var keys = _editingBindings?.ContainsKey(commandId) == true ? _editingBindings[commandId] : new List<string>();

        foreach (var keyStr in keys)
        {
            var badge = CreateKeyBadge(keyStr, commandId);
            keysPanel.Children.Add(badge);
        }

        // [+] 추가 버튼
        var addBtn = new Button
        {
            Content = "+",
            FontSize = 11,
            Padding = new Thickness(6, 2, 6, 2),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Tag = commandId
        };
        ToolTipService.SetToolTip(addBtn, _loc?.Get("Settings_ShortcutsAddKey") ?? "단축키 추가");
        addBtn.Click += OnAddKeyClick;
        keysPanel.Children.Add(addBtn);

        Grid.SetColumn(keysPanel, 1);
        grid.Children.Add(keysPanel);

        // 리셋 버튼
        var resetBtn = new Button
        {
            Content = "\u21BA",  // ↺
            FontSize = 14,
            Padding = new Thickness(4),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Tag = commandId,
            Margin = new Thickness(8, 0, 0, 0)
        };
        ToolTipService.SetToolTip(resetBtn, _loc?.Get("Settings_ShortcutsResetOne") ?? "기본값으로 리셋");
        resetBtn.Click += OnShortcutResetOne;
        Grid.SetColumn(resetBtn, 2);
        grid.Children.Add(resetBtn);

        return grid;
    }

    private Border CreateKeyBadge(string keyString, string commandId)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text = keyString,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });

        // x 제거 버튼
        var removeBtn = new Button
        {
            Content = "\u00D7",  // ×
            FontSize = 9,
            Padding = new Thickness(2, 0, 2, 0),
            MinWidth = 0,
            MinHeight = 0,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Tag = $"{commandId}|{keyString}"
        };
        removeBtn.Click += OnRemoveKeyClick;
        panel.Children.Add(removeBtn);

        var badge = new Border
        {
            Child = panel,
            Background = (Brush)Application.Current.Resources["SpanBgLayer2Brush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 4, 3)
        };
        return badge;
    }

    // ── Shortcut event handlers ──

    private async void OnAddKeyClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var btn = sender as Button;
            var commandId = btn?.Tag as string;
            if (commandId == null || _keyBindingService == null) return;

            var dialog = new ContentDialog
            {
                Title = _loc?.Get("Settings_ShortcutsRecord") ?? "단축키 입력",
                Content = CreateKeyRecorderContent(commandId),
                CloseButtonText = _loc?.Get("Cancel") ?? "취소",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.None
            };

            _recordingCommandId = commandId;
            _recordingDialog = dialog;

            // MainWindow의 글로벌 키보드 핸들러 억제
            var windows = ((App)App.Current).GetRegisteredWindows();
            foreach (var w in windows)
            {
                if (w is MainWindow mw)
                    mw._isRecordingShortcut = true;
            }

            await dialog.ShowAsync();

            foreach (var w in windows)
            {
                if (w is MainWindow mw)
                    mw._isRecordingShortcut = false;
            }
            _recordingCommandId = null;
            _recordingDialog = null;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] OnAddKeyClick error: {ex.Message}");
        }
    }

    private void OnRemoveKeyClick(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var tag = btn?.Tag as string;
        if (tag == null || _editingBindings == null) return;

        var parts = tag.Split('|', 2);
        if (parts.Length != 2) return;
        var commandId = parts[0];
        var keyString = parts[1];

        if (_editingBindings.ContainsKey(commandId))
            _editingBindings[commandId].Remove(keyString);

        RebuildShortcutItemsUI();
    }

    private void OnShortcutResetOne(object sender, RoutedEventArgs e)
    {
        var btn = sender as Button;
        var commandId = btn?.Tag as string;
        if (commandId == null || _keyBindingService == null || _editingBindings == null) return;

        var defaults = _keyBindingService.GetDefaultBindings();
        if (defaults.ContainsKey(commandId))
            _editingBindings[commandId] = new List<string>(defaults[commandId]);
        else
            _editingBindings.Remove(commandId);

        RebuildShortcutItemsUI();
    }

    private async void OnShortcutsResetAll(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = _loc?.Get("Settings_ShortcutsResetAllTitle") ?? "단축키 초기화",
                Content = _loc?.Get("Settings_ShortcutsResetAllContent") ?? "모든 단축키를 기본값으로 초기화하시겠습니까?",
                PrimaryButtonText = _loc?.Get("OK") ?? "확인",
                CloseButtonText = _loc?.Get("Cancel") ?? "취소",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _editingBindings = _keyBindingService?.GetDefaultBindings();
            RebuildShortcutItemsUI();
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] OnShortcutsResetAll error: {ex.Message}");
        }
    }

    private async void OnShortcutsCancel(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!HasShortcutChanges()) return;

            var dialog = new ContentDialog
            {
                Title = _loc?.Get("Settings_ShortcutsCancelTitle") ?? "변경사항 버리기",
                Content = _loc?.Get("Settings_ShortcutsCancelContent") ?? "저장하지 않은 변경사항을 버리시겠습니까?",
                PrimaryButtonText = _loc?.Get("Discard") ?? "버리기",
                CloseButtonText = _loc?.Get("Cancel") ?? "취소",
                XamlRoot = this.XamlRoot,
                DefaultButton = ContentDialogButton.Close
            };
            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary) return;

            _editingBindings = _savedBindings != null
                ? new Dictionary<string, List<string>>(_savedBindings.ToDictionary(k => k.Key, v => new List<string>(v.Value)))
                : _keyBindingService?.CloneCurrentBindings();
            RebuildShortcutItemsUI();
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[SettingsModeView] OnShortcutsCancel error: {ex.Message}");
        }
    }

    private void OnShortcutsSave(object sender, RoutedEventArgs e)
    {
        if (_keyBindingService == null || _editingBindings == null) return;

        _keyBindingService.ApplyAndSave(_editingBindings);
        _savedBindings = _keyBindingService.CloneCurrentBindings();

        RebuildShortcutItemsUI();

        // Toast 표시 — MainWindow에 직접 접근
        try
        {
            var windows = ((App)App.Current).GetRegisteredWindows();
            if (windows.Count > 0 && windows[0] is MainWindow mw)
                mw.ViewModel?.ShowToast(_loc?.Get("Settings_ShortcutsSaved") ?? "단축키가 저장되었습니다");
        }
        catch { /* 토스트 실패 무시 */ }
    }

    // ── Shortcut utility methods ──

    private bool HasShortcutChanges()
    {
        if (_editingBindings == null || _savedBindings == null) return false;
        var editJson = System.Text.Json.JsonSerializer.Serialize(_editingBindings);
        var savedJson = System.Text.Json.JsonSerializer.Serialize(_savedBindings);
        return editJson != savedJson;
    }

    private bool IsBindingModified(string commandId)
    {
        if (_editingBindings == null || _keyBindingService == null) return false;
        var defaults = _keyBindingService.GetDefaultBindings();
        var current = _editingBindings.ContainsKey(commandId) ? _editingBindings[commandId] : new List<string>();
        var defaultKeys = defaults.ContainsKey(commandId) ? defaults[commandId] : new List<string>();
        return !current.SequenceEqual(defaultKeys);
    }

    private void UpdateSaveButtonState()
    {
        bool hasChanges = HasShortcutChanges();
        if (ShortcutsSaveBtn != null) ShortcutsSaveBtn.IsEnabled = hasChanges;
        if (ShortcutsCancelBtn != null) ShortcutsCancelBtn.IsEnabled = hasChanges;
    }

    // ── Key recording dialog ──

    private StackPanel CreateKeyRecorderContent(string commandId)
    {
        var panel = new StackPanel { Spacing = 12 };

        panel.Children.Add(new TextBlock
        {
            Text = string.Format(
                _loc?.Get("Settings_ShortcutsRecordPrompt") ?? "'{0}'의 새 단축키를 입력하세요",
                Models.ShortcutCommands.GetDisplayName(commandId)),
            TextWrapping = TextWrapping.Wrap
        });

        var keyDisplay = new TextBlock
        {
            Text = _loc?.Get("Settings_ShortcutsPressKey") ?? "키를 누르세요...",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = (Brush)Application.Current.Resources["SpanAccentBrush"],
            Margin = new Thickness(0, 8, 0, 8)
        };
        panel.Children.Add(keyDisplay);

        var warningText = new TextBlock
        {
            Text = "",
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.Orange),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };
        panel.Children.Add(warningText);

        // 키 입력 캡처용 투명 TextBox
        var recorder = new TextBox
        {
            Width = 0,
            Height = 0,
            Opacity = 0,
            IsReadOnly = true
        };
        recorder.PreviewKeyDown += (s, e) =>
        {
            e.Handled = true;
            HandleKeyRecording(e, keyDisplay, warningText);
        };
        panel.Children.Add(recorder);

        // 다이얼로그 열릴 때 포커스
        panel.Loaded += (s, e) => recorder.Focus(FocusState.Programmatic);

        return panel;
    }

    private void HandleKeyRecording(KeyRoutedEventArgs e, TextBlock display, TextBlock warning)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            _recordingDialog?.Hide();
            return;
        }

        var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                   .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Shift)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                  .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

        // 수식키만 누른 경우 무시
        if (e.Key is Windows.System.VirtualKey.Control or Windows.System.VirtualKey.Shift
            or Windows.System.VirtualKey.Menu or Windows.System.VirtualKey.LeftControl
            or Windows.System.VirtualKey.RightControl or Windows.System.VirtualKey.LeftShift
            or Windows.System.VirtualKey.RightShift or Windows.System.VirtualKey.LeftMenu
            or Windows.System.VirtualKey.RightMenu)
            return;

        var keyString = Services.KeyBindingService.BuildKeyString(ctrl, shift, alt, e.Key);
        display.Text = keyString;

        // 시스템 예약 키 체크
        if (_keyBindingService!.IsSystemReserved(keyString))
        {
            warning.Text = string.Format(
                _loc?.Get("Settings_ShortcutsSystemReserved") ?? "'{0}'는 시스템 예약 키입니다.",
                keyString);
            warning.Visibility = Visibility.Visible;
            return;
        }

        // 구조적 키 체크
        if (_keyBindingService.IsStructuralKey(keyString))
        {
            warning.Text = string.Format(
                _loc?.Get("Settings_ShortcutsStructural") ?? "'{0}'는 탐색 필수 키이므로 변경할 수 없습니다.",
                keyString);
            warning.Visibility = Visibility.Visible;
            return;
        }

        // 충돌 검사
        var conflict = _keyBindingService.CheckConflict(keyString, _recordingCommandId!, _editingBindings!);
        if (conflict.Type == Services.ConflictType.AlreadyAssigned)
        {
            warning.Text = string.Format(
                _loc?.Get("Settings_ShortcutsConflict") ??
                "'{0}'는 현재 '{1}'에 할당되어 있습니다.\n교체하면 기존 바인딩이 제거됩니다.",
                keyString, conflict.ExistingCommandName);
            warning.Visibility = Visibility.Visible;

            // 다이얼로그 PrimaryButton을 "교체"로 변경
            if (_recordingDialog != null)
            {
                _recordingDialog.PrimaryButtonText = _loc?.Get("Replace") ?? "교체";
                // 기존 핸들러 제거 후 새로 등록 (중복 방지)
                _recordingDialog.PrimaryButtonClick -= OnRecordingReplace;
                _recordingDialog.PrimaryButtonClick += OnRecordingReplace;
                // 교체 시 사용할 정보 저장
                _pendingReplaceKey = keyString;
                _pendingReplaceConflictCommandId = conflict.ExistingCommandId;
            }
            return;
        }

        // 충돌 없음 — 즉시 할당
        if (!_editingBindings!.ContainsKey(_recordingCommandId!))
            _editingBindings[_recordingCommandId!] = new List<string>();
        if (!_editingBindings[_recordingCommandId!].Contains(keyString))
            _editingBindings[_recordingCommandId!].Add(keyString);

        _recordingDialog?.Hide();
        RebuildShortcutItemsUI();
    }

    private string? _pendingReplaceKey;
    private string? _pendingReplaceConflictCommandId;

    private void OnRecordingReplace(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_pendingReplaceKey == null || _editingBindings == null || _recordingCommandId == null) return;

        // 기존 바인딩에서 해당 키 제거
        if (_pendingReplaceConflictCommandId != null && _editingBindings.ContainsKey(_pendingReplaceConflictCommandId))
            _editingBindings[_pendingReplaceConflictCommandId].Remove(_pendingReplaceKey);

        // 새 바인딩에 추가
        if (!_editingBindings.ContainsKey(_recordingCommandId))
            _editingBindings[_recordingCommandId] = new List<string>();
        if (!_editingBindings[_recordingCommandId].Contains(_pendingReplaceKey))
            _editingBindings[_recordingCommandId].Add(_pendingReplaceKey);

        _pendingReplaceKey = null;
        _pendingReplaceConflictCommandId = null;

        RebuildShortcutItemsUI();
    }
}
