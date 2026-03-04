using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using System.Reflection;

namespace Span.Helpers;

/// <summary>
/// Sets ProtectedCursor on any UIElement via reflection (WinUI 3 workaround).
/// Also provides IsHandCursor attached property for XAML Style usage.
/// </summary>
public static class CursorHelper
{
    private static readonly PropertyInfo? _protectedCursorProp =
        typeof(UIElement).GetProperty("ProtectedCursor",
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    // ── Attached property: helpers:CursorHelper.IsHandCursor="True" ──

    public static readonly DependencyProperty IsHandCursorProperty =
        DependencyProperty.RegisterAttached("IsHandCursor", typeof(bool), typeof(CursorHelper),
            new PropertyMetadata(false, OnIsHandCursorChanged));

    public static bool GetIsHandCursor(DependencyObject obj) => (bool)obj.GetValue(IsHandCursorProperty);
    public static void SetIsHandCursor(DependencyObject obj, bool value) => obj.SetValue(IsHandCursorProperty, value);

    private static void OnIsHandCursorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement fe && e.NewValue is true)
        {
            if (fe.IsLoaded)
                ApplyHandCursor(fe);
            else
                fe.Loaded += OnElementLoaded;
        }
    }

    private static void OnElementLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement fe)
        {
            fe.Loaded -= OnElementLoaded;
            ApplyHandCursor(fe);
        }
    }

    private static void ApplyHandCursor(UIElement element)
    {
        try
        {
            _protectedCursorProp?.SetValue(element,
                InputSystemCursor.Create(InputSystemCursorShape.Hand));
        }
        catch { }
    }

    // ── Direct API ──

    public static void SetHandCursor(UIElement element) => ApplyHandCursor(element);

    public static void SetCursor(UIElement element, InputSystemCursorShape shape)
    {
        try
        {
            _protectedCursorProp?.SetValue(element, InputSystemCursor.Create(shape));
        }
        catch { }
    }
}
