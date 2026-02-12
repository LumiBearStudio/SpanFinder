using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Span.Helpers
{
    /// <summary>
    /// bool → Visibility: true → Collapsed, false → Visible (역변환).
    /// 브레드크럼에서 마지막 세그먼트의 ">" 구분자를 숨기는 데 사용.
    /// </summary>
    public class InverseBoolToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
                return Visibility.Collapsed;
            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
