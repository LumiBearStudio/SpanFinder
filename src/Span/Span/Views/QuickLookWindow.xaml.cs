using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using Windows.Graphics;

namespace Span.Views
{
    /// <summary>
    /// Quick Look 플로팅 윈도우.
    /// 두 가지 모드:
    ///   1. Content Preview (Image, Text, PDF 등): 메인 창 70% 크기
    ///   2. Info Only (Folder, Generic): Finder 스타일 컴팩트 카드
    /// ESC/Space로 닫기, 커스텀 타이틀바, Mica 배경.
    /// </summary>
    public sealed partial class QuickLookWindow : Window
    {
        public QuickLookViewModel ViewModel { get; private set; }

        public event Action? WindowClosed;

        private LocalizationService? _loc;
        private bool _isInfoOnlyMode;
        private AppWindow? _mainAppWindow;

        // Compact info-only size
        private const int InfoWidth = 840;
        private const int InfoHeight = 400;

        public QuickLookWindow()
        {
            this.InitializeComponent();

            var previewService = App.Current.Services.GetRequiredService<PreviewService>();
            ViewModel = new QuickLookViewModel(previewService);
            (this.Content as FrameworkElement)!.DataContext = ViewModel;

            ViewModel.CloseRequested += () =>
            {
                try { this.Close(); } catch { }
            };

            ConfigureWindow();

            this.Content.KeyDown += OnContentKeyDown;
            this.Closed += OnWindowClosed;

            _loc = App.Current.Services.GetService<LocalizationService>();
            if (_loc != null)
            {
                LocalizeUI();
                _loc.LanguageChanged += LocalizeUI;
            }
        }

        private void ConfigureWindow()
        {
            // Mica backdrop
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

            // Custom title bar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(QuickLookTitleBar);

            this.Title = "Quick Look";

            var appWindow = this.AppWindow;

            // Caption button padding
            var titleBar = appWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            // Default to content mode size (will be adjusted in UpdateContent)
            appWindow.Resize(new SizeInt32(600, 500));

            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMinimizable = false;
                presenter.IsMaximizable = false;
                presenter.IsAlwaysOnTop = true;
            }

            // Update right padding for caption buttons
            UpdateTitleBarPadding();
        }

        private void UpdateTitleBarPadding()
        {
            try
            {
                // Reserve space for min/max/close caption buttons (approx 138px on standard DPI)
                var scale = (this.Content as FrameworkElement)?.XamlRoot?.RasterizationScale ?? 1.0;
                TitleRightPadding.Width = new GridLength(138);
            }
            catch { }
        }

        /// <summary>
        /// 메인 윈도우의 AppWindow를 설정하여 중앙 위치 계산에 사용.
        /// </summary>
        public void SetMainWindow(AppWindow mainAppWindow)
        {
            _mainAppWindow = mainAppWindow;
        }

        /// <summary>
        /// 메인 윈도우의 테마를 QuickLook 윈도우에 동기화.
        /// WinUI 3에서 별도 Window는 독립적 테마를 가지므로 수동 동기화 필요.
        /// </summary>
        public void SyncTheme()
        {
            try
            {
                var settings = App.Current.Services.GetService<ISettingsService>();
                if (settings == null) return;

                var theme = settings.Theme;
                if (this.Content is not FrameworkElement root) return;

                bool isCustom = MainWindow._customThemes.Contains(theme);

                var targetTheme = theme switch
                {
                    "light" => ElementTheme.Light,
                    "dark" => ElementTheme.Dark,
                    _ when isCustom && theme == "solarized-light" => ElementTheme.Light,
                    _ when isCustom => ElementTheme.Dark,
                    _ => ElementTheme.Default
                };

                if (isCustom)
                {
                    bool isLightCustom = theme == "solarized-light";
                    root.RequestedTheme = isLightCustom ? ElementTheme.Dark : ElementTheme.Light;
                    MainWindow.ApplyCustomThemeOverrides(root, theme);
                    root.RequestedTheme = isLightCustom ? ElementTheme.Light : ElementTheme.Dark;
                }
                else
                {
                    MainWindow.ApplyCustomThemeOverrides(root, theme);
                    root.RequestedTheme = targetTheme == ElementTheme.Light
                        ? ElementTheme.Dark : ElementTheme.Light;
                    root.RequestedTheme = targetTheme;
                }

                // 캡션 버튼 색상도 테마에 맞게 조정
                var titleBar = this.AppWindow.TitleBar;
                bool isLight = theme == "light" || theme == "solarized-light"
                    || (theme == "system" && App.Current.RequestedTheme == ApplicationTheme.Light);
                titleBar.ButtonForegroundColor = isLight ? Colors.Black : Colors.White;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] SyncTheme error: {ex.Message}");
            }
        }

        /// <summary>
        /// 미리보기 내용 업데이트 + 모드/사이즈 자동 전환.
        /// </summary>
        public void UpdateContent(FileSystemViewModel? item)
        {
            if (item != null)
            {
                TitleText.Text = $"Quick Look — {item.Name}";
                this.Title = $"Quick Look - {item.Name}";
            }

            ViewModel.UpdateContent(item);

            // Determine mode after ViewModel updates
            if (item != null)
            {
                bool isFolder = item is FolderViewModel;
                var previewType = App.Current.Services.GetRequiredService<PreviewService>()
                    .GetPreviewType(item.Path, isFolder);

                bool infoOnly = previewType == PreviewType.Folder || previewType == PreviewType.Generic;
                SwitchMode(infoOnly, item);
            }
        }

        /// <summary>
        /// 모드 전환: 컨텐츠 미리보기 vs 정보만 표시.
        /// </summary>
        private void SwitchMode(bool infoOnly, FileSystemViewModel item)
        {
            _isInfoOnlyMode = infoOnly;
            var appWindow = this.AppWindow;

            if (infoOnly)
            {
                // === Info Only Mode (Finder style compact) ===
                ContentPreviewArea.Visibility = Visibility.Collapsed;
                BottomInfoBar.Visibility = Visibility.Collapsed;
                InfoOnlyArea.Visibility = Visibility.Visible;

                // Populate info texts
                UpdateInfoOnlyTexts(item);

                // Compact size
                appWindow.Resize(new SizeInt32(InfoWidth, InfoHeight));
                CenterOnMainWindow(InfoWidth, InfoHeight);
            }
            else
            {
                // === Content Preview Mode ===
                ContentPreviewArea.Visibility = Visibility.Visible;
                BottomInfoBar.Visibility = Visibility.Visible;
                InfoOnlyArea.Visibility = Visibility.Collapsed;

                // 70% of main window
                var (w, h) = GetContentModeSize();
                appWindow.Resize(new SizeInt32(w, h));
                CenterOnMainWindow(w, h);
            }
        }

        private void UpdateInfoOnlyTexts(FileSystemViewModel item)
        {
            bool isFolder = item is FolderViewModel;

            // Size
            if (!isFolder && !string.IsNullOrEmpty(ViewModel.FileSizeFormatted))
            {
                InfoSizeText.Text = ViewModel.FileSizeFormatted;
            }
            else if (isFolder)
            {
                // Folder size will be updated async via binding
                InfoSizeText.Text = "";
                // Subscribe to ViewModel property changes for folder size
                ViewModel.PropertyChanged += OnInfoOnlyPropertyChanged;
            }
            else
            {
                InfoSizeText.Text = "";
            }

            // Item count for folders
            if (isFolder && item is FolderViewModel folderVm)
            {
                int count = folderVm.Children.Count;
                InfoItemCountText.Text = count > 0 ? $"{count} items" : "";
                InfoSizeDot.Visibility = Visibility.Collapsed; // will show when size arrives
            }
            else
            {
                InfoItemCountText.Text = "";
                InfoSizeDot.Visibility = Visibility.Collapsed;
            }

            // Type
            InfoTypeText.Text = !string.IsNullOrEmpty(ViewModel.FileType) ? ViewModel.FileType : "";

            // Date
            if (!string.IsNullOrEmpty(ViewModel.DateModified))
            {
                var modLabel = _loc?.Get("Preview_Modified") ?? "Modified";
                InfoDateText.Text = $"{modLabel}: {ViewModel.DateModified}";
            }
            else
            {
                InfoDateText.Text = "";
            }
        }

        private void OnInfoOnlyPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuickLookViewModel.FolderSizeText))
            {
                var sizeText = ViewModel.FolderSizeText;
                if (!string.IsNullOrEmpty(sizeText) && sizeText != "Calculating size...")
                {
                    InfoSizeText.Text = sizeText;
                    if (!string.IsNullOrEmpty(InfoItemCountText.Text))
                        InfoSizeDot.Visibility = Visibility.Visible;
                }
                else if (sizeText == "Calculating size...")
                {
                    var calcLabel = _loc?.Get("Preview_Calculating") ?? "Calculating...";
                    InfoSizeText.Text = calcLabel;
                }
            }
        }

        /// <summary>
        /// 메인 창 70% 크기 계산.
        /// </summary>
        private (int width, int height) GetContentModeSize()
        {
            int w = 800, h = 600; // default fallback

            if (_mainAppWindow != null)
            {
                var mainSize = _mainAppWindow.Size;
                w = (int)(mainSize.Width * 0.8);
                h = (int)(mainSize.Height * 0.8);
            }

            // Minimum size
            w = Math.Max(500, w);
            h = Math.Max(400, h);

            return (w, h);
        }

        /// <summary>
        /// 메인 Span 창 중앙에 배치.
        /// </summary>
        private void CenterOnMainWindow(int width, int height)
        {
            try
            {
                if (_mainAppWindow != null)
                {
                    var mainPos = _mainAppWindow.Position;
                    var mainSize = _mainAppWindow.Size;
                    int x = mainPos.X + (mainSize.Width - width) / 2;
                    int y = mainPos.Y + (mainSize.Height - height) / 2;
                    this.AppWindow.Move(new PointInt32(x, y));
                }
                else
                {
                    // Fallback: screen center
                    var displayArea = DisplayArea.GetFromWindowId(this.AppWindow.Id, DisplayAreaFallback.Primary);
                    if (displayArea != null)
                    {
                        var workArea = displayArea.WorkArea;
                        int x = (workArea.Width - width) / 2 + workArea.X;
                        int y = (workArea.Height - height) / 2 + workArea.Y;
                        this.AppWindow.Move(new PointInt32(x, y));
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] CenterOnMainWindow error: {ex.Message}");
            }
        }

        private void OnContentKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Escape || e.Key == Windows.System.VirtualKey.Space)
            {
                e.Handled = true;
                this.Close();
            }
        }

        public void StopMedia()
        {
            try
            {
                if (QuickLookMediaPlayer?.MediaPlayer != null)
                {
                    QuickLookMediaPlayer.MediaPlayer.Pause();
                    QuickLookMediaPlayer.Source = null;
                }
            }
            catch { }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            StopMedia();
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            ViewModel.PropertyChanged -= OnInfoOnlyPropertyChanged;
            this.Content.KeyDown -= OnContentKeyDown;
            ViewModel?.Dispose();
            WindowClosed?.Invoke();
        }

        private void LocalizeUI()
        {
            // Info-only mode texts are set in UpdateInfoOnlyTexts
        }
    }
}
