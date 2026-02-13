using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;
using System.Linq;
using System;
using Windows.Storage;

namespace Span.Views
{
    public sealed partial class DetailsModeView : UserControl
    {
        private ExplorerViewModel? _viewModel;
        public ExplorerViewModel? ViewModel
        {
            get => _viewModel;
            set
            {
                _viewModel = value;
                RootGrid.DataContext = _viewModel;

                if (_viewModel != null && _isLoaded)
                {
                    SortItems("Name", true);
                }
            }
        }

        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isLoaded = false;
        private readonly ApplicationDataContainer _localSettings = ApplicationData.Current.LocalSettings;

        public DetailsModeView()
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
                }

                // Restore sort settings
                RestoreSortSettings();
            };

            this.Unloaded += OnUnloaded;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView.OnUnloaded] Starting cleanup...");

                // Save sort settings
                SaveSortSettings();

                // Disconnect ListView events
                if (DetailsListView != null)
                {
                    DetailsListView.DoubleTapped -= OnItemDoubleClick;
                    DetailsListView.KeyDown -= OnDetailsKeyDown;

                    // Clear bindings to prevent memory leaks
                    DetailsListView.ItemsSource = null;
                    DetailsListView.SelectedItem = null;
                }

                // Clear ViewModel reference
                ViewModel = null;

                // Unsubscribe from events
                this.Unloaded -= OnUnloaded;

                Helpers.DebugLogger.Log("[DetailsModeView.OnUnloaded] Cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.OnUnloaded] Error: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[DetailsModeView.OnUnloaded] Stack: {ex.StackTrace}");
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
                Helpers.DebugLogger.Log($"[DetailsModeView] DoubleClick: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[DetailsModeView] DoubleClick: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[DetailsModeView] Error opening file: {ex.Message}");
                }
            }
        }

        private void OnDetailsKeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
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
                    HandleDetailsEnter();
                    e.Handled = true;
                    break;

                case Windows.System.VirtualKey.Back:
                    // Navigate to parent folder
                    ViewModel?.NavigateUp();
                    e.Handled = true;
                    Helpers.DebugLogger.Log("[DetailsModeView] Backspace: Navigating to parent folder");
                    break;

                case Windows.System.VirtualKey.Delete:
                    // Let global handler handle Delete
                    break;

                case Windows.System.VirtualKey.F2:
                    // Let global handler handle F2 (Rename)
                    break;

                case Windows.System.VirtualKey.Up:
                case Windows.System.VirtualKey.Down:
                    // ListView handles Up/Down navigation by default
                    break;
            }
        }

        private void HandleDetailsEnter()
        {
            var selected = ViewModel?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                // Navigate into folder using manual navigation (bypasses auto-navigation check)
                ViewModel!.NavigateIntoFolder(folder);
                Helpers.DebugLogger.Log($"[DetailsModeView] Enter: Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                // Open file with default application
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    Helpers.DebugLogger.Log($"[DetailsModeView] Enter: Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[DetailsModeView] Error opening file: {ex.Message}");
                }
            }
        }

        private void OnHeaderClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string sortBy)
            {
                // Toggle sort direction if same column
                if (_currentSortBy == sortBy)
                {
                    _isAscending = !_isAscending;
                }
                else
                {
                    _currentSortBy = sortBy;
                    _isAscending = true;
                }

                // Apply sort
                SortItems(sortBy, _isAscending);

                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted by {sortBy} ({(_isAscending ? "Asc" : "Desc")})");
            }
        }

        private void SortItems(string sortBy, bool ascending)
        {
            if (ViewModel?.CurrentFolder == null || ViewModel.CurrentFolder.Children.Count == 0)
                return;

            var column = ViewModel.CurrentFolder;

            // CRITICAL: Save selection before sorting
            var savedSelection = column.SelectedChild;

            // Set sorting flag to prevent PropertyChanged events
            column.IsSorting = true;

            try
            {
                // Sort folders first, then files
                System.Collections.Generic.IEnumerable<FileSystemViewModel> sorted;

                switch (sortBy)
                {
                    case "Name":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance);
                        break;

                    case "DateModified":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.DateModifiedValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.DateModifiedValue);
                        break;

                    case "Type":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.FileType)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.FileType);
                        break;

                    case "Size":
                        sorted = ascending
                            ? column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.SizeValue)
                            : column.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.SizeValue);
                        break;

                    default:
                        return;
                }

                var sortedList = sorted.ToList();

                // Update collection
                column.Children.Clear();
                foreach (var item in sortedList)
                {
                    column.Children.Add(item);
                }

                // Restore selection
                if (savedSelection != null)
                {
                    column.SelectedChild = savedSelection;
                }

                Helpers.DebugLogger.Log($"[DetailsModeView] Sorted by {sortBy} ({(ascending ? "Ascending" : "Descending")}), {sortedList.Count} items");
            }
            finally
            {
                // Always clear sorting flag
                column.IsSorting = false;
            }
        }

        private void SaveSortSettings()
        {
            try
            {
                var composite = new ApplicationDataCompositeValue
                {
                    ["SortColumn"] = _currentSortBy,
                    ["SortAscending"] = _isAscending
                };

                _localSettings.Values["DetailsViewSort"] = composite;
                Helpers.DebugLogger.Log("[DetailsModeView] Sort settings saved");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error saving sort settings: {ex.Message}");
            }
        }

        private void RestoreSortSettings()
        {
            try
            {
                if (_localSettings.Values["DetailsViewSort"] is ApplicationDataCompositeValue composite)
                {
                    if (composite.TryGetValue("SortColumn", out var sortObj) && sortObj is string sortColumn)
                    {
                        _currentSortBy = sortColumn;
                    }

                    if (composite.TryGetValue("SortAscending", out var ascObj) && ascObj is bool ascending)
                    {
                        _isAscending = ascending;
                    }

                    // Apply restored sort
                    SortItems(_currentSortBy, _isAscending);

                    Helpers.DebugLogger.Log("[DetailsModeView] Sort settings restored");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView] Error restoring sort settings: {ex.Message}");
            }
        }

        /// <summary>
        /// Focus the Details ListView (called from MainWindow on view switch)
        /// </summary>
        public void FocusDataGrid()
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }

        // Keep old method for compatibility
        public void FocusListView() => FocusDataGrid();

        /// <summary>
        /// CRITICAL: Cleanup called from MainWindow.OnClosed BEFORE views are unloaded
        /// This prevents WinUI crash by disconnecting bindings early
        /// </summary>
        public void Cleanup()
        {
            try
            {
                Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] Starting early cleanup...");

                if (DetailsListView != null)
                {
                    // Disconnect events FIRST
                    DetailsListView.DoubleTapped -= OnItemDoubleClick;
                    DetailsListView.KeyDown -= OnDetailsKeyDown;

                    // Clear data bindings to prevent WinUI internal crash
                    DetailsListView.ItemsSource = null;
                    DetailsListView.SelectedItem = null;

                    Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] ListView disconnected");
                }

                // Clear ViewModel reference
                ViewModel = null;

                Helpers.DebugLogger.Log("[DetailsModeView.Cleanup] Early cleanup complete");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DetailsModeView.Cleanup] Error: {ex.Message}");
            }
        }
    }
}
