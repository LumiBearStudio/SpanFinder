using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;

namespace Span.Services
{
    public class ShellService
    {
        public async void OpenWithAsync(string filePath)
        {
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(filePath);
                var options = new Windows.System.LauncherOptions
                {
                    DisplayApplicationPicker = true
                };
                await Windows.System.Launcher.LaunchFileAsync(file, options);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] OpenWith error: {ex.Message}");
            }
        }

        public void ShowProperties(string path)
        {
            try
            {
                var info = new SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                    lpVerb = "properties",
                    lpFile = path,
                    nShow = 0, // SW_HIDE
                    fMask = SEE_MASK_INVOKEIDLIST
                };
                ShellExecuteEx(ref info);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] ShowProperties error: {ex.Message}");
            }
        }

        public void OpenInExplorer(string path)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] OpenInExplorer error: {ex.Message}");
            }
        }

        public void CopyPathToClipboard(string path)
        {
            try
            {
                var dataPackage = new DataPackage();
                dataPackage.SetText(path);
                Clipboard.SetContent(dataPackage);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] CopyPath error: {ex.Message}");
            }
        }

        #region P/Invoke

        private const int SEE_MASK_INVOKEIDLIST = 0x0000000C;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHELLEXECUTEINFO
        {
            public int cbSize;
            public int fMask;
            public IntPtr hwnd;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpVerb;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpFile;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
            public int nShow;
            public IntPtr hInstApp;
            public IntPtr lpIDList;
            [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
            public IntPtr hkeyClass;
            public int dwHotKey;
            public IntPtr hIcon;
            public IntPtr hProcess;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

        #endregion
    }
}
