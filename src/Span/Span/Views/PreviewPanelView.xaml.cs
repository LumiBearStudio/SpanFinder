using ColorCode;
using ColorCode.Common;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Span.Services;
using Span.ViewModels;
using System;
using System.Collections.Generic;
using Windows.Media.Playback;

namespace Span.Views
{
    public sealed partial class PreviewPanelView : UserControl
    {
        private LocalizationService? _loc;
        private DispatcherTimer? _seekTimer;
        public PreviewPanelViewModel? ViewModel { get; private set; }

        public PreviewPanelView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                LocalizeUI();
                if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                Helpers.CursorHelper.SetHandCursor(CenterPlayButton);
            };
            this.Unloaded += (s, e) =>
            {
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
                _seekTimer?.Stop();
            };
        }

        private static readonly Dictionary<string, ILanguage> _extToLanguage = new(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = Languages.CSharp,
            [".c"] = Languages.Cpp,
            [".cpp"] = Languages.Cpp,
            [".h"] = Languages.Cpp,
            [".hpp"] = Languages.Cpp,
            [".css"] = Languages.Css,
            [".html"] = Languages.Html,
            [".htm"] = Languages.Html,
            [".js"] = Languages.JavaScript,
            [".jsx"] = Languages.JavaScript,
            [".ts"] = Languages.Typescript,
            [".tsx"] = Languages.Typescript,
            [".json"] = Languages.JavaScript,
            [".xml"] = Languages.Xml,
            [".xaml"] = Languages.Xml,
            [".csproj"] = Languages.Xml,
            [".svg"] = Languages.Xml,
            [".sql"] = Languages.Sql,
            [".php"] = Languages.Php,
            [".ps1"] = Languages.PowerShell,
            [".psm1"] = Languages.PowerShell,
            [".java"] = Languages.Java,
            [".vb"] = Languages.VbDotNet,
            [".fs"] = Languages.FSharp,
            [".fsx"] = Languages.FSharp,
            [".md"] = Languages.Markdown,
            [".markdown"] = Languages.Markdown,
        };

        public void Initialize(PreviewPanelViewModel viewModel)
        {
            ViewModel = viewModel;
            RootPanel.DataContext = ViewModel;
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PreviewPanelViewModel.TextPreview))
                ApplySyntaxHighlighting();
        }

        private void ApplySyntaxHighlighting()
        {
            CodePreviewBlock.Blocks.Clear();
            var text = ViewModel?.TextPreview;
            if (string.IsNullOrEmpty(text)) return;

            var ext = ViewModel?.TextFileExtension ?? "";
            if (_extToLanguage.TryGetValue(ext, out var language))
            {
                try
                {
                    var theme = ActualTheme == ElementTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
                    var formatter = new RichTextBlockFormatter(theme);
                    formatter.FormatRichTextBlock(text, language, CodePreviewBlock);
                    return;
                }
                catch
                {
                    // 하이라이팅 실패 시 단색 폴백
                    CodePreviewBlock.Blocks.Clear();
                }
            }

            // 미지원 확장자 또는 폴백: 단색 텍스트
            var para = new Paragraph();
            para.Inlines.Add(new Run { Text = text });
            CodePreviewBlock.Blocks.Add(para);
        }

        public void UpdatePreview(FileSystemViewModel? selectedItem)
        {
            ResetMediaState();
            ViewModel?.OnSelectionChanged(selectedItem);
        }

        public void StopMedia()
        {
            try
            {
                _seekTimer?.Stop();
                if (PreviewMediaPlayer?.MediaPlayer != null)
                {
                    PreviewMediaPlayer.MediaPlayer.Pause();
                    PreviewMediaPlayer.MediaPlayer.Source = null;
                }
                ResetMediaUI();
            }
            catch { }
        }

        private void ResetMediaUI()
        {
            CenterPlayButton.Visibility = Visibility.Visible;
            CenterPlayButton.IsEnabled = true;
            CenterPlayButton.Opacity = 1.0;
            ToolTipService.SetToolTip(CenterPlayButton, null);
            BottomControlBar.Visibility = Visibility.Collapsed;
            SeekSlider.Value = 0;
            TimeLabel.Text = "0:00";
        }

        private void ResetMediaState()
        {
            _seekTimer?.Stop();
            if (PreviewMediaPlayer?.MediaPlayer != null)
            {
                PreviewMediaPlayer.MediaPlayer.Pause();
                PreviewMediaPlayer.MediaPlayer.Source = null;
            }
            ResetMediaUI();
        }

        // ── Play 클릭 → 즉시 재생 UI 전환 ─────────────────────

        private void OnCenterPlayClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var player = PreviewMediaPlayer.MediaPlayer;
                if (player == null) return;

                player.Play();

                // 즉시 UI 전환: 중앙 Play 숨기고 하단 바 표시
                CenterPlayButton.Visibility = Visibility.Collapsed;
                BottomControlBar.Visibility = Visibility.Visible;
                StartSeekTimer();
            }
            catch { }
        }

        // ── Pause 클릭 → 정지 UI 복원 ─────────────────────────

        private void OnBottomPauseClick(object sender, RoutedEventArgs e)
        {
            try
            {
                PreviewMediaPlayer.MediaPlayer?.Pause();
                _seekTimer?.Stop();
                BottomControlBar.Visibility = Visibility.Collapsed;
                CenterPlayButton.Visibility = Visibility.Visible;
            }
            catch { }
        }

        // ── 시크 타이머: 진행 표시 + 디코딩 불가 감지 + 재생 완료 ──

        private void StartSeekTimer()
        {
            if (_seekTimer == null)
            {
                _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _seekTimer.Tick += OnSeekTimerTick;
            }
            _seekTimer.Start();
        }

        private void OnSeekTimerTick(object? sender, object e)
        {
            try
            {
                var session = PreviewMediaPlayer?.MediaPlayer?.PlaybackSession;
                if (session == null) { _seekTimer?.Stop(); return; }

                var state = session.PlaybackState;
                var pos = session.Position;
                var dur = session.NaturalDuration;

                // 진행바 업데이트
                if (dur.TotalSeconds > 0)
                {
                    SeekSlider.Value = pos.TotalSeconds / dur.TotalSeconds * 100;
                    TimeLabel.Text = FormatTime(pos);
                }

                // 재생 완료 감지
                if (state == MediaPlaybackState.Paused && dur.TotalSeconds > 0 && pos >= dur)
                {
                    _seekTimer?.Stop();
                    BottomControlBar.Visibility = Visibility.Collapsed;
                    CenterPlayButton.Visibility = Visibility.Visible;
                    SeekSlider.Value = 0;
                    TimeLabel.Text = "0:00";
                }
            }
            catch { }
        }

        private static string FormatTime(TimeSpan ts)
            => ts.TotalHours >= 1 ? ts.ToString(@"h\:mm\:ss") : ts.ToString(@"m\:ss");

        // ── 다국어 ─────────────────────────────────────────────

        private void LocalizeUI()
        {
            if (_loc == null) return;
            EmptyStateText.Text = _loc.Get("Preview_SelectFile");
            LabelType.Text = _loc.Get("Preview_Type");
            LabelSize.Text = _loc.Get("Preview_Size");
            LabelCreated.Text = _loc.Get("Preview_Created");
            LabelModified.Text = _loc.Get("Preview_Modified");
            LabelResolution.Text = _loc.Get("Preview_Resolution");
            LabelDuration.Text = _loc.Get("Preview_Duration");
            LabelArtist.Text = _loc.Get("Preview_Artist");
            LabelAlbum.Text = _loc.Get("Preview_Album");
            LabelGit.Text = _loc.Get("Preview_Git");
            LabelCompressed.Text = _loc.Get("Preview_Compressed");
            LabelOriginal.Text = _loc.Get("Preview_Original");
        }

        public void Cleanup()
        {
            _seekTimer?.Stop();
            StopMedia();
            if (ViewModel != null)
                ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
            ViewModel?.Dispose();
            ViewModel = null;
            RootPanel.DataContext = null;
        }
    }
}
