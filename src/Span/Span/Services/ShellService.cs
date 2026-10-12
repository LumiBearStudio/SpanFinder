using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;

namespace Span.Services
{
    /// <summary>
    /// Windows Shell 서비스 구현. ShellExecuteEx, Process.Start, WinRT Launcher를 사용하여
    /// 파일 열기, 속성 보기, 터미널 실행, 드라이브 꺼내기, 네트워크 드라이브 해제를 처리.
    /// </summary>
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

        /// <summary>
        /// 폴더 뷰(목록)에서 파일을 열 때 쓴다. Issue #72: 기본 앱이 사진 앱인 로컬 이미지는 탐색기처럼
        /// 폴더 보기 문맥과 함께 열어, 사진 앱에서 ←/→로 같은 폴더의 다른 사진으로 넘어가게 한다
        /// (다운로드한 사진도 확인 창 없이, SPAN의 정렬 순서대로). 그 밖의 파일이거나 실패하면 <see cref="OpenFile"/>과 같다.
        /// </summary>
        /// <param name="folder">파일이 보이는 폴더(표시 순서·위치를 넘기는 데 쓴다). 모르면 null — 이름순으로 이동.</param>
        public void OpenFileFromFolderView(string filePath, ViewModels.FolderViewModel? folder)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return;
            if (!Helpers.FolderContextLauncher.IsApplicable(filePath))
            {
                OpenFile(filePath);
                return;
            }

            FileOpening?.Invoke(System.IO.Path.GetFileName(filePath));

            string? sortBy = null;
            bool ascending = true;
            int index = 0;
            if (folder != null)
            {
                // 화면에 보이는 순서를 넘긴다. Details 보기는 정렬을 따로 가지고 Children 순서만 바꾸므로
                // 폴더의 정렬 상태 대신 실제 순서에서 판별한다(맞는 게 없으면 폴더의 정렬 상태).
                var children = folder.Children;
                var files = new System.Collections.Generic.List<Helpers.FolderContextLauncher.SortProbe>(children.Count);
                for (int i = 0; i < children.Count; i++)
                {
                    var child = children[i];
                    if (string.Equals(child.Path, filePath, StringComparison.OrdinalIgnoreCase)) index = i;
                    if (child is ViewModels.FileViewModel)
                        files.Add(new Helpers.FolderContextLauncher.SortProbe(child.Name, child.DateModifiedValue, child.SizeValue, child.FileType));
                }
                (sortBy, ascending) = Helpers.FolderContextLauncher.DetectDisplayedSort(files, folder.CurrentSortBy, folder.CurrentSortAscending);
            }
            _ = OpenWithFolderContextAsync(filePath, sortBy, ascending, index);
        }

        private static async Task OpenWithFolderContextAsync(string filePath, string? sortBy, bool ascending, int index)
        {
            bool opened = false;
            try { opened = await Helpers.FolderContextLauncher.TryOpenAsync(filePath, sortBy, ascending, index); }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[ShellService] OpenFileFromFolderView error: {ex.Message}"); }
            if (opened) return;

            // 폴백: 예전과 같은 열기 (사진 한 장만 열린다)
            try { Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true }); }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[ShellService] OpenFile fallback error: {ex.Message}"); }
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

        public void RunAsAdmin(string filePath)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath)
                {
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User cancelled UAC dialog — ignore
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] RunAsAdmin error: {ex.Message}");
            }
        }

        public void FormatDrive(string drivePath)
        {
            try
            {
                // SHFormatDrive는 Win32 API — ShellExecuteEx "format" verb 사용
                var info = new SHELLEXECUTEINFO
                {
                    cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
                    lpVerb = "format",
                    lpFile = drivePath,
                    nShow = 1, // SW_SHOWNORMAL
                    fMask = SEE_MASK_INVOKEIDLIST
                };
                ShellExecuteEx(ref info);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellService] FormatDrive error: {ex.Message}");
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
                // 파일 복사와 순번을 공유하는 쓰기 — 진행 중인 파일 복사가 나중에 이 텍스트를 덮어쓰지 않게
                Helpers.ClipboardInteropHelper.SetText(path);
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
