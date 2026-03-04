using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Span.Helpers;

internal static class VisualTreeHelpers
{
    /// <summary>
    /// 비주얼 트리에서 지정한 타입의 자식 요소를 재귀 탐색.
    /// </summary>
    internal static T? FindChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            var result = FindChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}
