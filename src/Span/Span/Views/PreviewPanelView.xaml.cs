using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
using Span.ViewModels;
using Windows.Media.Playback;

namespace Span.Views
{
    /// <summary>
    /// 미리보기 패널 UserControl.
    /// 선택된 파일의 이미지/오디오/비디오 미리보기, 메타데이터(유형, 크기, 날짜,
    /// 해상도, 아티스트, Git 상태 등) 표시를 담당한다.
    /// 컴팩트 모드에서는 미디어 컨트롤이 축소된 재생 버튼으로 대체된다.
    /// </summary>
    public sealed partial class PreviewPanelView : UserControl
    {
        private bool _isCompactMode;
        private LocalizationService? _loc;
        public PreviewPanelViewModel? ViewModel { get; private set; }

        public PreviewPanelView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                LocalizeUI();
                if (_loc != null) _loc.LanguageChanged += LocalizeUI;
            };
            this.Unloaded += (s, e) =>
            {
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            };
        }

        public void Initialize(PreviewPanelViewModel viewModel)
        {
            ViewModel = viewModel;
            RootPanel.DataContext = ViewModel;
        }

        /// <summary>
        /// Called externally when selection changes.
        /// </summary>
        public void UpdatePreview(FileSystemViewModel? selectedItem)
        {
            ViewModel?.OnSelectionChanged(selectedItem);
        }

        /// <summary>
        /// Stop media playback (when selection changes or panel closes).
        /// </summary>
        public void StopMedia()
        {
            try
            {
                if (PreviewMediaPlayer?.MediaPlayer != null)
                {
                    PreviewMediaPlayer.MediaPlayer.Pause();
                    PreviewMediaPlayer.Source = null;
                }
            }
            catch { /* ignore during cleanup */ }
        }

        private void OnMediaContainerSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.NewSize.Width < 180)
            {
                if (!_isCompactMode)
                {
                    _isCompactMode = true;
                    PreviewMediaPlayer.AreTransportControlsEnabled = false;
                    CompactPlayButton.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (_isCompactMode)
                {
                    _isCompactMode = false;
                    PreviewMediaPlayer.AreTransportControlsEnabled = true;
                    CompactPlayButton.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void OnCompactPlayClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var player = PreviewMediaPlayer.MediaPlayer;
                if (player == null) return;

                if (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing)
                {
                    player.Pause();
                    CompactPlayIcon.Glyph = "\uE768"; // Play
                }
                else
                {
                    player.Play();
                    CompactPlayIcon.Glyph = "\uE769"; // Pause
                }
            }
            catch { /* ignore */ }
        }

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
            StopMedia();
            ViewModel?.Dispose();
            ViewModel = null;
            RootPanel.DataContext = null;
        }
    }
}
