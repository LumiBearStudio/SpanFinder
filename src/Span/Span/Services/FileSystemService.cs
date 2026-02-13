using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    public class FileSystemService
    {
        private const int DriveLoadTimeoutMs = 500; // 500ms timeout per drive

        /// <summary>
        /// Get all available drives with parallel loading and timeout protection
        /// </summary>
        public async Task<List<DriveItem>> GetDrivesAsync()
        {
            var allDrives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable);

            // Load each drive in parallel with timeout
            var tasks = allDrives.Select(drive => LoadDriveWithTimeoutAsync(drive));
            var results = await Task.WhenAll(tasks);

            // Filter out failed drives (null results)
            return results.Where(d => d != null).ToList()!;
        }

        /// <summary>
        /// Load a single drive with timeout protection
        /// </summary>
        private async Task<DriveItem?> LoadDriveWithTimeoutAsync(DriveInfo drive)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(DriveLoadTimeoutMs));
                return await Task.Run(() => CreateDriveItem(drive), cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Timeout occurred - skip this drive
                System.Diagnostics.Debug.WriteLine($"[FileSystemService] Drive {drive.Name} timed out after {DriveLoadTimeoutMs}ms");
                return null;
            }
            catch (Exception ex)
            {
                // Other errors (access denied, etc.) - skip this drive
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

                // Generate display name
                driveItem.Name = string.IsNullOrEmpty(driveItem.Label)
                    ? $"Local Disk ({driveItem.Path.TrimEnd('\\')})"
                    : $"{driveItem.Label} ({driveItem.Path.TrimEnd('\\')})";

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
                        if ((d.Attributes & FileAttributes.Hidden) != 0) continue; // Skip hidden

                        items.Add(new FolderItem
                        {
                            Name = d.Name,
                            Path = d.FullName,
                            DateModified = d.LastWriteTime
                        });
                    }

                    // Get Files
                    foreach (var f in dirInfo.GetFiles())
                    {
                        if ((f.Attributes & FileAttributes.Hidden) != 0) continue;

                        items.Add(new FileItem
                        {
                            Name = f.Name,
                            Path = f.FullName,
                            Size = f.Length,
                            DateModified = f.LastWriteTime,
                            FileType = f.Extension
                        });
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    // Ignore permission errors for now (or maybe add a placeholder item?)
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
