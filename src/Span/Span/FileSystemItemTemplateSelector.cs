using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;

namespace Span
{
    public class FileSystemItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate FolderTemplate { get; set; }
        public DataTemplate FileTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item is Span.ViewModels.FolderViewModel)
            {
                return FolderTemplate;
            }
            else if (item is Span.ViewModels.FileViewModel)
            {
                return FileTemplate;
            }

            return base.SelectTemplateCore(item, container);
        }
    }
}
