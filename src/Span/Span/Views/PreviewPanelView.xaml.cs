using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;

namespace Span.Views
{
    public sealed partial class PreviewPanelView : UserControl
    {
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

        public void Cleanup()
        {
            StopMedia();
            ViewModel?.Dispose();
            ViewModel = null;
            RootPanel.DataContext = null;
        }
    }
}
