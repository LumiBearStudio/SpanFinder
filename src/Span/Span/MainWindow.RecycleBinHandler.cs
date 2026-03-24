using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Span.Models;
using Span.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace Span
{
    public sealed partial class MainWindow
    {
        private bool _recycleBinEventsWired;

        private void EnsureRecycleBinEventsWired()
        {
            if (_recycleBinEventsWired) return;
            _recycleBinEventsWired = true;

            RecycleBinView.RestoreRequested += OnRecycleBinRestoreRequested;
            RecycleBinView.DeletePermanentlyRequested += OnRecycleBinDeletePermanentlyRequested;
            RecycleBinView.EmptyRequested += OnRecycleBinEmptyRequested;
            RecycleBinView.OpenOriginalLocationRequested += OnRecycleBinOpenOriginalLocation;
            RecycleBinView.PropertiesRequested += OnRecycleBinPropertiesRequested;
            RecycleBinView.RefreshCompleted += (_, _) => _ = ViewModel.RefreshRecycleBinInfoAsync();
            RecycleBinView.StatusChanged += OnRecycleBinStatusChanged;
        }

        private async Task LoadRecycleBinViewAsync()
        {
            EnsureRecycleBinEventsWired();
            await RecycleBinView.LoadItemsAsync();
        }

        private async void OnRecycleBinRestoreRequested(object? sender, List<RecycleBinItem> items)
        {
            try
            {
                var service = App.Current.Services.GetRequiredService<RecycleBinService>();
                var result = await service.RestoreAsync(items);

                if (result.Success || result.AffectedPaths.Count > 0)
                {
                    ViewModel.ShowToast(string.Format(
                        _loc.Get("RecycleBin_RestoredCount"),
                        result.AffectedPaths.Count));
                }

                if (!string.IsNullOrEmpty(result.ErrorMessage))
                {
                    ViewModel.ShowToast(result.ErrorMessage);
                }

                await RecycleBinView.LoadItemsAsync();
                _ = ViewModel.RefreshRecycleBinInfoAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] Restore error: {ex.Message}");
            }
        }

        private async void OnRecycleBinDeletePermanentlyRequested(object? sender, List<RecycleBinItem> items)
        {
            try
            {
                // Confirm permanent delete
                string content = items.Count == 1
                    ? string.Format(_loc.Get("DeletePermanentConfirmContent"), items[0].Name)
                    : string.Format(_loc.Get("DeletePermanentConfirmMultiple"), items.Count);

                var dialog = new ContentDialog
                {
                    Title = _loc.Get("DeletePermanentConfirmTitle"),
                    Content = content,
                    PrimaryButtonText = _loc.Get("Delete"),
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var dialogResult = await ShowContentDialogSafeAsync(dialog);
                if (dialogResult != ContentDialogResult.Primary) return;
                if (_isClosed) return;

                var service = App.Current.Services.GetRequiredService<RecycleBinService>();
                var result = await service.DeletePermanentlyAsync(items);

                if (result.Success || result.AffectedPaths.Count > 0)
                {
                    ViewModel.ShowToast(string.Format(
                        _loc.Get("RecycleBin_DeletedCount"),
                        result.AffectedPaths.Count));
                }

                await RecycleBinView.LoadItemsAsync();
                _ = ViewModel.RefreshRecycleBinInfoAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] Permanent delete error: {ex.Message}");
            }
        }

        private async void OnRecycleBinEmptyRequested(object? sender, EventArgs e)
        {
            try
            {
                var dialog = new ContentDialog
                {
                    Title = _loc.Get("RecycleBin_EmptyConfirmTitle"),
                    Content = _loc.Get("RecycleBin_EmptyConfirmContent"),
                    PrimaryButtonText = _loc.Get("RecycleBin_Empty"),
                    CloseButtonText = _loc.Get("Cancel"),
                    XamlRoot = this.Content.XamlRoot,
                    DefaultButton = ContentDialogButton.Close
                };

                var dialogResult = await ShowContentDialogSafeAsync(dialog);
                if (dialogResult != ContentDialogResult.Primary) return;
                if (_isClosed) return;

                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
                var service = App.Current.Services.GetRequiredService<RecycleBinService>();
                bool success = await service.EmptyAsync(hwnd);

                if (success)
                {
                    ViewModel.ShowToast(_loc.Get("RecycleBin_Emptied"));
                }

                await RecycleBinView.LoadItemsAsync();
                _ = ViewModel.RefreshRecycleBinInfoAsync();
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] Empty error: {ex.Message}");
            }
        }

        private void OnRecycleBinOpenOriginalLocation(object? sender, RecycleBinItem item)
        {
            try
            {
                string dir = item.OriginalLocation;
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    ViewModel.AddNewTab();
                    var explorer = ViewModel.ActiveExplorer;
                    if (explorer != null)
                    {
                        _ = explorer.NavigateToPath(dir);
                    }
                }
                else
                {
                    ViewModel.ShowToast(string.Format(
                        _loc.Get("RecycleBin_LocationNotFound"), dir ?? ""));
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] OpenOriginalLocation error: {ex.Message}");
            }
        }

        private void OnRecycleBinPropertiesRequested(object? sender, RecycleBinItem item)
        {
            try
            {
                ShowShellProperties(item.Path);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] Properties error: {ex.Message}");
            }
        }

        /// <summary>
        /// Shell 속성 다이얼로그를 표시한다.
        /// </summary>
        private static void ShowShellProperties(string path)
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                // Use shell verb "properties" via ShellExecuteEx
                var sei = new NativeMethods_ShellExecuteInfo
                {
                    cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods_ShellExecuteInfo>(),
                    lpVerb = "properties",
                    lpFile = path,
                    nShow = 1, // SW_SHOWNORMAL
                    fMask = 0x0000000C // SEE_MASK_INVOKEIDLIST
                };
                ShellExecuteExW(ref sei);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[RecycleBin] ShowShellProperties error: {ex.Message}");
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct NativeMethods_ShellExecuteInfo
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string lpVerb;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string lpFile;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string? lpParameters;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)]
            public string? lpClass;
            public IntPtr hkeyClass;
            public uint dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool ShellExecuteExW(ref NativeMethods_ShellExecuteInfo lpExecInfo);

        private void OnRecycleBinStatusChanged(object? sender, (int ItemCount, int SelectedCount, long TotalSize, long SelectedSize) e)
        {
            ViewModel.RecycleBinViewItemCount = e.ItemCount;
            ViewModel.RecycleBinViewSelectedCount = e.SelectedCount;
            ViewModel.RecycleBinViewTotalSize = e.TotalSize;
            ViewModel.RecycleBinViewSelectedSize = e.SelectedSize;
            ViewModel.UpdateStatusBar();
            SyncRecycleBinToolbarButtons();
        }

        /// <summary>
        /// 휴지통 모드에서 키보드 이벤트를 처리한다.
        /// 호출자(KeyboardHandler)에서 RecycleBin 모드일 때만 호출.
        /// true를 반환하면 호출자는 e.Handled = true 후 return.
        /// </summary>
        internal async Task<bool> HandleRecycleBinKeyAsync(Windows.System.VirtualKey key, bool ctrl, bool shift, bool alt)
        {
            if (ViewModel.CurrentViewMode != ViewMode.RecycleBin) return false;

            switch (key)
            {
                case Windows.System.VirtualKey.Enter when !ctrl && !shift && !alt:
                    // Properties
                    var propsItems = RecycleBinView.GetSelectedItems();
                    if (propsItems.Count == 1)
                        ShowShellProperties(propsItems[0].Path);
                    return true;

                case Windows.System.VirtualKey.Delete:
                    // Permanent delete
                    var delItems = RecycleBinView.GetSelectedItems();
                    if (delItems.Count > 0)
                        OnRecycleBinDeletePermanentlyRequested(this, delItems);
                    return true;

                case Windows.System.VirtualKey.F5:
                    // Refresh
                    await RecycleBinView.LoadItemsAsync();
                    _ = ViewModel.RefreshRecycleBinInfoAsync();
                    return true;

                case Windows.System.VirtualKey.A when ctrl:
                    // Select all
                    RecycleBinView.SelectAll();
                    return true;

                case Windows.System.VirtualKey.Z when ctrl:
                    // Ctrl+Z = Restore selected
                    var restoreItems = RecycleBinView.GetSelectedItems();
                    if (restoreItems.Count > 0)
                        OnRecycleBinRestoreRequested(this, restoreItems);
                    return true;

                // Block clipboard operations
                case Windows.System.VirtualKey.C when ctrl:
                case Windows.System.VirtualKey.X when ctrl:
                case Windows.System.VirtualKey.V when ctrl:
                    return true;

                case Windows.System.VirtualKey.Escape when !ctrl && !shift && !alt:
                    // 검색 필터 활성 시 → 필터 초기화만 (일반 탭과 동일 패턴)
                    if (!string.IsNullOrEmpty(SearchBox.Text))
                    {
                        RecycleBinView.FilterItems(null);
                        SearchBox.Text = string.Empty;
                    }
                    // 비어있으면 아무 동작 없음 (휴지통 뷰 유지 — 나가려면 사이드바/주소바 사용)
                    return true;
            }

            return false;
        }

        #region Unified Bar RecycleBin Commands

        private void OnRecycleBinToolbarRestoreClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
            => RecycleBinView.InvokeRestore();

        private void OnRecycleBinToolbarDeleteClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
            => RecycleBinView.InvokeDeletePermanently();

        private void OnRecycleBinToolbarEmptyClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
            => RecycleBinView.InvokeEmpty();

        private void OnRecycleBinToolbarRefreshClick(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
            => RecycleBinView.InvokeRefresh();

        /// <summary>
        /// RecycleBinModeView의 선택 변경 시 Unified Bar의 RecycleBin 버튼 IsEnabled 동기화.
        /// </summary>
        private void SyncRecycleBinToolbarButtons()
        {
            if (ViewModel.CurrentViewMode != ViewMode.RecycleBin) return;
            bool hasSelection = RecycleBinView.HasSelection;
            ToolbarRestoreButton.IsEnabled = hasSelection;
            ToolbarDeletePermButton.IsEnabled = hasSelection;
        }

        #endregion
    }
}
