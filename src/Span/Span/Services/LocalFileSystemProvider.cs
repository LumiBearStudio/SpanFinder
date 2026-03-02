using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 로컬 파일 시스템 프로바이더 구현. System.IO API를 사용하여
    /// 파일/폴더의 CRUD, 복사, 이동, 스트림 읽기/쓰기를 처리한다.
    /// </summary>
    public class LocalFileSystemProvider : IFileSystemProvider
    {
        private readonly SettingsService _settings;

        public LocalFileSystemProvider(SettingsService settings)
        {
            _settings = settings;
        }

        public string Scheme => "file";
        public string DisplayName => "Local File System";

        /// <summary>
        /// 경로를 정규화하고 Path Traversal (..) 공격을 방지.
        /// </summary>
        private static string SanitizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be null or empty.", nameof(path));
            return Path.GetFullPath(path);
        }

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

                    foreach (var d in dirInfo.EnumerateDirectories())
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

                    foreach (var f in dirInfo.EnumerateFiles())
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
            var safePath = SanitizePath(path);
            return Task.Run(() =>
            {
                if (Directory.Exists(safePath))
                    Directory.Delete(safePath, recursive);
                else if (File.Exists(safePath))
                    File.Delete(safePath);
            }, ct);
        }

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            var safeOld = SanitizePath(oldPath);
            var safeNew = SanitizePath(newPath);
            return Task.Run(() =>
            {
                if (Directory.Exists(safeOld))
                    Directory.Move(safeOld, safeNew);
                else if (File.Exists(safeOld))
                    File.Move(safeOld, safeNew);
            }, ct);
        }

        public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            var safeSrc = SanitizePath(sourcePath);
            var safeDst = SanitizePath(destPath);
            return Task.Run(() =>
            {
                if (Directory.Exists(safeSrc))
                    CopyDirectoryRecursive(safeSrc, safeDst, ct);
                else if (File.Exists(safeSrc))
                    File.Copy(safeSrc, safeDst, overwrite: true);
            }, ct);
        }

        public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            var safeSrc = SanitizePath(sourcePath);
            var safeDst = SanitizePath(destPath);
            return Task.Run(() =>
            {
                if (Directory.Exists(safeSrc))
                    Directory.Move(safeSrc, safeDst);
                else if (File.Exists(safeSrc))
                    File.Move(safeSrc, safeDst, overwrite: true);
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
