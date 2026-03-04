using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Span.Helpers;

internal static class ViewRenameHelper
{
    /// <summary>
    /// F2 cycling 선택 영역을 TextBox에 적용.
    /// cycle 0: 이름만 (확장자 제외), cycle 1: 전체, cycle 2: 확장자만.
    /// 폴더일 경우 항상 전체 선택.
    /// </summary>
    internal static void ApplyRenameSelection(TextBox textBox, bool isFolder, int selectionCycle, DispatcherQueue dispatcherQueue)
    {
        textBox.Focus(FocusState.Keyboard);

        dispatcherQueue.TryEnqueue(DispatcherQueuePriority.Normal, () =>
        {
            if (!isFolder && !string.IsNullOrEmpty(textBox.Text))
            {
                int dotIndex = textBox.Text.LastIndexOf('.');
                if (dotIndex > 0)
                {
                    switch (selectionCycle)
                    {
                        case 0: // Name only (exclude extension)
                            textBox.Select(0, dotIndex);
                            break;
                        case 1: // All (including extension)
                            textBox.SelectAll();
                            break;
                        case 2: // Extension only
                            textBox.Select(dotIndex + 1, textBox.Text.Length - dotIndex - 1);
                            break;
                    }
                }
                else
                {
                    textBox.SelectAll();
                }
            }
            else
            {
                textBox.SelectAll();
            }
        });
    }
}
