using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Models;

namespace Span.Helpers
{
    /// <summary>
    /// Picks header vs item template for the unified favorites sidebar list.
    /// </summary>
    public sealed class FavoriteSidebarTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? HeaderTemplate { get; set; }
        public DataTemplate? ItemTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            if (item is FavoriteGroupHeaderRow)
                return HeaderTemplate;
            return ItemTemplate;
        }

        protected override DataTemplate? SelectTemplateCore(object item, DependencyObject container)
            => SelectTemplateCore(item);
    }
}