using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
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
        private ShellService? _shellService;

        /// <summary>
        /// MainWindow에서 처리할 액션 (extractHere, extractTo, openInNewTab 등).
        /// </summary>
        public event Action<string, string>? ActionForwarded;

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

            ViewModel.ActionRequested += OnActionRequested;
            ViewModel.PropertyChanged += OnViewModelPropertyChanged;

            _shellService = App.Current.Services.GetService<ShellService>();

            ConfigureWindow();

            this.Content.KeyDown += OnContentKeyDown;
            this.Closed += OnWindowClosed;
            ContentPreviewArea.SizeChanged += OnContentPreviewAreaSizeChanged;

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

            this.Title = LocalizationService.L("QuickLook_Title");
            TitleText.Text = LocalizationService.L("QuickLook_Title");

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
                TitleText.Text = string.Format(LocalizationService.L("QuickLook_TitleWithName"), item.Name);
                this.Title = string.Format(LocalizationService.L("QuickLook_TitleWithName"), item.Name);
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
                InfoItemCountText.Text = count > 0 ? string.Format(LocalizationService.L("QuickLook_Items"), count) : "";
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
                if (!string.IsNullOrEmpty(sizeText) && sizeText != LocalizationService.L("QuickLook_CalculatingSize"))
                {
                    InfoSizeText.Text = sizeText;
                    if (!string.IsNullOrEmpty(InfoItemCountText.Text))
                        InfoSizeDot.Visibility = Visibility.Visible;
                }
                else if (sizeText == LocalizationService.L("QuickLook_CalculatingSize"))
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

        /// <summary>
        /// ViewModel에서 발생한 액션 요청을 처리.
        /// 직접 처리 가능한 것은 바로 실행, MainWindow 필요한 것은 포워딩.
        /// </summary>
        private void OnActionRequested(string action, string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                bool shouldClose = false;

                switch (action)
                {
                    // --- 팝업 종료: 다른 앱/창으로 전환되는 액션 ---
                    case "open":
                        _shellService?.OpenFile(path);
                        shouldClose = true;
                        break;

                    case "openWith":
                        _ = _shellService?.OpenWithAsync(path);
                        shouldClose = true;
                        break;

                    case "openTerminal":
                        _shellService?.OpenTerminal(path);
                        shouldClose = true;
                        break;

                    case "showProperties":
                        _shellService?.ShowProperties(path);
                        shouldClose = true; // TopMost 때문에 Properties가 뒤에 가려짐 → 닫기
                        break;

                    // --- 팝업 종료: MainWindow 포워딩 액션 ---
                    case "extractHere":
                    case "extractTo":
                    case "openInNewTab":
                        ActionForwarded?.Invoke(action, path);
                        shouldClose = true;
                        break;

                    // --- 팝업 유지 + 토스트 ---
                    case "copyPath":
                        _shellService?.CopyPathToClipboard(path);
                        ShowToast(_loc?.Get("Toast_PathCopied") ?? "Path copied to clipboard");
                        break;

                    case "copyContent":
                        CopyTextContent();
                        ShowToast(_loc?.Get("Toast_TextCopied") ?? "Text copied to clipboard");
                        break;

                    // --- 팝업 유지: 회전 저장 ---
                    case "saveRotation":
                        _ = SaveRotationAsync(path);
                        break;
                }

                if (shouldClose)
                {
                    try { this.Close(); } catch { }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] Action '{action}' error: {ex.Message}");
            }
        }

        private void ShowToast(string message)
        {
            ToastText.Text = message;
            ToastOverlay.Opacity = 1;

            var timer = new Microsoft.UI.Xaml.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1200)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                ToastOverlay.Opacity = 0;
            };
            timer.Start();
        }

        private void CopyTextContent()
        {
            try
            {
                var text = ViewModel.TextPreview;
                if (string.IsNullOrEmpty(text)) return;

                var dataPackage = new DataPackage();
                dataPackage.SetText(text);
                Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] CopyContent error: {ex.Message}");
            }
        }

        private async Task SaveRotationAsync(string path)
        {
            int angle = (int)ViewModel.RotationAngle;
            if (angle == 0) return;

            string? tmpPath = null;
            try
            {
                var rotation = angle switch
                {
                    90 => Windows.Graphics.Imaging.BitmapRotation.Clockwise90Degrees,
                    270 => Windows.Graphics.Imaging.BitmapRotation.Clockwise270Degrees,
                    180 => Windows.Graphics.Imaging.BitmapRotation.Clockwise180Degrees,
                    _ => Windows.Graphics.Imaging.BitmapRotation.None
                };

                // 확장자 기반 인코더 결정
                var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
                var encoderId = ext switch
                {
                    ".png" => Windows.Graphics.Imaging.BitmapEncoder.PngEncoderId,
                    ".bmp" => Windows.Graphics.Imaging.BitmapEncoder.BmpEncoderId,
                    ".gif" => Windows.Graphics.Imaging.BitmapEncoder.GifEncoderId,
                    ".tif" or ".tiff" => Windows.Graphics.Imaging.BitmapEncoder.TiffEncoderId,
                    _ => Windows.Graphics.Imaging.BitmapEncoder.JpegEncoderId
                };

                // 1) 원본 파일을 메모리로 읽기
                byte[] sourceBytes = await System.IO.File.ReadAllBytesAsync(path);

                // 2) 임시 파일에 회전 결과 쓰기 (원본 보호)
                tmpPath = path + ".rotate.tmp";

                using (var memStream = new Windows.Storage.Streams.InMemoryRandomAccessStream())
                {
                    // 소스 바이트를 스트림에 쓰기
                    await memStream.WriteAsync(sourceBytes.AsBuffer());
                    memStream.Seek(0);

                    var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(memStream);
                    var bitmap = await decoder.GetSoftwareBitmapAsync();

                    // 임시 파일에 회전 적용하여 저장
                    var tmpFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(
                        await Task.Run(() =>
                        {
                            System.IO.File.WriteAllBytes(tmpPath, new byte[0]);
                            return tmpPath;
                        }));

                    using (var outStream = await tmpFile.OpenAsync(Windows.Storage.FileAccessMode.ReadWrite))
                    {
                        var encoder = await Windows.Graphics.Imaging.BitmapEncoder.CreateAsync(encoderId, outStream);
                        encoder.SetSoftwareBitmap(bitmap);
                        encoder.BitmapTransform.Rotation = rotation;
                        await encoder.FlushAsync();
                    }

                    bitmap.Dispose();
                }

                // 3) 임시 파일 크기 검증 후 원본 교체
                var tmpInfo = new System.IO.FileInfo(tmpPath);
                if (tmpInfo.Exists && tmpInfo.Length > 0)
                {
                    System.IO.File.Move(tmpPath, path, overwrite: true);
                    tmpPath = null; // 성공 → cleanup 불필요

                    // 회전 상태 리셋 + 미리보기 새로고침
                    ViewModel.RotationAngle = 0;
                    ViewModel.HasPendingRotation = false;
                    ShowToast(_loc?.Get("QuickLook_RotationSaved") ?? "Saved");
                    await Task.Delay(150);
                    ActionForwarded?.Invoke("refreshAfterRotate", path);
                }
                else
                {
                    Helpers.DebugLogger.Log("[QuickLook] Rotate: tmp file empty, aborting");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] Rotate error: {ex.Message}");
            }
            finally
            {
                // 실패 시 임시 파일 정리
                if (tmpPath != null)
                {
                    try { System.IO.File.Delete(tmpPath); } catch { }
                }
            }
        }

        private void OnWindowClosed(object sender, WindowEventArgs args)
        {
            StopMedia();
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            ViewModel.ActionRequested -= OnActionRequested;
            ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel.PropertyChanged -= OnInfoOnlyPropertyChanged;
            ContentPreviewArea.SizeChanged -= OnContentPreviewAreaSizeChanged;
            this.Content.KeyDown -= OnContentKeyDown;
            ViewModel?.Dispose();
            WindowClosed?.Invoke();
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(QuickLookViewModel.RotationAngle))
            {
                UpdateImageTransform();
            }
        }

        private void OnContentPreviewAreaSizeChanged(object sender, SizeChangedEventArgs e)
        {
            // 컨테이너 크기 변경 시 스케일 재계산
            if (ViewModel.RotationAngle % 180 != 0)
            {
                UpdateImageTransform();
            }
        }

        /// <summary>
        /// 이미지 회전 시 스케일 보정.
        /// 90/270° 회전 시 RenderTransform은 레이아웃에 영향을 주지 않으므로,
        /// 컨테이너 크기와 이미지 비율을 고려한 스케일 팩터를 적용해야 함.
        /// </summary>
        private void UpdateImageTransform()
        {
            double angle = ViewModel.RotationAngle;
            ImageTransform.Rotation = angle;

            bool isSwapped = (int)angle % 180 != 0; // 90° or 270°

            if (!isSwapped)
            {
                ImageTransform.ScaleX = 1;
                ImageTransform.ScaleY = 1;
                return;
            }

            try
            {
                // 컨테이너 영역 (Margin 16 제외)
                double containerW = ContentPreviewArea.ActualWidth - 32;
                double containerH = ContentPreviewArea.ActualHeight - 32;

                if (containerW <= 0 || containerH <= 0) return;

                // 원본 이미지 픽셀 크기
                var bmp = ViewModel.ImagePreview as Microsoft.UI.Xaml.Media.Imaging.BitmapImage;
                double imgW = bmp?.PixelWidth ?? 0;
                double imgH = bmp?.PixelHeight ?? 0;

                if (imgW <= 0 || imgH <= 0) return;

                // 0° 상태에서의 Uniform 스케일 = min(containerW/imgW, containerH/imgH)
                double normalScale = Math.Min(containerW / imgW, containerH / imgH);
                // 90° 회전 후 원하는 스케일 = min(containerW/imgH, containerH/imgW)
                double rotatedScale = Math.Min(containerW / imgH, containerH / imgW);

                double factor = rotatedScale / normalScale;

                ImageTransform.ScaleX = factor;
                ImageTransform.ScaleY = factor;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[QuickLook] UpdateImageTransform error: {ex.Message}");
                ImageTransform.ScaleX = 1;
                ImageTransform.ScaleY = 1;
            }
        }

        private void LocalizeUI()
        {
            if (_loc == null) return;

            // Content mode action tooltips
            ToolTipService.SetToolTip(BtnRotate, _loc.Get("QuickLook_Rotate"));
            ToolTipService.SetToolTip(BtnSaveRotation, _loc.Get("QuickLook_SaveRotation"));
            ToolTipService.SetToolTip(BtnCopyContent, _loc.Get("QuickLook_CopyText"));
            ToolTipService.SetToolTip(BtnExtractHere, _loc.Get("ExtractHere"));
            ToolTipService.SetToolTip(BtnExtractTo, _loc.Get("ExtractTo"));
            ToolTipService.SetToolTip(BtnCopyPath, _loc.Get("CopyPath"));
            ToolTipService.SetToolTip(BtnOpenDefault, _loc.Get("Open"));
            ToolTipService.SetToolTip(BtnOpenWith, _loc.Get("OpenWith"));
            ToolTipService.SetToolTip(BtnProperties, _loc.Get("Properties"));

            // Info-only mode action tooltips
            ToolTipService.SetToolTip(BtnInfoNewTab, _loc.Get("OpenInNewTab"));
            ToolTipService.SetToolTip(BtnInfoTerminal, _loc.Get("OpenTerminal"));
        }
    }
}
