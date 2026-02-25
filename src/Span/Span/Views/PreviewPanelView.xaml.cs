using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;
using Windows.Media.Playback;

namespace Span.Views
{
    public sealed partial class PreviewPanelView : UserControl
    {
        private bool _isCompactMode;
        public PreviewPanelViewModel? ViewModel { get; private set; }

        public PreviewPanelView()
        {
            this.InitializeComponent();
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

        public void Cleanup()
        {
            StopMedia();
            ViewModel?.Dispose();
            ViewModel = null;
            RootPanel.DataContext = null;
        }
    }
}
