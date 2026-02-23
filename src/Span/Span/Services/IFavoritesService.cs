using Span.Models;
using System.Collections.Generic;

namespace Span.Services
{
    public interface IFavoritesService
    {
        List<FavoriteItem> GetDefaultFavorites();
        List<FavoriteItem> LoadFavorites();
        void SaveFavorites(List<FavoriteItem> favorites);
        List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing);
        List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing);
    }
}
