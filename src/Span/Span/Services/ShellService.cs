using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Span.Services
{
    public class ShellService : IShellService
    {
        /// <summary>
        /// Fired just before a file is opened, with the file name as argument.
        /// Subscribe to show immediate UI feedback (e.g. toast notification).
        /// </summary>
        public event Action<string>? FileOpening;

        /// <summary>
        /// Open a file with its default application using Win32 ShellExecute.
        /// Faster than WinRT Launcher.LaunchUriAsync — no URI parsing overhead,
        /// and handles ISO/disc images, executables, and all file types natively.
        /// </summary>
        public void OpenFile(string filePath)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(filePath)) return;

                var fileName = System.IO.Path.GetFileName(filePath);
                FileOpening?.Invoke(fileName);

                Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] OpenFile error: {ex.Message}");
            }
        }

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

        public void EjectDrive(string drivePath)
        {
            try
            {
                var info = new SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                    lpVerb = "eject",
                    lpFile = drivePath,
                    nShow = 0,
                    fMask = SEE_MASK_INVOKEIDLIST
                };
                ShellExecuteEx(ref info);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] EjectDrive error: {ex.Message}");
            }
        }

        public bool DisconnectNetworkDrive(string drivePath)
        {
            try
            {
                int result = WNetCancelConnection2W(drivePath.TrimEnd('\\'), 0, true);
                return result == 0;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] DisconnectNetworkDrive error: {ex.Message}");
                return false;
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

        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        private static extern int WNetCancelConnection2W(string lpName, int dwFlags, bool fForce);

        #endregion
    }
}
