using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using static Span.Services.LocalizationService;

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

    /// <summary>Issue #61: FileOperationManager의 진행률 팝업 표시 휴리스틱용.</summary>
    public IReadOnlyList<string> SourcePaths => _sourcePaths;

    /// <inheritdoc/>
    public string Description => _sourcePaths.Count == 1
        ? (_permanent
            ? string.Format(L("Op_PermanentDeleteSingle"), FileOperationHelpers.GetFileName(_sourcePaths[0]))
            : string.Format(L("Op_DeleteSingle"), FileOperationHelpers.GetFileName(_sourcePaths[0])))
        : (_permanent
            ? string.Format(L("Op_PermanentDeleteMultiple"), _sourcePaths.Count)
            : string.Format(L("Op_DeleteMultiple"), _sourcePaths.Count));

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
            // Issue #61: 로컬 경로는 IFileOperation(탐색기와 동일 API)으로 일괄 처리 —
            // 폴더 내부에서도 항목별 콜백이 오므로 실시간 진행률 + 즉시 취소가 가능하고,
            // 휴지통에는 폴더가 통째로 들어가 기존 Undo(복원) 로직이 그대로 유지된다.
            var localPaths = new List<string>();
            foreach (var p in _sourcePaths)
            {
                if (!FileSystemRouter.IsRemotePath(p)) localPaths.Add(p);
            }

            if (localPaths.Count > 0)
            {
                var shellResult = await Task.Run(() => ShellDeleteWithProgress.Execute(
                    localPaths,
                    _permanent,
                    (pct, name) => progress?.Report(new FileOperationProgress
                    {
                        CurrentFile = name,
                        CurrentFileIndex = 1,
                        TotalFileCount = localPaths.Count,
                        Percentage = pct
                    }),
                    cancellationToken), cancellationToken);

                foreach (var deletedPath in localPaths)
                {
                    // 애초에 없던 경로는 삭제 성공이 아니라 오류로 보고한다 (아래 MissingPaths)
                    if (shellResult.MissingPaths.Contains(deletedPath)) continue;

                    // 삭제 성공 여부는 실제 존재 여부로 판정 (취소 시 일부만 삭제될 수 있음)
                    if (!FileExistsWin32(deletedPath) && !Directory.Exists(deletedPath))
                    {
                        result.AffectedPaths.Add(deletedPath);
                        if (!_permanent) _recycledPaths[deletedPath] = deletedPath;
                    }
                }

                if (shellResult.Cancelled)
                {
                    result.Success = false;
                    result.ErrorMessage = L("Op_Cancelled_Delete");
                    return result;
                }
                if (shellResult.Error != null)
                {
                    errors.Add(shellResult.Error);
                }
                // 존재하지 않던 경로는 기존 동작대로 오류로 보고
                foreach (var missingPath in shellResult.MissingPaths)
                {
                    errors.Add(string.Format(L("Op_PathNotFound"),
                        FileOperationHelpers.GetFileName(missingPath)));
                }
            }

            // 원격(FTP/SFTP) 경로는 기존 항목별 경로로 처리
            for (int i = 0; i < _sourcePaths.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var sourcePath = _sourcePaths[i];
                if (!FileSystemRouter.IsRemotePath(sourcePath)) continue;
                var fileName = FileOperationHelpers.GetFileName(sourcePath);

                // Issue #61: 항목 "시작" 시점 기준으로 보고 (기존 (i+1)*100은 작업 전에
                // 이미 완료율로 표시되어 단일 항목이 시작 직후 100%로 보였음)
                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = fileName,
                    CurrentFileIndex = i + 1,
                    TotalFileCount = _sourcePaths.Count,
                    Percentage = i * 100 / _sourcePaths.Count
                });

                try
                {
                    // ── 원격 삭제 (로컬은 위 IFileOperation 경로에서 이미 처리됨) ──
                    var provider = _router?.GetConnectionForPath(sourcePath);
                    if (provider == null)
                    {
                        errors.Add(string.Format(L("Op_NoRemoteRouter"), sourcePath));
                        continue;
                    }

                    var remotePath = FileSystemRouter.ExtractRemotePath(sourcePath);
                    await provider.DeleteAsync(remotePath, recursive: true, cancellationToken);

                    result.AffectedPaths.Add(sourcePath);
                }
                catch (PathTooLongException)
                {
                    errors.Add(string.Format(L("Op_PathTooLong"), fileName));
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format(L("Op_FailedTo_Delete"), fileName, ex.Message));
                }
            }

            // Issue #61: 전 항목 처리 완료 → 100% 보고 (시작 시점 기준 보고의 마무리)
            progress?.Report(new FileOperationProgress
            {
                CurrentFileIndex = _sourcePaths.Count,
                TotalFileCount = _sourcePaths.Count,
                Percentage = 100
            });

            FileOperationHelpers.FinalizeResultWithErrors(result, errors, "Op_SomeNotDeleted");
        }
        catch (OperationCanceledException)
        {
            result.Success = false;
            result.ErrorMessage = L("Op_Cancelled_Delete");
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = string.Format(L("Op_UnexpectedError"), ex.Message);
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
            return OperationResult.CreateFailure(L("Op_CannotUndoPermanent"));
        }

        if (_recycledPaths.Count == 0)
        {
            return OperationResult.CreateFailure(L("Op_NoItemsToRestore"));
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
                    return OperationResult.CreateFailure(L("Error_ShellNotAvailable"));

                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    // NameSpace(10) = CSIDL_BITBUCKET (Recycle Bin)
                    dynamic? recycleBin = shell.NameSpace(10);
                    if (recycleBin == null)
                        return OperationResult.CreateFailure(L("Error_CannotAccessRecycleBin"));

                    try
                    {
                        dynamic items = recycleBin.Items();

                        // Issue #61 후속: 휴지통을 1회만 스캔해 (원래위치|이름) → 항목 인덱스를 만든다.
                        // 기존에는 복원 대상마다 휴지통 전체를 순회하며 GetDetailsOf(COM)를 호출해
                        // O(N×M)이었고, 휴지통에 항목이 많으면 복원이 눈에 띄게 느렸다.
                        var recycleIndex = new Dictionary<string, dynamic>(StringComparer.OrdinalIgnoreCase);
                        foreach (dynamic item in items)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                // Column 1 = "Original Location" (휴지통 항목의 원래 디렉토리)
                                string? itemOriginalDir = recycleBin.GetDetailsOf(item, 1)?.ToString();
                                string? itemName = item.Name?.ToString();
                                if (itemOriginalDir != null && itemName != null)
                                {
                                    // 같은 경로가 여러 번 삭제된 경우 나중 항목(더 최근)이 우선
                                    recycleIndex[itemOriginalDir + "|" + itemName] = item;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[DeleteUndo] Error indexing Recycle Bin item: {ex.Message}");
                            }
                        }

                        foreach (var originalPath in _recycledPaths.Keys)
                        {
                            cancellationToken.ThrowIfCancellationRequested();

                            string originalDir = Path.GetDirectoryName(originalPath) ?? "";
                            string originalName = Path.GetFileName(originalPath);
                            bool found = false;

                            if (recycleIndex.TryGetValue(originalDir + "|" + originalName, out dynamic? match))
                            {
                                try
                                {
                                    // 원래 디렉토리로 복원
                                    dynamic? targetFolder = shell.NameSpace(originalDir);
                                    if (targetFolder != null)
                                    {
                                        // 0x0014 = FOF_NOCONFIRMATION (0x10) | FOF_SILENT (0x04)
                                        targetFolder.MoveHere(match, 0x0014);
                                        restored.Add(originalPath);
                                        found = true;
                                        Marshal.ReleaseComObject(targetFolder);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"[DeleteUndo] Error restoring item: {ex.Message}");
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
                                    errors.Add(string.Format(L("Error_NotFoundInRecycleBin"), Path.GetFileName(originalPath)));
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
                return OperationResult.CreateFailure(L("Op_Cancelled_Restore"));
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(string.Format(L("Op_FailedRestoreRecycleBin"), ex.Message));
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
                    result.ErrorMessage = $"{L("Op_SomeNotRestored")}:\n{string.Join("\n", errors)}";
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
            if (proc == null) return L("Error_CannotStartAdmin");
            proc.WaitForExit(15_000);

            if (!FileExistsWin32(sourcePath) && !Directory.Exists(sourcePath))
                return null;

            return string.Format(L("Error_AdminDeleteFailed"), $"exit=0x{proc.ExitCode:X}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return L("Error_AdminRequired");
        }
        catch (Exception ex)
        {
            return string.Format(L("Error_AdminDeleteError"), ex.Message);
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

        if (!isFile && !isDir) return L("Error_PathNotExist");

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
        if (err != ERROR_ACCESS_DENIED) return string.Format(L("Error_DeleteFailed"), err);

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
            if (proc == null) return L("Error_CannotStartAdmin");
            proc.WaitForExit(15_000);

            if (!FileExistsWin32(sourcePath) && !Directory.Exists(sourcePath))
                return null;

            return string.Format(L("Error_AdminDeleteFailed"), $"exit={proc.ExitCode}");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return L("Error_AdminRequired");
        }
        catch (Exception ex)
        {
            return string.Format(L("Error_AdminDeleteError"), ex.Message);
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

}
