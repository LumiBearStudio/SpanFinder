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

        private void OnIconKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
        {
            // Check for rename mode
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected != null && selected.IsRenaming) return;

            // Check for Ctrl/Alt modifiers (let global handlers handle them)
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            if (ctrl || alt) return;

            switch (e.Key)
            {
                case Windows.System.VirtualKey.Enter:
                    HandleIconEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Let global handler handle Delete
                    break;

                case Windows.System.VirtualKey.F2:
                    // Let global handler handle F2 (Rename)
                    break;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                case Windows.System.VirtualKey.Left:
                case Windows.System.VirtualKey.Right:
                    // GridView handles arrow key navigation by default
                    break;
            }
        }

        private void HandleIconEnter()
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder
                ViewModel!.CurrentFolder!.SelectedChild = folder;
                Helpers.DebugLogger.Log($"[IconModeView] Enter: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[IconModeView] Enter: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[IconModeView] Error opening file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Focus the Icon GridView (called from MainWindow on view switch)
        /// </summary>
        public void FocusGridView()
        {
            IconGridView?.Focus(FocusState.Programmatic);
        }
    }
}
