using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
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
                // Check if drive is ready
                if (!drive.IsReady)
                {
                    return null;
                }

                var driveItem = new DriveItem
                {
                    Path = drive.Name,
                    Label = drive.VolumeLabel ?? string.Empty,
                    DriveFormat = drive.DriveFormat,
                    DriveType = drive.DriveType.ToString()
                };

                // Try to get size information (can be slow)
                try
                {
                    driveItem.TotalSize = drive.TotalSize;
                    driveItem.AvailableFreeSpace = drive.AvailableFreeSpace;
                }
                catch
                {
                    // Size info not available - continue without it
                    driveItem.TotalSize = 0;
                    driveItem.AvailableFreeSpace = 0;
                }

                // Set icon based on drive type (uses current icon pack)
                driveItem.IconGlyph = IconService.Current?.GetDriveGlyph(driveItem.DriveType) ?? "\uEC65";

                // Generate display name based on drive type
                var driveLetter = driveItem.Path.TrimEnd('\\');
                var defaultLabel = drive.DriveType switch
                {
                    DriveType.Fixed => "Local Disk",
                    DriveType.Removable => "USB Drive",
                    DriveType.Network => "Network Drive",
                    DriveType.CDRom => "CD/DVD Drive",
                    _ => "Drive"
                };
                driveItem.Name = string.IsNullOrEmpty(driveItem.Label)
                    ? $"{defaultLabel} ({driveLetter})"
                    : $"{driveItem.Label} ({driveLetter})";

                return driveItem;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystemService] Error creating DriveItem for {drive.Name}: {ex.Message}");
                return null;
            }
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

                    // Get Directories
                    foreach (var d in dirInfo.GetDirectories())
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

                    // Get Files
                    foreach (var f in dirInfo.GetFiles())
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
