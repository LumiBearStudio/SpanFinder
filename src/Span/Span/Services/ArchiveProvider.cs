using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services;

/// <summary>
/// archive:// 스킴 프로바이더. 압축 파일 내부를 폴더처럼 탐색한다. (읽기 전용)
/// </summary>
public class ArchiveProvider : IFileSystemProvider
{
    private readonly ArchiveReaderService _reader;

    public ArchiveProvider(ArchiveReaderService reader)
    {
        _reader = reader;
    }

    public string Scheme => "archive";
    public string DisplayName => "Archive Browser";

    public async Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
    {
        var (archivePath, internalPath) = ArchivePathHelper.Parse(path);
        Helpers.DebugLogger.Log($"[ArchiveProvider] GetItemsAsync: path='{path}' → archive='{archivePath}', internal='{internalPath}'");
        var entries = await _reader.GetChildrenAsync(archivePath, internalPath, ct);
        Helpers.DebugLogger.Log($"[ArchiveProvider] GetChildrenAsync returned {entries.Count} entries");

        var items = new List<IFileSystemItem>();
        foreach (var entry in entries)
        {
            var fullPath = ArchivePathHelper.Combine(archivePath, entry.FullPath);
            if (entry.IsDirectory)
            {
                items.Add(new FolderItem
                {
                    Name = entry.Name,
                    Path = fullPath,
                    DateModified = entry.LastModified,
                });
            }
            else
            {
                items.Add(new FileItem
                {
                    Name = entry.Name,
                    Path = fullPath,
                    Size = entry.Size,
                    DateModified = entry.LastModified,
                });
            }
        }
        return items;
    }

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(true); // entries exist if we can parse the path

    public Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
    {
        var (_, internalPath) = ArchivePathHelper.Parse(path);
        // Root or path ending with / is directory; otherwise check extension
        return Task.FromResult(string.IsNullOrEmpty(internalPath) ||
            internalPath.EndsWith('/') || internalPath.EndsWith('\\') ||
            !System.IO.Path.HasExtension(internalPath));
    }

    public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
    {
        var (archivePath, internalPath) = ArchivePathHelper.Parse(path);
        return await _reader.OpenEntryAsync(archivePath, internalPath, ct);
    }

    // ── Read-only: all write operations throw ──

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");

    public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");

    public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");

    public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");

    public Task WriteAsync(string path, Stream content, CancellationToken ct = default)
        => throw new NotSupportedException("Archive is read-only");
}
