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

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Helpers.DebugLogger.Log("[IconModeView.OnUnloaded] Starting cleanup...");

                // Disconnect GridView events
                if (IconGridView != null)
                {
                    IconGridView.DoubleTapped -= OnItemDoubleClick;
                    IconGridView.KeyDown -= OnIconKeyDown;

                    // Clear bindings to prevent memory leaks
                    IconGridView.ItemsSource = null;
                    IconGridView.SelectedItem = null;
                }

                // Clear ViewModel reference
                ViewModel = null;

                // Unsubscribe from events
                this.Unloaded -= OnUnloaded;

                Helpers.DebugLogger.Log("[IconModeView.OnUnloaded] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[IconModeView.OnUnloaded] Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[IconModeView.OnUnloaded] Stack: {ex.StackTrace}");
            }
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

        private void OnItemDoubleClick(object sender, Microsoft.UI.Xaml.Input.DoubleTappedRoutedEventArgs e)
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[IconModeView] DoubleClick: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[IconModeView] DoubleClick: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[IconModeView] Error opening file: {ex.Message}");
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

                case Windows.System.VirtualKey.Back:
                    // Navigate to parent folder
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    Helpers.DebugLogger.Log("[IconModeView] Backspace: Navigating to parent folder");
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
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
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

        /// <summary>
        /// CRITICAL: Cleanup called from MainWindow.OnClosed BEFORE views are unloaded
        /// This prevents WinUI crash by disconnecting bindings early
        /// </summary>
        public void Cleanup()
        {
            try
            {
                Helpers.DebugLogger.Log("[IconModeView.Cleanup] Starting early cleanup...");

                if (IconGridView != null)
                {
                    // Disconnect events FIRST
                    IconGridView.DoubleTapped -= OnItemDoubleClick;
                    IconGridView.KeyDown -= OnIconKeyDown;

                    // Clear data bindings to prevent WinUI internal crash
                    IconGridView.ItemsSource = null;
                    IconGridView.SelectedItem = null;

                    Helpers.DebugLogger.Log("[IconModeView.Cleanup] GridView disconnected");
                }

                // Clear ViewModel reference
                ViewModel = null;

                Helpers.DebugLogger.Log("[IconModeView.Cleanup] Early cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[IconModeView.Cleanup] Error: {ex.Message}");
            }
        }
    }
}
