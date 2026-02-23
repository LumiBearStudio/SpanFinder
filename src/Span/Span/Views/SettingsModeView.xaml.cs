using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Views;

public sealed partial class SettingsModeView : UserControl
{
    private readonly ScrollViewer[] _sections;
    private readonly Services.SettingsService _settings;
    private LocalizationService? _loc;
    private DispatcherTimer? _updateTimer;
    private int _updateStage;
    private bool _isLoading = true;

    /// <summary>
    /// 뒤로가기 요청 이벤트 (MainWindow에서 구독)
    /// </summary>
    public event EventHandler? BackRequested;

    private readonly Dictionary<int, List<string>> _searchKeywords = new()
    {
        { 0, new() { "general", "language", "startup", "tray", "favorites", "일반", "언어", "시작", "시스템 트레이", "즐겨찾기" } },
        { 1, new() { "appearance", "theme", "pro", "density", "font", "icon", "모양", "테마", "밀도", "폰트", "아이콘" } },
        { 2, new() { "browsing", "view", "hidden", "extensions", "checkbox", "miller", "thumbnail", "quick look", "delete", "undo", "탐색", "보기", "숨김", "확장자", "체크박스", "밀러", "썸네일", "삭제", "실행 취소" } },
        { 3, new() { "tools", "terminal", "smart run", "context", "developer", "git", "shell", "copilot", "도구", "터미널", "명령", "컨텍스트", "개발자", "셸", "코파일럿" } },
        { 4, new() { "about", "license", "update", "pro", "upgrade", "coffee", "github", "link", "정보", "라이선스", "업데이트", "후원", "링크" } },
    };

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
            AboutSection
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
                _ => 0
            };

            var startup = _settings.StartupBehavior;
            StartupRestore.IsChecked = startup == 0;
            StartupHome.IsChecked = startup == 1;
            StartupFolder.IsChecked = startup == 2;

            FavoritesTreeToggle.IsOn = _settings.ShowFavoritesTree;
            SystemTrayToggle.IsOn = _settings.MinimizeToTray;

            // Appearance
            var theme = _settings.Theme;
            ThemeSystem.IsChecked = theme == "system";
            ThemeLight.IsChecked = theme == "light";
            ThemeDark.IsChecked = theme == "dark";

            var density = _settings.Density;
            DensityCompact.IsChecked = density == "compact";
            DensityComfortable.IsChecked = density == "comfortable";
            DensitySpacious.IsChecked = density == "spacious";

            var iconPack = _settings.IconPack;
            IconPackCombo.SelectedIndex = iconPack switch
            {
                "phosphor" => 1,
                "tabler" => 2,
                _ => 0
            };

            var font = _settings.FontFamily;
            FontCombo.SelectedIndex = font switch
            {
                "Cascadia Code" => 1,
                "Consolas" => 2,
                _ => 0
            };

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
            DeveloperMenuToggle.IsOn = _settings.ShowDeveloperMenu;
            CopilotMenuToggle.IsOn = _settings.ShowCopilotMenu;
            ContextMenuToggle.IsOn = _settings.ShowContextMenu;
        }
        finally
        {
            _isLoading = false;
        }
    }

    // ── Wire UI events to save settings ──

    private void WireEvents()
    {
        StartupRestore.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 0; };
        StartupHome.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 1; };
        StartupFolder.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 2; };

        FavoritesTreeToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowFavoritesTree = FavoritesTreeToggle.IsOn; };
        SystemTrayToggle.Toggled += (s, e) => { if (!_isLoading) _settings.MinimizeToTray = SystemTrayToggle.IsOn; };

        ThemeSystem.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "system"; };
        ThemeLight.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "light"; };
        ThemeDark.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "dark"; };

        DensityCompact.Checked += (s, e) => { if (!_isLoading) _settings.Density = "compact"; };
        DensityComfortable.Checked += (s, e) => { if (!_isLoading) _settings.Density = "comfortable"; };
        DensitySpacious.Checked += (s, e) => { if (!_isLoading) _settings.Density = "spacious"; };

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
            _settings.FontFamily = FontCombo.SelectedIndex switch
            {
                1 => "Cascadia Code",
                2 => "Consolas",
                _ => "Segoe UI Variable"
            };
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
        DeveloperMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowDeveloperMenu = DeveloperMenuToggle.IsOn; };
        CopilotMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowCopilotMenu = CopilotMenuToggle.IsOn; };
        ContextMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowContextMenu = ContextMenuToggle.IsOn; };
    }

    // ── Responsive layout ──

    private void SettingsNav_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var width = e.NewSize.Width;
        if (width < 500)
        {
            SettingsNav.PaneDisplayMode = NavigationViewPaneDisplayMode.Top;
            SettingsNav.IsPaneOpen = true;
        }
        else
        {
            SettingsNav.PaneDisplayMode = NavigationViewPaneDisplayMode.Left;
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
            "About" => AboutSection,
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

        // Header
        SettingsTitle.Text = _loc.Get("Settings");
        SettingsSearchBox.PlaceholderText = _loc.Get("Settings_SearchPlaceholder");

        // Navigation
        NavGeneral.Content = _loc.Get("Settings_General");
        NavAppearance.Content = _loc.Get("Settings_Appearance");
        NavBrowsing.Content = _loc.Get("Settings_Browsing");
        NavTools.Content = _loc.Get("Settings_Tools");
        NavAbout.Content = _loc.Get("Settings_About");

        // General
        GeneralTitle.Text = _loc.Get("Settings_General");
        LangLabel.Text = _loc.Get("Settings_Language");
        LangDesc.Text = _loc.Get("Settings_LanguageDesc");
        LangSystem.Content = _loc.Get("Settings_SystemDefault");
        LangRestartText.Text = _loc.Get("Settings_RestartNotice");
        StartupLabel.Text = _loc.Get("Settings_StartupBehavior");
        StartupDesc.Text = _loc.Get("Settings_StartupBehaviorDesc");
        RestoreSessionLabel.Text = _loc.Get("Settings_RestoreSession");
        RestoreSessionDesc.Text = _loc.Get("Settings_RestoreSessionDesc");
        StartupHome.Content = _loc.Get("Settings_OpenHome");
        OpenFolderLabel.Text = _loc.Get("Settings_OpenSpecificFolder");
        CustomPathDesc.Text = _loc.Get("Settings_CustomPath");
        FavTreeLabel.Text = _loc.Get("Settings_FavoritesTree");
        FavTreeDesc.Text = _loc.Get("Settings_FavoritesTreeDesc");
        SysTrayLabel.Text = _loc.Get("Settings_SystemTray");
        SysTrayDesc.Text = _loc.Get("Settings_SystemTrayDesc");

        // Appearance
        AppearanceTitle.Text = _loc.Get("Settings_Appearance");
        ThemeLabel.Text = _loc.Get("Settings_AppTheme");
        ThemeDesc.Text = _loc.Get("Settings_ThemeDesc");
        ThemeSystemText.Text = _loc.Get("Settings_System");
        ThemeLightText.Text = _loc.Get("Settings_Light");
        ThemeDarkText.Text = _loc.Get("Settings_Dark");
        ProThemesLabel.Text = _loc.Get("Settings_ProThemes");
        ProThemesDesc.Text = _loc.Get("Settings_ProThemesDesc");
        MidnightGoldDesc.Text = _loc.Get("Settings_MidnightGoldDesc");
        CyberpunkDesc.Text = _loc.Get("Settings_CyberpunkDesc");
        NordicDesc.Text = _loc.Get("Settings_NordicDesc");
        UpgradeProThemesText.Text = _loc.Get("Settings_UpgradeProThemes");
        DensityLabel.Text = _loc.Get("Settings_LayoutDensity");
        DensityDesc.Text = _loc.Get("Settings_LayoutDensityDesc");
        IconPackLabel.Text = _loc.Get("Settings_IconPack");
        IconPackDesc.Text = _loc.Get("Settings_IconPackDesc");
        IconPackRestartText.Text = _loc.Get("Settings_IconPackRestart");
        FontLabel.Text = _loc.Get("Settings_Font");
        FontDesc.Text = _loc.Get("Settings_FontDesc");

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
        ThumbnailLabel.Text = _loc.Get("Settings_Thumbnails");
        ThumbnailDesc.Text = _loc.Get("Settings_ThumbnailsDesc");
        QuickLookLabel.Text = _loc.Get("Settings_QuickLook");
        QuickLookDesc.Text = _loc.Get("Settings_QuickLookDesc");
        DeleteConfirmLabel.Text = _loc.Get("Settings_DeleteConfirm");
        DeleteConfirmDesc.Text = _loc.Get("Settings_DeleteConfirmDesc");
        UndoLabel.Text = _loc.Get("Settings_UndoHistory");
        UndoDesc.Text = _loc.Get("Settings_UndoHistoryDesc");

        // Tools
        ToolsTitle.Text = _loc.Get("Settings_Tools");
        DevBadge.Text = _loc.Get("Settings_Developer");
        TerminalLabel.Text = _loc.Get("Settings_TerminalApp");
        TerminalDesc.Text = _loc.Get("Settings_TerminalAppDesc");
        SmartRunLabel.Text = _loc.Get("Settings_SmartRun");
        SmartRunDesc.Text = _loc.Get("Settings_SmartRunDesc");
        AddShortcutText.Text = _loc.Get("Settings_AddShortcut");
        ShellExtLabel.Text = _loc.Get("Settings_ShellExtras");
        ShellExtDesc.Text = _loc.Get("Settings_ShellExtrasDesc");
        DevMenuLabel.Text = _loc.Get("Settings_DeveloperMenu");
        DevMenuDesc.Text = _loc.Get("Settings_DeveloperMenuDesc");
        CopilotLabel.Text = _loc.Get("Settings_CopilotMenu");
        CopilotDesc.Text = _loc.Get("Settings_CopilotMenuDesc");
        CtxMenuLabel.Text = _loc.Get("Settings_ContextMenu");
        CtxMenuDesc.Text = _loc.Get("Settings_ContextMenuDesc");

        // About
        AboutTitle.Text = _loc.Get("Settings_About");
        EvalCopyText.Text = _loc.Get("Settings_EvalCopy");
        UpdateText.Text = _loc.Get("Settings_CheckUpdate");
        UpgradeProTitle.Text = _loc.Get("Settings_UpgradePro");
        UpgradeProDesc.Text = _loc.Get("Settings_UpgradeProDesc");
        UnlockThemesText.Text = _loc.Get("Settings_UnlockThemes");
        UnlimitedSmartRunText.Text = _loc.Get("Settings_UnlimitedSmartRun");
        AllPremiumText.Text = _loc.Get("Settings_AllPremiumFeatures");
        CoffeeLabel.Text = _loc.Get("Settings_BuyMeCoffee");
        CoffeeDesc.Text = _loc.Get("Settings_BuyMeCoffeeDesc");
        LinksLabel.Text = _loc.Get("Settings_Links");
        GitHubText.Text = _loc.Get("Settings_GitHub");
        BugReportText.Text = _loc.Get("Settings_BugReport");
        PrivacyText.Text = _loc.Get("Settings_Privacy");
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

    // ── Search filtering ──

    private void SettingsSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        var query = sender.Text?.Trim().ToLowerInvariant();

        if (string.IsNullOrEmpty(query))
        {
            if (SettingsNav.SelectedItem is NavigationViewItem navItem && navItem.Tag is string tag)
                ShowSection(tag);
            else
                ShowSection("General");
            return;
        }

        bool anyMatch = false;

        for (int i = 0; i < _sections.Length; i++)
        {
            bool sectionMatches = _searchKeywords[i]
                .Any(keyword => keyword.Contains(query, StringComparison.OrdinalIgnoreCase));

            _sections[i].Visibility = sectionMatches ? Visibility.Visible : Visibility.Collapsed;

            if (sectionMatches) anyMatch = true;
        }

        if (!anyMatch)
        {
            foreach (var section in _sections)
                section.Visibility = Visibility.Visible;
        }
    }
}
