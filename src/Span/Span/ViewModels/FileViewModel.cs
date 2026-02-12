using Span.Models;

namespace Span.ViewModels
{
    public class FileViewModel : FileSystemViewModel
    {
        public FileViewModel(FileItem model) : base(model)
        {
        }

        /// <summary>
        /// 확장자 기반 아이콘 (Segoe Fluent Icons)
        /// </summary>
        public override string IconGlyph => Services.IconService.Current.GetIcon(((FileItem)_model).FileType);
        // Legacy method removed

        public override Microsoft.UI.Xaml.Media.Brush IconBrush => Services.IconService.Current.GetBrush(((FileItem)_model).FileType);
    }
}
