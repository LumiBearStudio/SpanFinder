using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using System;

namespace Span.Helpers
{
    public class BoolToBrushConverter : DependencyObject, IValueConverter
    {
        public static readonly DependencyProperty TrueBrushProperty =
            DependencyProperty.Register(nameof(TrueBrush), typeof(Brush), typeof(BoolToBrushConverter), new PropertyMetadata(null));

        public static readonly DependencyProperty FalseBrushProperty =
            DependencyProperty.Register(nameof(FalseBrush), typeof(Brush), typeof(BoolToBrushConverter), new PropertyMetadata(null));

        public Brush TrueBrush
        {
            get => (Brush)GetValue(TrueBrushProperty);
            set => SetValue(TrueBrushProperty, value);
        }

        public Brush FalseBrush
        {
            get => (Brush)GetValue(FalseBrushProperty);
            set => SetValue(FalseBrushProperty, value);
        }

        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is bool b && b)
                return TrueBrush;
            return FalseBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language)
        {
            throw new NotImplementedException();
        }
    }
}
