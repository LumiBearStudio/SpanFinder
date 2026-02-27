using Span.Models;
using Span.ViewModels;

namespace Span.Services
{
    /// <summary>
    /// 컨텍스트 메뉴 호스트 인터페이스. MainWindow가 구현하며,
    /// ContextMenuService가 메뉴 항목 실행 시 이 인터페이스를 통해 
    /// 파일 조작, 뷰 전환, 정렬 등의 액션을 MainWindow에 위임한다.
    /// </summary>
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
        void EditRemoteConnection(string connectionId);
        void PerformEjectDrive(DriveItem drive);
        void PerformDisconnectDrive(DriveItem drive);
        void SwitchViewMode(ViewMode mode);
        void ApplySort(string field);
        void ApplySortDirection(bool ascending);
        void ApplyGroupBy(string groupBy);
        string CurrentGroupBy { get; }
        void PerformSelectAll();
        void PerformSelectNone();
        void PerformInvertSelection();
    }
}
