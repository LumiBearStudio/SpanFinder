using System;
using Span.ViewModels;

namespace Span.Helpers
{
    /// <summary>
    /// Shared item interaction logic used by DetailsModeView, ListModeView, and IconModeView.
    /// </summary>
    public static class ViewItemHelper
    {
        /// <summary>
        /// Opens the selected file or navigates into the selected folder.
        /// Used by double-click and Enter key handlers.
        /// </summary>
        public static void OpenFileOrFolder(ExplorerViewModel? explorer, string viewName)
        {
            var selected = explorer?.CurrentFolder?.SelectedChild;
            if (selected == null) return;

            if (selected is FolderViewModel folder)
            {
                explorer!.NavigateIntoFolder(folder);
                DebugLogger.Log($"[{viewName}] Opening folder {folder.Name}");
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    _ = Windows.System.Launcher.LaunchUriAsync(new Uri(file.Path));
                    DebugLogger.Log($"[{viewName}] Opening file {file.Name}");
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[{viewName}] Error opening file: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Returns true if Ctrl or Alt modifier is currently held down.
        /// Used to skip view-level key handling when global shortcuts should take precedence.
        /// </summary>
        public static bool HasModifierKey()
        {
            var ctrl = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                       .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            var alt = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Menu)
                      .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            return ctrl || alt;
        }
    }
}
