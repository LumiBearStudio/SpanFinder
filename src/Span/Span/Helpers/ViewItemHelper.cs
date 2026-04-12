using System;
using Microsoft.Extensions.DependencyInjection;
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
                // 검색 결과에서 폴더 더블클릭 → 검색 취소 + 해당 경로로 이동
                if (explorer!.HasActiveSearchResults)
                {
                    explorer.CancelRecursiveSearch();
                    _ = explorer.NavigateToPath(folder.Path);
                    DebugLogger.Log($"[{viewName}] Search → navigate to folder {folder.Name}");
                }
                else
                {
                    explorer.NavigateIntoFolder(folder);
                    DebugLogger.Log($"[{viewName}] Opening folder {folder.Name}");
                }
            }
            else if (selected is FileViewModel file)
            {
                try
                {
                    if (ArchivePathHelper.IsArchivePath(file.Path))
                    {
                        MainWindow.OpenArchiveEntryStaticAsync(file.Path);
                        DebugLogger.Log($"[{viewName}] Extracting archive entry {file.Name}");
                    }
                    else if (file.Path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                    {
                        // .lnk 바로가기: 대상이 폴더면 네비게이션, 파일이면 ShellExecute
                        var target = Services.FileSystemService.ResolveShellLink(file.Path);
                        if (!string.IsNullOrEmpty(target) && System.IO.Directory.Exists(target))
                        {
                            _ = explorer!.NavigateToPath(target);
                            DebugLogger.Log($"[{viewName}] .lnk → navigate to folder {target}");
                        }
                        else
                        {
                            var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                            shellService.OpenFile(file.Path);
                            DebugLogger.Log($"[{viewName}] .lnk → opening target file {file.Name}");
                        }
                    }
                    else
                    {
                        var shellService = App.Current.Services.GetRequiredService<Services.ShellService>();
                        shellService.OpenFile(file.Path);
                        DebugLogger.Log($"[{viewName}] Opening file {file.Name}");
                    }
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
