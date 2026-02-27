using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Span.Helpers
{
    /// <summary>
    /// bool → Thickness XAML 컨버터. 선택 상태에 따라 테두리 두께를 변경하는 데 사용.
    /// </summary>
    public class BoolToThicknessConverter : IValueConverter
    {
        public Thickness TrueThickness { get; set; }
        public Thickness FalseThickness { get; set; }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
                return TrueThickness;
            return FalseThickness;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
