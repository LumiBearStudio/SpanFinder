using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Span.Views;

public sealed partial class SettingsDialog : ContentDialog
{
    private readonly ScrollViewer[] _sections;
    private readonly Services.SettingsService _settings;
    private DispatcherTimer? _updateTimer;
    private int _updateStage;
    private bool _isLoading = true; // Suppress events during initial load (true by default to block InitializeComponent events)

    // Search keyword map: section index -> list of searchable keywords
    private readonly Dictionary<int, List<string>> _searchKeywords = new()
    {
        { 0, new() { "일반", "언어", "language", "시작", "startup", "시스템 트레이", "tray" } },
        { 1, new() { "모양", "테마", "theme", "pro", "밀도", "density", "폰트", "font" } },
        { 2, new() { "탐색", "보기", "숨김", "확장자", "체크박스", "밀러", "썸네일", "quick look", "삭제", "undo", "실행 취소" } },
        { 3, new() { "도구", "터미널", "terminal", "smart run", "명령", "컨텍스트", "context" } },
        { 4, new() { "정보", "about", "라이선스", "license", "업데이트", "update", "pro", "upgrade", "coffee", "후원", "github", "링크" } },
    };

    public SettingsDialog()
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
                _ => 0 // system
            };

            var startup = _settings.StartupBehavior;
            StartupRestore.IsChecked = startup == 0;
            StartupHome.IsChecked = startup == 1;
            StartupFolder.IsChecked = startup == 2;

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

            var font = _settings.FontFamily;
            FontCombo.SelectedIndex = font switch
            {
                "Cascadia Code" => 1,
                "Consolas" => 2,
                _ => 0 // Segoe UI Variable
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
                _ => 2 // 50
            };

            // Tools
            var terminal = _settings.DefaultTerminal;
            TerminalCombo.SelectedIndex = terminal switch
            {
                "powershell" => 1,
                "cmd" => 2,
                _ => 0 // wt
            };
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
        // General — Language
        // (handled by LanguageCombo_SelectionChanged)

        // General — Startup
        StartupRestore.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 0; };
        StartupHome.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 1; };
        StartupFolder.Checked += (s, e) => { if (!_isLoading) _settings.StartupBehavior = 2; };

        // General — System Tray
        SystemTrayToggle.Toggled += (s, e) => { if (!_isLoading) _settings.MinimizeToTray = SystemTrayToggle.IsOn; };

        // Appearance — Theme
        ThemeSystem.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "system"; };
        ThemeLight.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "light"; };
        ThemeDark.Checked += (s, e) => { if (!_isLoading) _settings.Theme = "dark"; };

        // Appearance — Density
        DensityCompact.Checked += (s, e) => { if (!_isLoading) _settings.Density = "compact"; };
        DensityComfortable.Checked += (s, e) => { if (!_isLoading) _settings.Density = "comfortable"; };
        DensitySpacious.Checked += (s, e) => { if (!_isLoading) _settings.Density = "spacious"; };

        // Appearance — Font
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

        // Browsing toggles
        ShowHiddenToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowHiddenFiles = ShowHiddenToggle.IsOn; };
        ShowExtensionsToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowFileExtensions = ShowExtensionsToggle.IsOn; };
        CheckboxToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowCheckboxes = CheckboxToggle.IsOn; };
        ThumbnailToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowThumbnails = ThumbnailToggle.IsOn; };
        QuickLookToggle.Toggled += (s, e) => { if (!_isLoading) _settings.EnableQuickLook = QuickLookToggle.IsOn; };
        ConfirmDeleteToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ConfirmDelete = ConfirmDeleteToggle.IsOn; };

        // Browsing — Miller click
        MillerClickCombo.SelectionChanged += (s, e) =>
        {
            if (_isLoading) return;
            _settings.MillerClickBehavior = MillerClickCombo.SelectedIndex == 1 ? "double" : "single";
        };

        // Browsing — Undo history
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

        // Tools
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
        ContextMenuToggle.Toggled += (s, e) => { if (!_isLoading) _settings.ShowContextMenu = ContextMenuToggle.IsOn; };
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
        {
            section.Visibility = Visibility.Collapsed;
        }

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

    // ── Update check animation (3-stage) ──

    private void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        if (_updateTimer != null) return;

        UpdateButton.IsEnabled = false;
        _updateStage = 0;

        UpdateIcon.Glyph = "\uE895";
        UpdateText.Text = "확인 중...";

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
            UpdateText.Text = "최신 버전입니다";
            _updateTimer!.Interval = TimeSpan.FromMilliseconds(3000);
        }
        else
        {
            UpdateIcon.Glyph = "\uE72C";
            UpdateText.Text = "업데이트 확인";
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
            {
                ShowSection(tag);
            }
            else
            {
                ShowSection("General");
            }
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
            {
                section.Visibility = Visibility.Visible;
            }
        }
    }
}
