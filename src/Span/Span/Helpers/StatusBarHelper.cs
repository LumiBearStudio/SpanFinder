using Microsoft.UI.Xaml;

namespace Span.Helpers
{
    public static class StatusBarHelper
    {
        public static Visibility ShowIfNotEmpty(string text)
            => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}
