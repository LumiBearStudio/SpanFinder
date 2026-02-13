using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;
using System.Linq;

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
                    UpdateSortIndicators();
                }
            }
        }

        private string _currentSortBy = "Name";
        private bool _isAscending = true;
        private bool _isLoaded = false;

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
            };
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
                // Navigate into folder
                ViewModel!.CurrentFolder!.SelectedChild = folder;
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

        private void OnSortByName(object sender, RoutedEventArgs e)
        {
            ToggleSort("Name");
        }

        private void OnSortByDateModified(object sender, RoutedEventArgs e)
        {
            ToggleSort("DateModified");
        }

        private void OnSortByType(object sender, RoutedEventArgs e)
        {
            ToggleSort("Type");
        }

        private void OnSortBySize(object sender, RoutedEventArgs e)
        {
            ToggleSort("Size");
        }

        private void ToggleSort(string sortBy)
        {
            if (_currentSortBy == sortBy)
            {
                // Toggle direction
                _isAscending = !_isAscending;
            }
            else
            {
                // New sort column, default to ascending
                _currentSortBy = sortBy;
                _isAscending = true;
            }

            SortItems(sortBy, _isAscending);
            UpdateSortIndicators();
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

        private void UpdateSortIndicators()
        {
            // Hide all indicators
            NameSortIcon.Visibility = Visibility.Collapsed;
            DateSortIcon.Visibility = Visibility.Collapsed;
            TypeSortIcon.Visibility = Visibility.Collapsed;
            SizeSortIcon.Visibility = Visibility.Collapsed;

            // Show indicator for current sort column
            FontIcon? activeIcon = _currentSortBy switch
            {
                "Name" => NameSortIcon,
                "DateModified" => DateSortIcon,
                "Type" => TypeSortIcon,
                "Size" => SizeSortIcon,
                _ => null
            };

            if (activeIcon != null)
            {
                activeIcon.Visibility = Visibility.Visible;
                // &#xE70D; = ChevronUp, &#xE70E; = ChevronDown
                activeIcon.Glyph = _isAscending ? "\uE70D" : "\uE70E";
            }
        }

        /// <summary>
        /// Focus the Details ListView (called from MainWindow on view switch)
        /// </summary>
        public void FocusListView()
        {
            DetailsListView?.Focus(FocusState.Programmatic);
        }
    }
}
