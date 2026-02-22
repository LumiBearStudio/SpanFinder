using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    public interface IContextMenuHost
    {
        bool HasClipboardContent { get; }
        void PerformCut(string path);
        void PerformCopy(string path);
        void PerformPaste(string targetFolderPath);
        void PerformDelete(string path, string itemName);
        void PerformRename(FileSystemViewModel item);
        void PerformOpen(FileSystemViewModel item);
        void PerformOpenDrive(DriveItem drive);
        void PerformOpenFavorite(FavoriteItem fav);
        void PerformNewFolder(string parentFolderPath);
        void PerformNewFile(string parentFolderPath, string fileName);
        void PerformCompress(string[] paths);
        void PerformExtractHere(string zipPath);
        void PerformExtractTo(string zipPath);
        void AddToFavorites(string path);
        void RemoveFromFavorites(string path);
        bool IsFavorite(string path);
        void RemoveRemoteConnection(string connectionId);
        void SwitchViewMode(ViewMode mode);
        void ApplySort(string field);
        void ApplySortDirection(bool ascending);
        void PerformSelectAll();
        void PerformSelectNone();
        void PerformInvertSelection();
    }
}
