using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    public class LocalFileSystemProvider : IFileSystemProvider
    {
        private readonly SettingsService _settings;

        public LocalFileSystemProvider(SettingsService settings)
        {
            _settings = settings;
        }

        public string Scheme => "file";
        public string DisplayName => "Local File System";

        public Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var items = new List<IFileSystemItem>();

                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                    return (IReadOnlyList<IFileSystemItem>)items;

                try
                {
                    var dirInfo = new DirectoryInfo(path);

                    foreach (var d in dirInfo.GetDirectories())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!_settings.ShowHiddenFiles && (d.Attributes & FileAttributes.Hidden) != 0) continue;

                        items.Add(new FolderItem
                        {
                            Name = d.Name,
                            Path = d.FullName,
                            DateModified = d.LastWriteTime,
                            IsHidden = (d.Attributes & FileAttributes.Hidden) != 0
                        });
                    }

                    foreach (var f in dirInfo.GetFiles())
                    {
                        ct.ThrowIfCancellationRequested();
                        if (!_settings.ShowHiddenFiles && (f.Attributes & FileAttributes.Hidden) != 0) continue;

                        items.Add(new FileItem
                        {
                            Name = f.Name,
                            Path = f.FullName,
                            Size = f.Length,
                            DateModified = f.LastWriteTime,
                            FileType = f.Extension,
                            IsHidden = (f.Attributes & FileAttributes.Hidden) != 0
                        });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (UnauthorizedAccessException)
                {
                    // Silently ignore permission errors
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LocalFileSystemProvider] Error reading {path}: {ex.Message}");
                }

                return (IReadOnlyList<IFileSystemItem>)items;
            }, ct);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() => Directory.Exists(path) || File.Exists(path), ct);
        }

        public Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() => Directory.Exists(path), ct);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() => Directory.CreateDirectory(path), ct);
        }

        public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive);
                else if (File.Exists(path))
                    File.Delete(path);
            }, ct);
        }

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);
                else if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
            }, ct);
        }

        public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(sourcePath))
                    CopyDirectoryRecursive(sourcePath, destPath, ct);
                else if (File.Exists(sourcePath))
                    File.Copy(sourcePath, destPath, overwrite: true);
            }, ct);
        }

        public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (Directory.Exists(sourcePath))
                    Directory.Move(sourcePath, destPath);
                else if (File.Exists(sourcePath))
                    File.Move(sourcePath, destPath, overwrite: true);
            }, ct);
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        {
            return Task.Run<Stream>(() => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read), ct);
        }

        public Task WriteAsync(string path, Stream content, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                await content.CopyToAsync(fs, ct);
            }, ct);
        }

        private static void CopyDirectoryRecursive(string sourceDir, string destDir, CancellationToken ct)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var subDir in Directory.GetDirectories(sourceDir))
            {
                ct.ThrowIfCancellationRequested();
                var destSubDir = Path.Combine(destDir, new DirectoryInfo(subDir).Name);
                CopyDirectoryRecursive(subDir, destSubDir, ct);
            }
        }
    }
}
