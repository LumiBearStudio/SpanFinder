using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;

namespace Span.Views
{
    public sealed partial class IconModeView : UserControl
    {
        private ExplorerViewModel? _viewModel;
        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                _viewModel = value;
                RootGrid.DataContext = _viewModel;
            }
        }

        private ViewMode _currentIconSize = ViewMode.IconMedium;
        private bool _isLoaded = false;

        public IconModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                _isLoaded = true;

                // Get ViewModel from MainWindow's DataContext
                if (this.XamlRoot?.Content is FrameworkElement root &&
                    root.DataContext is MainViewModel mainVm)
                {
                    ViewModel = mainVm.Explorer;
                    UpdateIconSize(mainVm.CurrentIconSize);
                }
            };
        }

        public void UpdateIconSize(ViewMode iconSize)
        {
            if (!_isLoaded || !Helpers.ViewModeExtensions.IsIconMode(iconSize))
                return;

            _currentIconSize = iconSize;

            // Switch template based on icon size
            string templateKey = iconSize switch
            {
                ViewMode.IconSmall => "SmallIconTemplate",
                ViewMode.IconMedium => "MediumIconTemplate",
                ViewMode.IconLarge => "LargeIconTemplate",
                ViewMode.IconExtraLarge => "ExtraLargeIconTemplate",
                _ => "MediumIconTemplate"
            };

            if (this.Resources.ContainsKey(templateKey))
            {
                IconGridView.ItemTemplate = (DataTemplate)this.Resources[templateKey];
                Helpers.DebugLogger.Log($"[IconModeView] Icon size updated: {Helpers.ViewModeExtensions.GetDisplayName(iconSize)} (Template: {templateKey})");
            }
        }

        private void OnItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FolderViewModel folder)
            {
                // Navigate into folder
                if (ViewModel?.CurrentFolder != null)
                {
                    ViewModel.CurrentFolder.SelectedChild = folder;
                }
            }
            else if (e.ClickedItem is FileViewModel file)
            {
                // Open file (TODO: implement file opening)
                if (ViewModel?.CurrentFolder != null)
                {
                    ViewModel.CurrentFolder.SelectedChild = file;
                }
            }
        }
    }
}
