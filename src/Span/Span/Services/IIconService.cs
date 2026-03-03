using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// 아이콘 서비스 인터페이스. 아이콘 팩(Remix/Phosphor/Tabler)에 따라
    /// 파일/폴더/드라이브 등의 글리프, 브러시, 폰트 패밀리를 제공한다.
    /// </summary>
    public interface IIconService
    {
        string FolderIcon { get; }
        Brush FolderBrush { get; }
        string FontFamilyPath { get; }
        string FolderGlyph { get; }
        string FolderOpenGlyph { get; }
        string FileDefaultGlyph { get; }
        string DriveGlyph { get; }
        string RemovableGlyph { get; }
        string CdRomGlyph { get; }
        string NetworkGlyph { get; }
        string ServerGlyph { get; }
        string ChevronRightGlyph { get; }
        string NewFolderGlyph { get; }
        string SplitViewGlyph { get; }

        Task LoadAsync();
        void UpdateTheme(bool isLightTheme);
        string GetDriveGlyph(string driveType);
        string GetIcon(string extension);
        Brush GetBrush(string extension);
    }
}
