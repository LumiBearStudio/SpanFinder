using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;

namespace Span
{
    /// <summary>
    /// Selects the appropriate DataTemplate for sidebar tree items:
    /// FavoriteItem (root nodes) or SidebarFolderNode (child subfolders).
    /// When using TreeView with RootNodes mode, the item parameter is a
    /// TreeViewNode whose Content holds the actual model object.
    /// </summary>
    public class SidebarItemTemplateSelector : DataTemplateSelector
    {
        public DataTemplate FavoriteTemplate { get; set; } = null!;
        public DataTemplate SubfolderTemplate { get; set; } = null!;

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            // In RootNodes mode, TreeView passes TreeViewNode as the item.
            // Unwrap to get the actual Content object for type matching.
            var content = item;
            if (item is TreeViewNode node)
                content = node.Content;

            if (content is FavoriteItem)
                return FavoriteTemplate;
            if (content is SidebarFolderNode)
                return SubfolderTemplate;

            return base.SelectTemplateCore(item, container);
        }
    }
}
