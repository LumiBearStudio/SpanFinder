using Span.Models;
using System.Collections.Generic;

namespace Span.Services
{
    /// <summary>
    /// 즐겨찾기(빠른 액세스) 관리 서비스 인터페이스.
    /// JSON 파일로 영속화하며, 추가/제거/기본값 관리를 담당한다.
    /// </summary>
    public interface IFavoritesService
    {
        List<FavoriteItem> GetDefaultFavorites();
        List<FavoriteItem> LoadFavorites();
        void SaveFavorites(List<FavoriteItem> favorites);
        List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing);
        List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing);
    }
}
