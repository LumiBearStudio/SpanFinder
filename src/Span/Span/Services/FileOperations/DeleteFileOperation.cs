using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Span.Services.FileOperations;

/// <summary>
/// Represents a file or directory delete operation with Recycle Bin support.
/// Supports remote (FTP/SFTP) paths via FileSystemRouter.
/// Uses Win32 SHFileOperation for reliable Recycle Bin integration in MSIX apps.
/// Handles Windows reserved device names (nul, con, aux, etc.) and protected paths.
/// </summary>
public class DeleteFileOperation : IFileOperation
{
    // ── Win32 P/Invoke ──
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteFileW(string lpFileName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveDirectoryW(string lpPathName);

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOERRORUI = 0x0400;

    private const int ERROR_ACCESS_DENIED = 5;

    /// <summary>
    /// Windows reserved device names that cannot be deleted via normal APIs.
    /// </summary>
    private static readonly Regex ReservedNamePattern = new(
        @"^(CON|PRN|AUX|NUL|COM[0-9¹²³]|LPT[0-9¹²³])(\..+)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly List<string> _sourcePaths;
    private readonly bool _permanent;
    private readonly FileSystemRouter? _router;
    private readonly Dictionary<string, string> _recycledPaths = new();

    public DeleteFileOperation(List<string> sourcePaths, bool permanent = false)
        : this(sourcePaths, permanent, null)
    {
    }

    public DeleteFileOperation(List<string> sourcePaths, bool permanent, FileSystemRouter? router)
    {
        _sourcePaths = sourcePaths ?? throw new ArgumentNullException(nameof(sourcePaths));
        _permanent = permanent;
        _router = router;
    }

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? (_permanent
            ? $"Permanently delete \"{GetFileName(_sourcePaths[0])}\""
            : $"Delete \"{GetFileName(_sourcePaths[0])}\"")
        : (_permanent
            ? $"Permanently delete {_sourcePaths.Count} item(s)"
            : $"Delete {_sourcePaths.Count} item(s)");

    /// <inheritdoc/>
    public bool CanUndo => !_permanent && !_sourcePaths.Any(FileSystemRouter.IsRemotePath);

    /// <inheritdoc/>
    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new OperationResult { Success = true };
        var errors = new List<string>();

        try
        {
            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = _sourcePaths[i];
                var fileName = GetFileName(sourcePath);

                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = fileName,
                    CurrentFileIndex = i + 1,
                    TotalFileCount = _sourcePaths.Count,
                    Percentage = (i + 1) * 100 / _sourcePaths.Count
                });

                try
                {
                    if (FileSystemRouter.IsRemotePath(sourcePath))
                    {
                        // ── 원격 삭제 ──
                        var provider = _router?.GetConnectionForPath(sourcePath);
                        if (provider == null)
                        {
                            errors.Add($"원격 연결을 찾을 수 없습니다: {sourcePath}");
                            continue;
                        }

                        var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
                        await provider.DeleteAsync(remotePath, recursive: true, cancellationToken);
                    }
                    else if (_permanent)
                    {
                        // ── 로컬 영구 삭제 (Task.Run으로 UI 스레드 블록 방지) ──
                        var deleteError = await Task.Run(() => TryDeleteDirect(sourcePath), cancellationToken);
                        if (deleteError != null)
                        {
                            errors.Add($"{deleteError}: {fileName}");
                            continue;
                        }
                    }
                    else
                    {
                        // ── 로컬 휴지통 삭제 (Task.Run으로 UI 스레드 블록 방지) ──
                        var recycleError = await Task.Run(() =>
                        {
                            if (!FileExistsWin32(sourcePath) && !Directory.Exists(sourcePath))
                                return $"Path not found: {sourcePath}";

                            var err = TryRecycle(sourcePath);
                            if (err != null)
                                return $"{err}: {fileName}";

                            return (string?)null;
                        }, cancellationToken);

                        if (recycleError != null)
                        {
                            if (recycleError.StartsWith("Path not found:"))
                                errors.Add(recycleError);
                            else
                                errors.Add(recycleError);
                            continue;
                        }
                        _recycledPaths[sourcePath] = sourcePath;
                    }

                    result.AffectedPaths.Add(sourcePath);
                }
                catch (PathTooLongException)
                {
                    errors.Add($"경로가 너무 깁니다: {fileName}");
                }
                catch (Exception ex)
                {
                    errors.Add($"Failed to delete {fileName}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                if (result.AffectedPaths.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = string.Join("\n", errors);
                }
                else
                {
                    result.Success = true;
                    result.ErrorMessage = $"Some items could not be deleted:\n{string.Join("\n", errors)}";
                }
            }
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = "Delete operation was cancelled";
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Unexpected error: {ex.Message}";
        }

        return result;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Shell.Application COM 객체를 통해 휴지통(NameSpace 10)에서 삭제된 항목을 찾아
    /// 원래 위치로 복원한다. GetDetailsOf(item, 1)로 "Original Location"을 매칭하고,
    /// Folder.MoveHere()로 이동한다.
    /// </remarks>
    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        if (_permanent)
        {
            return OperationResult.CreateFailure("Cannot undo permanent deletion");
        }

        if (_recycledPaths.Count == 0)
        {
            return OperationResult.CreateFailure("No items to restore (undo information not available)");
        }

        return await Task.Run(() =>
        {
            var result = new OperationResult { Success = true };
            var errors = new List<string>();
            var restored = new List<string>();

            try
            {
                // Shell.Application COM — Recycle Bin 접근
                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return OperationResult.CreateFailure("Shell.Application COM not available");

                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    // NameSpace(10) = CSIDL_BITBUCKET (Recycle Bin)
                    dynamic? recycleBin = shell.NameSpace(10);
                    if (recycleBin == null)
                        return OperationResult.CreateFailure("Cannot access Recycle Bin");

                    try
                    {
                        dynamic items = recycleBin.Items();

                        foreach (var originalPath in _recycledPaths.Keys)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string originalDir = Path.GetDirectoryName(originalPath) ?? "";
                            string originalName = Path.GetFileName(originalPath);
                            bool found = false;

                            foreach (dynamic item in items)
                            {
                                try
                                {
                                    // Column 1 = "Original Location" (휴지통 항목의 원래 디렉토리)
                                    string? itemOriginalDir = recycleBin.GetDetailsOf(item, 1)?.ToString();
                                    string? itemName = item.Name?.ToString();

                                    if (itemName != null && itemOriginalDir != null &&
                                        string.Equals(itemName, originalName, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(itemOriginalDir, originalDir, StringComparison.OrdinalIgnoreCase))
                                    {
                                        // 원래 디렉토리로 복원
                                        dynamic? targetFolder = shell.NameSpace(originalDir);
                                        if (targetFolder != null)
                                        {
                                            // 0x0014 = FOF_NOCONFIRMATION (0x10) | FOF_SILENT (0x04)
                                            targetFolder.MoveHere(item, 0x0014);
                                            restored.Add(originalPath);
                                            found = true;
                                            Marshal.ReleaseComObject(targetFolder);
                                        }
                                        break;
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[DeleteUndo] Error checking Recycle Bin item: {ex.Message}");
                                }
                            }

                            if (!found)
                            {
                                // 이미 복원되었는지 확인 (원래 경로에 존재)
                                if (File.Exists(originalPath) || Directory.Exists(originalPath))
                                {
                                    restored.Add(originalPath);
                                }
                                else
                                {
                                    errors.Add($"Recycle Bin에서 찾을 수 없음: {Path.GetFileName(originalPath)}");
                                }
                            }
                        }
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(recycleBin);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(shell);
                }
            }
            catch (OperationCanceledException)
            {
                return OperationResult.CreateFailure("Restore operation was cancelled");
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure($"Failed to restore from Recycle Bin: {ex.Message}");
            }

            result.AffectedPaths = restored;
            if (errors.Count > 0)
            {
                if (restored.Count == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = string.Join("\n", errors);
                }
                else
                {
                    result.ErrorMessage = $"Some items could not be restored:\n{string.Join("\n", errors)}";
                }
            }

            return result;
        }, cancellationToken);
    }

    // ────────────────────────────────────────────────────────────
    //  Recycle (Delete 키) — 모든 경로에서 휴지통 유지
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends a file/directory to the Recycle Bin. Uses SHFileOperation as primary,
    /// then elevated SHFileOperation for protected paths. Reserved device names
    /// cannot go to the Recycle Bin, so they are permanently deleted with warning.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    private static string? TryRecycle(string sourcePath)
    {
        // Step 1: SHFileOperation with FOF_ALLOWUNDO (standard recycle bin)
        int shResult = RunSHFileDelete(sourcePath, allowUndo: true);
        if (shResult == 0) return null;

        // Step 2: For reserved device names, SHFileOperation always fails (0x7C).
        // These can't go to the recycle bin — permanently delete with \\?\ prefix.
        if (IsReservedDeviceName(sourcePath))
        {
            return TryDeleteDirect(sourcePath);
        }

        // Step 3: ACCESS_DENIED (0x78) on protected paths → elevated SHFileOperation (recycle bin preserved)
        return TryRecycleElevated(sourcePath);
    }

    /// <summary>
    /// Runs SHFileOperation FO_DELETE with the given flags.
    /// Returns 0 on success, or the SHFileOperation error code.
    /// </summary>
    private static int RunSHFileDelete(string sourcePath, bool allowUndo)
    {
        ushort flags = FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI;
        if (allowUndo) flags |= FOF_ALLOWUNDO;

        var fileOp = new SHFILEOPSTRUCT
        {
            hwnd = IntPtr.Zero,
            wFunc = FO_DELETE,
            pFrom = sourcePath + "\0\0",
            pTo = null,
            fFlags = flags,
            fAnyOperationsAborted = false,
            hNameMappings = IntPtr.Zero,
            lpszProgressTitle = null
        };

        int ret = SHFileOperation(ref fileOp);
        if (ret == 0 && fileOp.fAnyOperationsAborted)
            return -1; // user cancelled
        return ret;
    }

    /// <summary>
    /// Runs SHFileOperation via an elevated (Administrator) process to send
    /// protected files to the Recycle Bin. This preserves recycle bin behavior
    /// even for paths like C:\ that require admin privileges.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    private static string? TryRecycleElevated(string sourcePath)
    {
        try
        {
            // PowerShell elevated with SHFileOperation P/Invoke — keeps FOF_ALLOWUNDO
            string escaped = sourcePath.Replace("'", "''");
            string script = $@"
Add-Type -TypeDefinition '
using System;using System.Runtime.InteropServices;
public class ShellOp {{
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]
    public struct SHFILEOPSTRUCT {{
        public IntPtr hwnd;public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]public string pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]public bool fAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]public string lpszProgressTitle;
    }}
    [DllImport(""shell32.dll"",CharSet=CharSet.Unicode)]
    public static extern int SHFileOperation(ref SHFILEOPSTRUCT op);
    public static int Recycle(string path) {{
        var op = new SHFILEOPSTRUCT();
        op.wFunc = 3;
        op.pFrom = path + ""\0\0"";
        op.fFlags = 0x0054;
        return SHFileOperation(ref op);
    }}
}}';
$r = [ShellOp]::Recycle('{escaped}');
exit $r
".Replace("\r\n", " ").Replace("\n", " ");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "관리자 권한 프로세스를 시작할 수 없습니다";
            proc.WaitForExit(15_000);

            if (!FileExistsWin32(sourcePath) && !Directory.Exists(sourcePath))
                return null;

            return $"관리자 권한으로도 삭제 실패 (exit=0x{proc.ExitCode:X})";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "관리자 권한이 필요합니다 (UAC 취소됨)";
        }
        catch (Exception ex)
        {
            return $"관리자 권한 삭제 오류: {ex.Message}";
        }
    }

    // ────────────────────────────────────────────────────────────
    //  Permanent Delete (Shift+Delete) — 영구 삭제
    // ────────────────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes a file/directory using Win32 API with \\?\ prefix.
    /// Falls back to elevated process if ACCESS_DENIED.
    /// Returns null on success, or an error message on failure.
    /// </summary>
    private static string? TryDeleteDirect(string sourcePath)
    {
        bool isFile = File.Exists(sourcePath);
        bool isDir = !isFile && Directory.Exists(sourcePath);

        if (!isFile && !isDir && IsReservedDeviceName(sourcePath))
        {
            isFile = FileExistsWin32(sourcePath);
        }

        if (!isFile && !isDir) return "Path not found";

        string extPath = EnsureExtendedLengthPrefix(sourcePath);

        bool deleted;
        if (isFile)
        {
            deleted = DeleteFileW(extPath);
        }
        else
        {
            try { Directory.Delete(sourcePath, recursive: true); return null; }
            catch { /* fall through to Win32 */ }
            deleted = RemoveDirectoryW(extPath);
        }

        if (deleted) return null;

        int err = Marshal.GetLastWin32Error();
        if (err != ERROR_ACCESS_DENIED) return $"삭제 실패 (Win32 error {err})";

        return TryDeleteElevated(sourcePath, isDir);
    }

    /// <summary>
    /// Permanently deletes via an elevated (Administrator) process with UAC prompt.
    /// Used only for Shift+Delete and reserved device names that can't go to recycle bin.
    /// </summary>
    private static string? TryDeleteElevated(string sourcePath, bool isDirectory)
    {
        try
        {
            string script;
            if (IsReservedDeviceName(sourcePath))
            {
                string extPath = EnsureExtendedLengthPrefix(sourcePath).Replace("'", "''");
                script = $@"Add-Type -TypeDefinition 'using System;using System.Runtime.InteropServices;public class D{{[DllImport(""kernel32.dll"",CharSet=CharSet.Unicode,SetLastError=true)][return:MarshalAs(UnmanagedType.Bool)]public static extern bool DeleteFileW(string p);}}';$r=[D]::DeleteFileW('{extPath}');if(-not $r){{exit 1}}";
            }
            else
            {
                string escaped = sourcePath.Replace("'", "''");
                script = isDirectory
                    ? $"Remove-Item -LiteralPath '{escaped}' -Recurse -Force -ErrorAction Stop"
                    : $"Remove-Item -LiteralPath '{escaped}' -Force -ErrorAction Stop";
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                Verb = "runas",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
            };

            using var proc = Process.Start(psi);
            if (proc == null) return "관리자 권한 프로세스를 시작할 수 없습니다";
            proc.WaitForExit(15_000);

            if (!FileExistsWin32(sourcePath) && !Directory.Exists(sourcePath))
                return null;

            return $"관리자 권한으로도 삭제 실패 (exit={proc.ExitCode})";
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return "관리자 권한이 필요합니다 (UAC 취소됨)";
        }
        catch (Exception ex)
        {
            return $"관리자 권한 삭제 오류: {ex.Message}";
        }
    }

    /// <summary>
    /// Checks if the file name component is a Windows reserved device name.
    /// </summary>
    private static bool IsReservedDeviceName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return !string.IsNullOrEmpty(name) && ReservedNamePattern.IsMatch(name);
    }

    /// <summary>
    /// Adds the \\?\ extended-length path prefix to bypass Win32 name validation.
    /// </summary>
    private static string EnsureExtendedLengthPrefix(string path)
    {
        if (path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\"))
            return path;
        if (path.StartsWith(@"\\"))
            return @"\\?\UNC\" + path[2..]; // UNC path
        return @"\\?\" + path;
    }

    /// <summary>
    /// Uses Win32 FindFirstFile to check file existence (works for reserved device names).
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr hFindFile);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WIN32_FIND_DATA
    {
        public uint dwFileAttributes;
        public long ftCreationTime, ftLastAccessTime, ftLastWriteTime;
        public uint nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    private static bool FileExistsWin32(string path)
    {
        string extPath = EnsureExtendedLengthPrefix(path);
        var h = FindFirstFileW(extPath, out _);
        if (h == new IntPtr(-1)) return false;
        FindClose(h);
        return true;
    }

    private static string GetFileName(string path)
    {
        if (FileSystemRouter.IsRemotePath(path))
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri))
            {
                var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
                return segments.Length > 0 ? Uri.UnescapeDataString(segments[^1]) : path;
            }
            return path.TrimEnd('/').Split('/')[^1];
        }
        return Path.GetFileName(path);
    }
}
