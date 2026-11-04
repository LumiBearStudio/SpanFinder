using Span.Models;
using Span.ViewModels;
using System.Collections.Generic;

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
        void PerformCopyPath(string path);
        void PerformPaste(string targetFolderPath);
        void PerformDelete(string path, string itemName);
        void PerformRename(FileSystemViewModel item);
        void PerformOpen(FileSystemViewModel item);
        void PerformOpenDrive(DriveItem drive);
        void PerformOpenFavorite(FavoriteItem fav);
        void PerformNewFolder(string parentFolderPath);
        void PerformNewFile(string parentFolderPath, string fileName);
        void PerformNewFileFromShellNew(string parentFolderPath, ShellNewItem shellNewItem);
        void PerformCompress(string[] paths);
        void PerformExtractHere(string zipPath);
        void PerformExtractTo(string zipPath);
        void AddToFavorites(string path, string? groupId = null);
        /// <summary>Add the active pane's current folder (toast feedback).</summary>
        void AddCurrentFolderToFavorites(string? groupId = null);
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
        void PerformOpenInNewTab(string folderPath);
        void PerformOpenTerminal(string folderPath);
        void PerformRefresh();

        /// <summary>Issue #58: 폴더에 컬러 태그 지정/해제.</summary>
        void PerformSetFolderTag(FolderViewModel folder, Models.FolderTagColor color);
        void PerformUndo();
        void PerformShowProperties(string path);

        /// <summary>Prompt for a group name; <paramref name="addPath"/> is moved/added into the new group.</summary>
        void CreateFavoriteGroup(string? addPath = null);
        void RenameFavorite(FavoriteItem fav);
        void SetFavoriteColor(FavoriteItem fav, string? color);
        /// <summary>Navigate to the favorite's parent folder and select it (file favorites).</summary>
        void RevealFavorite(FavoriteItem fav);
        void RenameFavoriteGroup(string groupId);
        void DeleteFavoriteGroup(string groupId);
        void MoveFavoriteToGroup(string path, string? groupId);
        IReadOnlyList<FavoriteGroupViewModel> GetFavoriteGroups();
    }
}
