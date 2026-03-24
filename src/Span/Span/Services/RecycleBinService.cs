using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;
using Span.Services.FileOperations;

namespace Span.Services
{
    /// <summary>
    /// 휴지통 조작 서비스. Shell.Application COM을 래핑하여
    /// 항목 열거, 복원, 영구 삭제, 비우기를 제공한다.
    /// DI Singleton으로 등록. 모든 COM 작업은 Task.Run에서 실행.
    /// </summary>
    public class RecycleBinService
    {
        /// <summary>
        /// 휴지통 전체 항목을 열거한다.
        /// Shell.Application COM을 사용하며, Task.Run에서 실행하여 UI 스레드 블록 방지.
        /// </summary>
        /// <returns>RecycleBinItem 목록 (삭제일 내림차순 정렬)</returns>
        public Task<List<RecycleBinItem>> GetItemsAsync(CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var results = new List<RecycleBinItem>();

                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null) return results;

                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic? recycleBin = shell.NameSpace(10); // CSIDL_BITBUCKET
                    if (recycleBin == null) return results;

                    try
                    {
                        dynamic items = recycleBin.Items();
                        foreach (dynamic item in items)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var rbItem = new RecycleBinItem
                                {
                                    Name = item.Name?.ToString() ?? "",
                                    Path = item.Path?.ToString() ?? "",
                                    OriginalLocation = recycleBin.GetDetailsOf(item, 1)?.ToString() ?? "",
                                    ItemType = recycleBin.GetDetailsOf(item, 4)?.ToString() ?? "",
                                    IsFolder = item.IsFolder,
                                };

                                rbItem.OriginalPath = string.IsNullOrEmpty(rbItem.OriginalLocation)
                                    ? rbItem.Name
                                    : System.IO.Path.Combine(rbItem.OriginalLocation, rbItem.Name);

                                string? dateStr = CleanShellDateString(
                                    recycleBin.GetDetailsOf(item, 2)?.ToString());
                                if (!string.IsNullOrEmpty(dateStr) &&
                                    DateTime.TryParse(dateStr, System.Globalization.CultureInfo.CurrentCulture,
                                        System.Globalization.DateTimeStyles.NoCurrentDateDefault, out var deleted))
                                    rbItem.DateDeleted = deleted;

                                rbItem.Size = ParseSizeString(
                                    recycleBin.GetDetailsOf(item, 3)?.ToString());

                                string? modDateStr = CleanShellDateString(
                                    recycleBin.GetDetailsOf(item, 5)?.ToString());
                                if (!string.IsNullOrEmpty(modDateStr) &&
                                    DateTime.TryParse(modDateStr, System.Globalization.CultureInfo.CurrentCulture,
                                        System.Globalization.DateTimeStyles.NoCurrentDateDefault, out var modified))
                                    rbItem.DateModified = modified;

                                results.Add(rbItem);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch (Exception ex)
                            {
                                DebugLogger.Log(
                                    $"[RecycleBinService] Item parse error: {ex.Message}");
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

                results.Sort((a, b) => b.DateDeleted.CompareTo(a.DateDeleted));
                return results;
            }, ct);
        }

        /// <summary>
        /// SHQueryRecycleBin P/Invoke로 휴지통 크기/개수를 빠르게 조회.
        /// pszRootPath=null로 모든 드라이브 합산.
        /// </summary>
        public Task<RecycleBinInfo> GetInfoAsync()
        {
            return Task.Run(() =>
            {
                var rbInfo = new NativeMethods.SHQUERYRBINFO
                {
                    cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>()
                };

                int hr = NativeMethods.SHQueryRecycleBin(null, ref rbInfo);
                if (hr != 0)
                {
                    DebugLogger.Log($"[RecycleBinService] SHQueryRecycleBin failed: 0x{hr:X8}");
                    return new RecycleBinInfo(0, 0);
                }

                return new RecycleBinInfo(rbInfo.i64Size, rbInfo.i64NumItems);
            });
        }

        /// <summary>
        /// 선택된 항목들을 원래 위치로 복원.
        /// Shell.Application의 MoveHere를 사용 (기존 DeleteFileOperation.UndoAsync 패턴).
        /// </summary>
        public Task<OperationResult> RestoreAsync(
            List<RecycleBinItem> items, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var result = new OperationResult { Success = true };
                var errors = new List<string>();

                Type? shellType = Type.GetTypeFromProgID("Shell.Application");
                if (shellType == null)
                    return OperationResult.CreateFailure("Shell.Application unavailable");

                dynamic shell = Activator.CreateInstance(shellType)!;
                try
                {
                    dynamic? recycleBin = shell.NameSpace(10);
                    if (recycleBin == null)
                        return OperationResult.CreateFailure("Cannot access Recycle Bin");

                    try
                    {
                        dynamic binItems = recycleBin.Items();

                        foreach (var target in items)
                        {
                            ct.ThrowIfCancellationRequested();
                            bool found = false;

                            foreach (dynamic binItem in binItems)
                            {
                                try
                                {
                                    string? name = binItem.Name?.ToString();
                                    string? origDir = recycleBin.GetDetailsOf(binItem, 1)?.ToString();

                                    if (string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(origDir, target.OriginalLocation, StringComparison.OrdinalIgnoreCase))
                                    {
                                        dynamic? targetFolder = shell.NameSpace(target.OriginalLocation);
                                        if (targetFolder != null)
                                        {
                                            // 0x0014 = FOF_NOCONFIRMATION (0x10) | FOF_SILENT (0x04)
                                            targetFolder.MoveHere(binItem, 0x0014);
                                            result.AffectedPaths.Add(target.OriginalPath);
                                            found = true;
                                            Marshal.ReleaseComObject(targetFolder);
                                        }
                                        break;
                                    }
                                }
                                catch (OperationCanceledException) { throw; }
                                catch (Exception ex)
                                {
                                    DebugLogger.Log(
                                        $"[RecycleBinService] Restore match error: {ex.Message}");
                                }
                            }

                            if (!found)
                                errors.Add($"Restore failed: {target.Name}");
                        }
                    }
                    finally { Marshal.ReleaseComObject(recycleBin); }
                }
                finally { Marshal.ReleaseComObject(shell); }

                if (errors.Count > 0)
                {
                    result.ErrorMessage = string.Join("\n", errors);
                    if (result.AffectedPaths.Count == 0) result.Success = false;
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// 선택된 항목들을 휴지통에서 영구 삭제.
        /// 물리적 경로($R..., $I...)를 직접 삭제한다.
        /// </summary>
        public Task<OperationResult> DeletePermanentlyAsync(
            List<RecycleBinItem> items, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var result = new OperationResult { Success = true };
                var errors = new List<string>();

                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        string physicalPath = item.Path;

                        if (item.IsFolder && Directory.Exists(physicalPath))
                            Directory.Delete(physicalPath, recursive: true);
                        else if (File.Exists(physicalPath))
                            File.Delete(physicalPath);

                        // $RXXXXXX.ext -> $IXXXXXX.ext
                        string dir = System.IO.Path.GetDirectoryName(physicalPath) ?? "";
                        string fileName = System.IO.Path.GetFileName(physicalPath);
                        if (fileName.StartsWith("$R", StringComparison.OrdinalIgnoreCase))
                        {
                            string iFileName = "$I" + fileName[2..];
                            string iPath = System.IO.Path.Combine(dir, iFileName);
                            if (File.Exists(iPath))
                                File.Delete(iPath);
                        }

                        result.AffectedPaths.Add(item.OriginalPath);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        errors.Add($"{item.Name}: {ex.Message}");
                    }
                }

                if (errors.Count > 0)
                {
                    result.ErrorMessage = string.Join("\n", errors);
                    if (result.AffectedPaths.Count == 0) result.Success = false;
                }
                return result;
            }, ct);
        }

        /// <summary>
        /// SHEmptyRecycleBin P/Invoke로 휴지통 전체 비우기.
        /// SPAN 자체 확인 다이얼로그 사용 -> SHERB_NOCONFIRMATION 플래그.
        /// </summary>
        /// <param name="hwnd">부모 창 핸들 (진행 UI용, IntPtr.Zero 가능)</param>
        public Task<bool> EmptyAsync(IntPtr hwnd)
        {
            return Task.Run(() =>
            {
                uint flags = NativeMethods.SHERB_NOCONFIRMATION
                           | NativeMethods.SHERB_NOPROGRESSUI
                           | NativeMethods.SHERB_NOSOUND;
                uint hr = NativeMethods.SHEmptyRecycleBin(hwnd, null, flags);
                return hr == 0;
            });
        }

        /// <summary>
        /// Shell COM GetDetailsOf()가 반환하는 날짜 문자열에서
        /// Unicode directional markers(LRM \u200E, RLM \u200F 등)를 제거.
        /// 이 문자들이 포함되면 DateTime.TryParse가 실패한다.
        /// </summary>
        private static string? CleanShellDateString(string? dateStr)
        {
            if (string.IsNullOrWhiteSpace(dateStr))
                return null;

            // Remove Unicode directional markers and other invisible formatting chars
            // LRM (\u200E), RLM (\u200F), LRE (\u202A), RLE (\u202B),
            // PDF (\u202C), LRO (\u202D), RLO (\u202E), NBSP (\u00A0)
            var cleaned = new System.Text.StringBuilder(dateStr.Length);
            foreach (char c in dateStr)
            {
                if (c != '\u200E' && c != '\u200F' &&
                    c != '\u202A' && c != '\u202B' && c != '\u202C' &&
                    c != '\u202D' && c != '\u202E')
                {
                    cleaned.Append(c);
                }
            }
            return cleaned.ToString().Trim();
        }

        /// <summary>
        /// Shell COM GetDetailsOf(item, 3)의 크기 문자열을 long 바이트로 파싱.
        /// "1.5 KB", "300 bytes" 등의 형식을 처리한다.
        /// </summary>
        private static long ParseSizeString(string? sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr))
                return 0;

            // Remove non-breaking spaces and trim
            sizeStr = sizeStr.Replace("\u00A0", " ").Trim();

            // Try to extract numeric part and unit
            int i = 0;
            while (i < sizeStr.Length && (char.IsDigit(sizeStr[i]) || sizeStr[i] == '.' || sizeStr[i] == ','))
                i++;

            string numPart = sizeStr[..i].Replace(",", "").Trim();
            string unitPart = sizeStr[i..].Trim().ToUpperInvariant();

            if (!double.TryParse(numPart, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double value))
                return 0;

            return unitPart switch
            {
                "TB" => (long)(value * (1L << 40)),
                "GB" => (long)(value * (1L << 30)),
                "MB" => (long)(value * (1L << 20)),
                "KB" => (long)(value * (1L << 10)),
                _ => (long)value
            };
        }
    }
}
