using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 로컬 파일 시스템 서비스 구현. 드라이브 목록을 병렬/타임아웃으로 안전하게 로드하고,
    /// 지정 경로의 디렉토리/파일 목록을 반환한다. 숨김 파일 표시 설정을 반영.
    /// </summary>
    public class FileSystemService : IFileSystemService
    {
        private const int DriveLoadTimeoutMs = 500; // 500ms timeout per drive
        private readonly SettingsService _settings;

        public FileSystemService(SettingsService settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Get all available drives with parallel loading and timeout protection
        /// </summary>
        public async Task<List<DriveItem>> GetDrivesAsync()
        {
            // DriveInfo.GetDrives() can block for seconds with stale network drives,
            // so run the entire enumeration off the UI thread
            var allDrives = await Task.Run(() =>
                DriveInfo.GetDrives()
                    .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable || d.DriveType == DriveType.Network || d.DriveType == DriveType.CDRom)
                    .ToList());

            // Load each drive in parallel with timeout
            var tasks = allDrives.Select(drive => LoadDriveWithTimeoutAsync(drive));
            var results = await Task.WhenAll(tasks);

            // Filter out failed drives (null results)
            return results.Where(d => d != null).ToList()!;
        }

        /// <summary>
        /// Load a single drive with timeout protection.
        /// Uses Task.WhenAny to enforce real timeout — CancellationToken alone
        /// cannot cancel a blocking DriveInfo property access (IsReady, VolumeLabel, etc.).
        /// </summary>
        private async Task<DriveItem?> LoadDriveWithTimeoutAsync(DriveInfo drive)
        {
            try
            {
                var driveTask = Task.Run(() => CreateDriveItem(drive));
                var timeoutTask = Task.Delay(DriveLoadTimeoutMs);

                var completed = await Task.WhenAny(driveTask, timeoutTask);

                if (completed == driveTask)
                {
                    return await driveTask; // propagate result or exception
                }

                // Timeout — drive took too long (stale network share, etc.)
                System.Diagnostics.Debug.WriteLine($"[FileSystemService] Drive {drive.Name} timed out after {DriveLoadTimeoutMs}ms");
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystemService] Error loading drive {drive.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Create DriveItem from DriveInfo (blocking operation)
        /// </summary>
        private DriveItem? CreateDriveItem(DriveInfo drive)
        {
            try
            {
                // Network drives may be disconnected but should still appear in sidebar.
                // Only skip non-network drives that aren't ready.
                bool isNetwork = drive.DriveType == DriveType.Network;
                if (!drive.IsReady && !isNetwork)
                {
                    return null;
                }

                var driveItem = new DriveItem
                {
                    Path = drive.Name,
                    DriveType = drive.DriveType.ToString()
                };

                // Try to get volume/size info (may fail for disconnected network drives)
                if (drive.IsReady)
                {
                    try { driveItem.Label = drive.VolumeLabel ?? string.Empty; } catch { }
                    try { driveItem.DriveFormat = drive.DriveFormat; } catch { }
                    try
                    {
                        driveItem.TotalSize = drive.TotalSize;
                        driveItem.AvailableFreeSpace = drive.AvailableFreeSpace;
                    }
                    catch
                    {
                        driveItem.TotalSize = 0;
                        driveItem.AvailableFreeSpace = 0;
                    }
                }
                else
                {
                    // Disconnected network drive — show with minimal info
                    driveItem.Label = string.Empty;
                    driveItem.TotalSize = 0;
                    driveItem.AvailableFreeSpace = 0;
                }

                // Set icon based on drive type (uses current icon pack)
                driveItem.IconGlyph = IconService.Current?.GetDriveGlyph(driveItem.DriveType) ?? "\uEDFA";

                // Generate display name based on drive type
                var driveLetter = driveItem.Path.TrimEnd('\\');

                if (isNetwork)
                {
                    // 네트워크 드라이브: UNC 경로에서 공유 이름 추출 (Windows 탐색기와 동일)
                    // 예: \\server\share → "share (\\server\share) (Y:)"
                    var uncPath = GetUncPath(driveLetter);
                    if (!string.IsNullOrEmpty(uncPath))
                    {
                        var shareName = uncPath.Split('\\', StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? uncPath;
                        driveItem.Name = $"{shareName} ({uncPath}) ({driveLetter})";
                    }
                    else
                    {
                        var fallback = string.IsNullOrEmpty(driveItem.Label)
                            ? LocalizationService.L("Drive_Network") : driveItem.Label;
                        driveItem.Name = $"{fallback} ({driveLetter})";
                    }
                }
                else
                {
                    var defaultLabel = drive.DriveType switch
                    {
                        DriveType.Fixed => LocalizationService.L("Drive_LocalDisk"),
                        DriveType.Removable => LocalizationService.L("Drive_USB"),
                        DriveType.CDRom => LocalizationService.L("Drive_CDDVD"),
                        _ => LocalizationService.L("Drive_Default")
                    };
                    driveItem.Name = string.IsNullOrEmpty(driveItem.Label)
                        ? $"{defaultLabel} ({driveLetter})"
                        : $"{driveItem.Label} ({driveLetter})";
                }

                return driveItem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystemService] Error creating DriveItem for {drive.Name}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 드라이브 문자에서 UNC 경로를 반환 (WNetGetConnectionW).
        /// 예: "Y:" → "\\server\share"
        /// </summary>
        private static string? GetUncPath(string driveLetter)
        {
            try
            {
                var sb = new System.Text.StringBuilder(260);
                int len = sb.Capacity;
                int result = Helpers.NativeMethods.WNetGetConnectionW(driveLetter, sb, ref len);
                return result == 0 ? sb.ToString() : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Windows 네트워크 위치(Network Shortcuts)를 열거.
        /// %APPDATA%\Microsoft\Windows\Network Shortcuts 폴더의 바로가기를 DriveItem으로 반환.
        /// </summary>
        public Task<List<DriveItem>> GetNetworkShortcutsAsync()
        {
            return Task.Run(() =>
            {
                var items = new List<DriveItem>();
                try
                {
                    var shortcutsDir = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "Microsoft", "Windows", "Network Shortcuts");

                    if (!Directory.Exists(shortcutsDir)) return items;

                    foreach (var dir in Directory.GetDirectories(shortcutsDir))
                    {
                        try
                        {
                            var name = Path.GetFileName(dir);
                            if (string.IsNullOrEmpty(name)) continue;

                            // 각 하위 폴더 안에 target.lnk 또는 desktop.ini로 실제 경로를 참조
                            var targetPath = ResolveNetworkShortcutTarget(dir);

                            // target을 찾지 못해도 폴더명으로 표시 (Windows 탐색기와 동일)
                            var displayName = !string.IsNullOrEmpty(targetPath)
                                ? $"{name} ({targetPath})" : name;
                            var path = !string.IsNullOrEmpty(targetPath) ? targetPath : dir;

                            items.Add(new DriveItem
                            {
                                Name = displayName,
                                Path = path,
                                Label = name,
                                DriveType = "Network",
                                IconGlyph = IconService.Current?.GetDriveGlyph("Network") ?? "\uEDFA",
                                NetworkShortcutPath = dir,
                                TotalSize = 0,
                                AvailableFreeSpace = 0
                            });
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[FileSystemService] Error enumerating network shortcuts: {ex.Message}");
                }
                return items;
            });
        }

        /// <summary>
        /// 네트워크 바로가기 폴더에서 실제 UNC 경로를 추출.
        /// target.lnk → Shell Link 또는 desktop.ini → CLSID 경로.
        /// </summary>
        internal static string? ResolveNetworkShortcutTarget(string shortcutDir)
        {
            // 방법 1: target.lnk 파일에서 UNC 경로 추출
            var lnkFile = Path.Combine(shortcutDir, "target.lnk");
            if (File.Exists(lnkFile))
            {
                var target = ResolveShellLink(lnkFile);
                if (!string.IsNullOrEmpty(target)) return target;
            }

            // 방법 2: desktop.ini에서 경로 정보 추출 (일부 네트워크 위치)
            var iniFile = Path.Combine(shortcutDir, "desktop.ini");
            if (File.Exists(iniFile))
            {
                try
                {
                    var lines = File.ReadAllLines(iniFile);
                    foreach (var line in lines)
                    {
                        // URL=\\server\share 형식
                        if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase))
                            return line.Substring(4).Trim();
                    }
                }
                catch { }
            }

            // 방법 3: 폴더명 자체가 UNC 경로인 경우
            var dirName = Path.GetFileName(shortcutDir);
            if (dirName != null && dirName.StartsWith(@"\\")) return dirName;

            return null;
        }

        /// <summary>
        /// .lnk 파일에서 대상 경로를 추출 (IShellLink COM 대신 바이너리 파싱).
        /// </summary>
        internal static string? ResolveShellLink(string lnkPath)
        {
            try
            {
                var bytes = File.ReadAllBytes(lnkPath);
                if (bytes.Length < 76) return null;

                // Shell Link Binary Format: HeaderSize(4) + LinkCLSID(16) + LinkFlags(4)
                int flags = BitConverter.ToInt32(bytes, 0x14);
                int offset = 0x4C; // after header

                // HasLinkTargetIDList flag (bit 0)
                if ((flags & 0x01) != 0)
                {
                    if (offset + 2 > bytes.Length) return null;
                    int idListSize = BitConverter.ToUInt16(bytes, offset);
                    offset += 2 + idListSize;
                }

                // HasLinkInfo flag (bit 1)
                if ((flags & 0x02) != 0)
                {
                    if (offset + 4 > bytes.Length) return null;
                    int linkInfoSize = BitConverter.ToInt32(bytes, offset);
                    int linkInfoHeaderSize = BitConverter.ToInt32(bytes, offset + 4);

                    // CommonNetworkRelativeLinkOffset (offset 0x14 in LinkInfo)
                    if (linkInfoHeaderSize >= 0x1C)
                    {
                        int cnrlOffset = BitConverter.ToInt32(bytes, offset + 0x14);
                        if (cnrlOffset > 0 && offset + cnrlOffset + 0x14 < bytes.Length)
                        {
                            int netNameOffset = BitConverter.ToInt32(bytes, offset + cnrlOffset + 0x08);
                            if (netNameOffset > 0)
                            {
                                int netNamePos = offset + cnrlOffset + netNameOffset;
                                int end = Array.IndexOf(bytes, (byte)0, netNamePos);
                                if (end > netNamePos)
                                {
                                    var netName = System.Text.Encoding.Default.GetString(bytes, netNamePos, end - netNamePos);
                                    if (!string.IsNullOrEmpty(netName)) return netName;
                                }
                            }
                        }
                    }

                    // LocalBasePath (offset 0x10 in LinkInfo)
                    int localBasePathOffset = BitConverter.ToInt32(bytes, offset + 0x10);
                    if (localBasePathOffset > 0 && offset + localBasePathOffset < bytes.Length)
                    {
                        int lbpPos = offset + localBasePathOffset;
                        int lbpEnd = Array.IndexOf(bytes, (byte)0, lbpPos);
                        if (lbpEnd > lbpPos)
                        {
                            var localPath = System.Text.Encoding.Default.GetString(bytes, lbpPos, lbpEnd - lbpPos);
                            if (!string.IsNullOrEmpty(localPath)) return localPath;
                        }
                    }
                }

                // Fallback: lnk 바이너리에서 ftp://, http://, \\\\ 등 URL/UNC 문자열 직접 검색
                var urlFromBytes = ExtractUrlFromLnkBytes(bytes);
                if (!string.IsNullOrEmpty(urlFromBytes)) return urlFromBytes;

                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// lnk 바이너리에서 ftp://, http://, https://, \\\\ 패턴의 URL/UNC 문자열 추출.
        /// IDList 내부에 Unicode로 저장된 URL을 찾는다.
        /// </summary>
        private static string? ExtractUrlFromLnkBytes(byte[] bytes)
        {
            // Unicode (UTF-16LE) 패턴 검색: "ftp://", "http://", "https://", "\\\\"
            string[] prefixes = { "ftp://", "http://", "https://" };
            foreach (var prefix in prefixes)
            {
                var prefixBytes = System.Text.Encoding.Unicode.GetBytes(prefix);
                int idx = FindBytes(bytes, prefixBytes);
                if (idx >= 0)
                {
                    // null-terminated Unicode 문자열 추출
                    int end = idx;
                    while (end + 1 < bytes.Length)
                    {
                        if (bytes[end] == 0 && bytes[end + 1] == 0) break;
                        end += 2;
                    }
                    if (end > idx)
                    {
                        var url = System.Text.Encoding.Unicode.GetString(bytes, idx, end - idx).TrimEnd('/');
                        if (!string.IsNullOrEmpty(url)) return url;
                    }
                }
            }
            return null;
        }

        private static int FindBytes(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i <= haystack.Length - needle.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j]) { match = false; break; }
                }
                if (match) return i;
            }
            return -1;
        }

        public Task<List<IFileSystemItem>> GetItemsAsync(string path)
        {
            return Task.Run(() =>
            {
                var items = new List<IFileSystemItem>();

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    return items;
                }

                try
                {
                    var dirInfo = new DirectoryInfo(path);

                    // Enumerate (lazy) — 대용량 폴더에서 메모리 효율적
                    foreach (var d in dirInfo.EnumerateDirectories())
                    {
                        bool isHidden = (d.Attributes & FileAttributes.Hidden) != 0;
                        if (!_settings.ShowHiddenFiles && isHidden) continue;

                        items.Add(new FolderItem
                        {
                            Name = d.Name,
                            Path = d.FullName,
                            DateModified = d.LastWriteTime,
                            IsHidden = isHidden
                        });
                    }

                    foreach (var f in dirInfo.EnumerateFiles())
                    {
                        bool isHidden = (f.Attributes & FileAttributes.Hidden) != 0;
                        if (!_settings.ShowHiddenFiles && isHidden) continue;

                        items.Add(new FileItem
                        {
                            Name = f.Name,
                            Path = f.FullName,
                            Size = f.Length,
                            DateModified = f.LastWriteTime,
                            FileType = f.Extension,
                            IsHidden = isHidden
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore permission errors — FolderViewModel handles error UI
                }
                catch (PathTooLongException)
                {
                    // Skip items with paths exceeding MAX_PATH
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error reading path {path}: {ex.Message}");
                }

                return items;
            });
        }
    }
}
