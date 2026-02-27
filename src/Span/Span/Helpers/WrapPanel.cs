using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Span.Helpers;

/// <summary>
/// 자식 요소를 수평으로 배치하고 공간이 부족하면 다음 줄로 넘기는 패널.
/// </summary>
public class WrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(8.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double), typeof(WrapPanel),
            new PropertyMetadata(8.0, (d, _) => ((WrapPanel)d).InvalidateMeasure()));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double x = 0, y = 0, rowHeight = 0, maxWidth = 0;
        var hsp = HorizontalSpacing;
        var vsp = VerticalSpacing;

        foreach (var child in Children)
        {
            child.Measure(availableSize);
            var sz = child.DesiredSize;

            if (x > 0 && x + sz.Width > availableSize.Width)
            {
                y += rowHeight + vsp;
                x = 0;
                rowHeight = 0;
            }

            maxWidth = Math.Max(maxWidth, x + sz.Width);
            rowHeight = Math.Max(rowHeight, sz.Height);
            x += sz.Width + hsp;
        }

        return new Size(
            double.IsInfinity(availableSize.Width) ? maxWidth : availableSize.Width,
            y + rowHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;
        var hsp = HorizontalSpacing;
        var vsp = VerticalSpacing;

        foreach (var child in Children)
        {
            var sz = child.DesiredSize;

            if (x > 0 && x + sz.Width > finalSize.Width)
            {
                y += rowHeight + vsp;
                x = 0;
                rowHeight = 0;
            }

            child.Arrange(new Rect(x, y, sz.Width, sz.Height));
            rowHeight = Math.Max(rowHeight, sz.Height);
            x += sz.Width + hsp;
        }

        return new Size(finalSize.Width, y + rowHeight);
    }
}
