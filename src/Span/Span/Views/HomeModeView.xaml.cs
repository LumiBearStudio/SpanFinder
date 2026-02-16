using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.ViewModels;
using System.Collections.ObjectModel;

namespace Span.Views
{
    public sealed partial class HomeModeView : UserControl
    {
        private MainViewModel? _mainViewModel;

        public ObservableCollection<FavoriteItem>? Favorites => _mainViewModel?.Favorites;
        public ObservableCollection<FavoriteItem>? RecentFolders => _mainViewModel?.RecentFolders;

        public HomeModeView()
        {
            this.InitializeComponent();

            this.Loaded += (s, e) =>
            {
                if (this.XamlRoot?.Content is FrameworkElement root &&
                    root.DataContext is MainViewModel mainVm)
                {
                    _mainViewModel = mainVm;
                    RootPanel.DataContext = _mainViewModel;
                    Bindings.Update();
                }
            };
        }

        private void OnFavoriteClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem favorite && _mainViewModel != null)
            {
                _mainViewModel.NavigateToFavorite(favorite);
                Helpers.DebugLogger.Log($"[HomeModeView] Favorite clicked: {favorite.Name}");
            }
        }

        private void OnRecentClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is FavoriteItem recent && _mainViewModel != null)
            {
                _mainViewModel.NavigateToFavorite(recent);
                Helpers.DebugLogger.Log($"[HomeModeView] Recent clicked: {recent.Name}");
            }
        }

        public void Cleanup()
        {
            _mainViewModel = null;
        }
    }
}
