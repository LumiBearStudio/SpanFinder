using Microsoft.UI.Xaml.Media;
using System.Threading.Tasks;

namespace Span.Services
{
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
        string GetDriveGlyph(string driveType);
        string GetIcon(string extension);
        Brush GetBrush(string extension);
    }
}
