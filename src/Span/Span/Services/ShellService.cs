using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Span.Services
{
    public class ShellService : IShellService
    {
        public async Task OpenWithAsync(string filePath)
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
                // Validate path to prevent command injection
                if (string.IsNullOrWhiteSpace(path) || path.IndexOfAny(new[] { '"', '\n', '\r' }) >= 0)
                    return;

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

        /// <summary>
        /// Opens a terminal at the given directory using the user's preferred terminal app.
        /// </summary>
        public void OpenTerminal(string directoryPath, string terminalType = "wt")
        {
            try
            {
                var (fileName, arguments) = terminalType switch
                {
                    "powershell" => ("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{EscapePowerShell(directoryPath)}'\""),
                    "cmd" => ("cmd.exe", $"/K cd /d \"{EscapeCmd(directoryPath)}\""),
                    _ => ("wt.exe", $"-d \"{directoryPath.Replace("\"", "")}\"") // Windows Terminal
                };

                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] OpenTerminal error: {ex.Message}");
            }
        }

        #region Escape Helpers

        private static string EscapePowerShell(string s) => s.Replace("'", "''");
        private static string EscapeCmd(string s) => s.Replace("\"", "\"\"");

        #endregion

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
