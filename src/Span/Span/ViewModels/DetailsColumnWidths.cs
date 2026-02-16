using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;

namespace Span.ViewModels
{
    /// <summary>
    /// Shared column widths for Details view header-item synchronization.
    /// Used as a StaticResource in DetailsModeView.xaml.
    /// Header GridSplitter resizes update these properties,
    /// and item template ColumnDefinitions bind to them via {Binding Source={StaticResource}}.
    /// </summary>
    public partial class DetailsColumnWidths : ObservableObject
    {
        [ObservableProperty]
        private GridLength _dateWidth = new(200, GridUnitType.Pixel);

        [ObservableProperty]
        private GridLength _typeWidth = new(150, GridUnitType.Pixel);

        [ObservableProperty]
        private GridLength _sizeWidth = new(100, GridUnitType.Pixel);
    }
}
