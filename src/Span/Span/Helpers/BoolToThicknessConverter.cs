using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using System;

namespace Span.Helpers
{
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
