using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Span.Helpers
{
    /// <summary>
    /// Converts bool/string to Visibility.
    /// bool true → Visible, false → Collapsed.
    /// string non-empty → Visible, empty/null → Collapsed.
    /// ConverterParameter="Invert" inverts the result.
    /// </summary>
    public class VisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            bool visible = value switch
            {
                bool b => b,
                string s => !string.IsNullOrEmpty(s),
                null => false,
                _ => true
            };

            if (parameter is string p && p == "Invert")
                visible = !visible;

            return visible ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
            => throw new NotImplementedException();
    }
}
