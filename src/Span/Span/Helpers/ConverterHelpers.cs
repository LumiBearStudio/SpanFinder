using Microsoft.UI.Xaml.Media;

namespace Span.Helpers
{
    public static class ConverterHelpers
    {
        public static double ChevronAngle(bool expanded) => expanded ? 0 : -90;

        /// <summary>
        /// Active tab: Layer1 background (connects visually to toolbar).
        /// Inactive tab: semi-transparent for Mica blending.
        /// </summary>
        public static Brush TabBackground(bool isActive)
        {
            if (isActive)
                return (Brush)Microsoft.UI.Xaml.Application.Current.Resources["SpanBgLayer1Brush"];
            return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }
    }
}
