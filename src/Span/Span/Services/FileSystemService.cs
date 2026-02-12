using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    public class FileSystemService
    {
        public Task<List<DriveItem>> GetDrivesAsync()
        {
            return Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && (d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable))
                    .Select(d => new DriveItem
                    {
                        Name = $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})", // e.g. "Windows (C:)"
                        Path = d.Name,
                        Label = d.VolumeLabel,
                        TotalSize = d.TotalSize,
                        AvailableFreeSpace = d.AvailableFreeSpace,
                        DriveFormat = d.DriveFormat,
                        DriveType = d.DriveType.ToString()
                    })
                    .ToList();

                // Fallback for empty labels
                foreach (var drive in drives)
                {
                    if (string.IsNullOrEmpty(drive.Label))
                    {
                        drive.Name = $"Local Disk ({drive.Path.TrimEnd('\\')})";
                    }
                }

                return drives;
            });
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
