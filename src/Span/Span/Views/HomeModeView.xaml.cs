using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Span.Views
{
    public sealed partial class HomeModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }

        private MainViewModel? _mainViewModel;
        private SettingsService? _settings;

        public ObservableCollection<DriveItem>? LocalDrives => _mainViewModel?.Drives;
        public ObservableCollection<DriveItem>? NetworkDrivesList => _mainViewModel?.NetworkDrives;
        public ObservableCollection<FavoriteItem>? Favorites => _mainViewModel?.Favorites;

        public Visibility HasNetworkDrives =>
            _mainViewModel?.NetworkDrives?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public MainViewModel? MainViewModel
        {
            get => _mainViewModel;
            set
            {
                if (_mainViewModel != null)
                    _mainViewModel.NetworkDrives.CollectionChanged -= OnNetworkDrivesChanged;

                _mainViewModel = value;

                if (_mainViewModel != null)
                {
                    _mainViewModel.NetworkDrives.CollectionChanged += OnNetworkDrivesChanged;
                    Bindings.Update();
                }
            }
        }

        public HomeModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                try
                {
                    _settings = App.Current.Services.GetService(typeof(SettingsService)) as SettingsService;
                }
                catch { }
            };

            this.Unloaded += (s, e) =>
            {
                if (_mainViewModel != null)
                {
                    _mainViewModel.NetworkDrives.CollectionChanged -= OnNetworkDrivesChanged;
                }
                _settings = null;
            };
        }

        private void OnNetworkDrivesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Bindings.Update();
        }

        /// <summary>
        /// Drive single-click via GridView.ItemClick (IsItemClickEnabled=True)
        /// </summary>
        private void OnDriveItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is DriveItem drive && _mainViewModel != null)
            {
                _mainViewModel.OpenDrive(drive);
                Helpers.DebugLogger.Log($"[HomeModeView] Drive clicked: {drive.Name}");
            }
        }

        /// <summary>
        /// Favorite single-click via GridView.ItemClick (IsItemClickEnabled=True)
        /// </summary>
        private void OnFavoriteItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem favorite && _mainViewModel != null)
            {
                _mainViewModel.NavigateToFavorite(favorite);
                Helpers.DebugLogger.Log($"[HomeModeView] Favorite clicked: {favorite.Name}");
            }
        }

        private void OnDriveRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (ContextMenuService == null || ContextMenuHost == null) return;
            if (sender is FrameworkElement fe && fe.DataContext is DriveItem drive)
            {
                var flyout = ContextMenuService.BuildDriveMenu(drive, ContextMenuHost);
                flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(fe)
                });
                e.Handled = true;
            }
        }

        private void OnFavoriteRightTapped(object sender, Microsoft.UI.Xaml.Input.RightTappedRoutedEventArgs e)
        {
            if (_settings != null && !_settings.ShowContextMenu) return;
            if (ContextMenuService == null || ContextMenuHost == null) return;
            if (sender is FrameworkElement fe && fe.DataContext is FavoriteItem favorite)
            {
                var flyout = ContextMenuService.BuildFavoriteMenu(favorite, ContextMenuHost);
                flyout.ShowAt(fe, new Microsoft.UI.Xaml.Controls.Primitives.FlyoutShowOptions
                {
                    Position = e.GetPosition(fe)
                });
                e.Handled = true;
            }
        }

        public void Cleanup()
        {
            try
            {
                if (_mainViewModel != null)
                {
                    _mainViewModel.NetworkDrives.CollectionChanged -= OnNetworkDrivesChanged;
                }

                if (DrivesGridView != null)
                    DrivesGridView.ItemsSource = null;
                if (NetworkDrivesGridView != null)
                    NetworkDrivesGridView.ItemsSource = null;
                if (FavoritesGridView != null)
                    FavoritesGridView.ItemsSource = null;
                RootPanel.DataContext = null;
                _mainViewModel = null;
            }
            catch { /* ignore during teardown */ }
        }
    }
}
