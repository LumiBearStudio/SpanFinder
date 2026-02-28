using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using Span.ViewModels;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Span.Views
{
    /// <summary>
    /// Home 뷰 모드 UserControl.
    /// 로컬 드라이브, 클라우드 스토리지, 네트워크 위치, 즐겨찾기를
    /// GridView로 표시하는 시작 화면이다.
    /// 드라이브/즐겨찾기 클릭 시 탐색, 우클릭 컨텍스트 메뉴를 지원한다.
    /// </summary>
    public sealed partial class HomeModeView : UserControl
    {
        public ContextMenuService? ContextMenuService { get; set; }
        public IContextMenuHost? ContextMenuHost { get; set; }

        private MainViewModel? _mainViewModel;
        private SettingsService? _settings;
        private LocalizationService? _loc;

        public ObservableCollection<DriveItem>? LocalDrives => _mainViewModel?.Drives;
        public ObservableCollection<DriveItem>? CloudDrivesList => _mainViewModel?.CloudDrives;
        public ObservableCollection<DriveItem>? NetworkDrivesList => _mainViewModel?.NetworkAndRemoteDrives;
        public ObservableCollection<FavoriteItem>? Favorites => _mainViewModel?.Favorites;

        public Visibility HasCloudDrives =>
            _mainViewModel?.CloudDrives?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public Visibility HasNetworkDrives =>
            _mainViewModel?.NetworkAndRemoteDrives?.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public MainViewModel? MainViewModel
        {
            get => _mainViewModel;
            set
            {
                if (_mainViewModel != null)
                {
                    _mainViewModel.CloudDrives.CollectionChanged -= OnCloudDrivesChanged;
                    _mainViewModel.NetworkAndRemoteDrives.CollectionChanged -= OnNetworkDrivesChanged;
                }

                _mainViewModel = value;

                if (_mainViewModel != null)
                {
                    _mainViewModel.CloudDrives.CollectionChanged += OnCloudDrivesChanged;
                    _mainViewModel.NetworkAndRemoteDrives.CollectionChanged += OnNetworkDrivesChanged;
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
                    _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
                    LocalizeUI();
                    if (_loc != null) _loc.LanguageChanged += LocalizeUI;
                }
                catch { }
            };

            this.Unloaded += (s, e) =>
            {
                if (_mainViewModel != null)
                {
                    _mainViewModel.CloudDrives.CollectionChanged -= OnCloudDrivesChanged;
                    _mainViewModel.NetworkAndRemoteDrives.CollectionChanged -= OnNetworkDrivesChanged;
                }
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
                _settings = null;
            };
        }

        private void OnCloudDrivesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Bindings.Update();
        }

        private void OnNetworkDrivesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Bindings.Update();
        }

        private void LocalizeUI()
        {
            if (_loc == null) return;
            DevicesHeader.Text = _loc.Get("DevicesAndDrives");
            CloudHeader.Text = _loc.Get("CloudStorage");
            NetworkHeader.Text = _loc.Get("NetworkLocations");
            FavoritesHeader.Text = _loc.Get("Favorites");
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

        /// <summary>
        /// 홈 화면의 TextBlock/FontIcon 크기를 스케일 레벨에 맞춰 조정한다.
        /// 절대값 기반: baseline + level = 최종 FontSize.
        /// 범위: TextBlock/FontIcon 8~20 (28px 드라이브 아이콘, 24px 즐겨찾기 아이콘은 제외)
        /// </summary>
        public void ApplyIconFontScale(int level)
        {
            MainWindow.ApplyAbsoluteScaleToTree(this, level, 8, 20);
        }

        public void Cleanup()
        {
            try
            {
                if (_mainViewModel != null)
                {
                    _mainViewModel.CloudDrives.CollectionChanged -= OnCloudDrivesChanged;
                    _mainViewModel.NetworkAndRemoteDrives.CollectionChanged -= OnNetworkDrivesChanged;
                }

                if (DrivesGridView != null)
                    DrivesGridView.ItemsSource = null;
                if (CloudDrivesGridView != null)
                    CloudDrivesGridView.ItemsSource = null;
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
