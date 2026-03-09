using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Services;

/// <summary>
/// Summary information about an archive file.
/// </summary>
public class ArchiveInfo
{
    public int TotalFiles { get; init; }
    public int TotalFolders { get; init; }
    public long UncompressedSize { get; init; }
    public long CompressedSize { get; init; }
    public double CompressionRatio { get; init; }

    /// <summary>
    /// Tree preview of archive contents (max 2 levels deep, max 50 items).
    /// </summary>
    public IReadOnlyList<ArchiveEntryInfo> TopEntries { get; init; } = [];
}

/// <summary>
/// Information about a single entry (file or directory) within an archive.
/// </summary>
public class ArchiveEntryInfo
{
    public string Name { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime LastModified { get; init; }
    public int Depth { get; init; }
    public int ChildCount { get; init; }
}

/// <summary>
/// Service for reading archive file entries.
/// Phase 1: ZIP only (System.IO.Compression).
/// </summary>
public class ArchiveReaderService
{
    private const int MaxTopEntries = 50;
    private const int MaxPreviewDepth = 2;

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip"
    };

    /// <summary>
    /// Check if the file format is supported (Phase 1: only .zip).
    /// </summary>
    public bool IsSupported(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return false;

        var ext = Path.GetExtension(filePath);
        return SupportedExtensions.Contains(ext);
    }

    /// <summary>
    /// Get all entries from an archive, returning summary info and a tree preview.
    /// </summary>
    public Task<ArchiveInfo> GetArchiveInfoAsync(string archiveFilePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var fileInfo = new FileInfo(archiveFilePath);
            if (!fileInfo.Exists)
                throw new FileNotFoundException("Archive file not found.", archiveFilePath);

            var compressedSize = fileInfo.Length;

            try
            {
                using var stream = new FileStream(
                    archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                ct.ThrowIfCancellationRequested();

                // Build virtual folder tree from flat zip entries
                var tree = BuildVirtualTree(archive.Entries, ct);

                var totalFiles = 0;
                var totalFolders = 0;
                long uncompressedSize = 0;

                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    if (IsDirectoryEntry(entry))
                    {
                        totalFolders++;
                    }
                    else
                    {
                        totalFiles++;
                        uncompressedSize += entry.Length;
                    }
                }

                // Add virtual directories that were not explicit zip entries
                totalFolders += tree.VirtualFolderCount;

                var compressionRatio = compressedSize > 0 && uncompressedSize > 0
                    ? Math.Round((1.0 - (double)compressedSize / uncompressedSize) * 100.0, 1)
                    : 0.0;

                var topEntries = CollectTopEntries(tree.Root, MaxPreviewDepth, MaxTopEntries);

                return new ArchiveInfo
                {
                    TotalFiles = totalFiles,
                    TotalFolders = totalFolders,
                    UncompressedSize = uncompressedSize,
                    CompressedSize = compressedSize,
                    CompressionRatio = compressionRatio,
                    TopEntries = topEntries,
                };
            }
            catch (InvalidDataException)
            {
                // Corrupted, password-protected, or unreadable ZIP
                return new ArchiveInfo
                {
                    TotalFiles = -1,
                    TotalFolders = 0,
                    UncompressedSize = 0,
                    CompressedSize = compressedSize,
                    CompressionRatio = 0,
                    TopEntries = [],
                };
            }
            catch (IOException)
            {
                // File locked or I/O error
                return new ArchiveInfo
                {
                    TotalFiles = -1,
                    TotalFolders = 0,
                    UncompressedSize = 0,
                    CompressedSize = compressedSize,
                    CompressionRatio = 0,
                    TopEntries = [],
                };
            }
        }, ct);
    }

    /// <summary>
    /// Get children of a specific internal path within an archive.
    /// </summary>
    public Task<IReadOnlyList<ArchiveEntryInfo>> GetChildrenAsync(
        string archiveFilePath, string internalPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(archiveFilePath))
                throw new FileNotFoundException("Archive file not found.", archiveFilePath);

            try
            {
                using var stream = new FileStream(
                    archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

                ct.ThrowIfCancellationRequested();

                var tree = BuildVirtualTree(archive.Entries, ct);

                // Navigate to the target node
                var targetNode = tree.Root;
                if (!string.IsNullOrEmpty(internalPath))
                {
                    var segments = internalPath.TrimEnd('/').Split('/');
                    foreach (var segment in segments)
                    {
                        if (targetNode.Children.TryGetValue(segment, out var child))
                        {
                            targetNode = child;
                        }
                        else
                        {
                            // Path not found; return empty list
                            return (IReadOnlyList<ArchiveEntryInfo>)[];
                        }
                    }
                }

                var result = new List<ArchiveEntryInfo>();
                foreach (var child in targetNode.Children.Values
                    .OrderBy(c => !c.IsDirectory) // folders first
                    .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
                {
                    ct.ThrowIfCancellationRequested();

                    result.Add(new ArchiveEntryInfo
                    {
                        Name = child.Name,
                        FullPath = child.FullPath,
                        IsDirectory = child.IsDirectory,
                        Size = child.Size,
                        LastModified = child.LastModified,
                        Depth = child.Depth,
                        ChildCount = child.IsDirectory ? child.Children.Count : 0,
                    });
                }

                return (IReadOnlyList<ArchiveEntryInfo>)result;
            }
            catch (InvalidDataException)
            {
                // Corrupted, password-protected, or unreadable ZIP
                return (IReadOnlyList<ArchiveEntryInfo>)[];
            }
            catch (IOException)
            {
                // File locked or I/O error
                return (IReadOnlyList<ArchiveEntryInfo>)[];
            }
        }, ct);
    }

    /// <summary>
    /// Open a read-only stream for a specific entry within the archive.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    public Task<Stream> OpenEntryAsync(
        string archiveFilePath, string entryPath, CancellationToken ct = default)
    {
        return Task.Run<Stream>(() =>
        {
            ct.ThrowIfCancellationRequested();

            if (!File.Exists(archiveFilePath))
                throw new FileNotFoundException("Archive file not found.", archiveFilePath);

            // We must keep the file stream and archive alive until the caller
            // disposes the returned stream. Use a MemoryStream copy.
            using var fileStream = new FileStream(
                archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);

            ct.ThrowIfCancellationRequested();

            // Normalize path separators for matching
            var normalizedPath = entryPath.Replace('\\', '/');

            var entry = archive.Entries.FirstOrDefault(e =>
                string.Equals(e.FullName.TrimEnd('/'), normalizedPath.TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase));

            if (entry is null)
                throw new FileNotFoundException($"Entry not found in archive: {entryPath}");

            // Copy to MemoryStream so we can dispose the archive
            var memoryStream = new MemoryStream();
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(memoryStream);
            }
            memoryStream.Position = 0;

            return memoryStream;
        }, ct);
    }

    #region Private helpers

    /// <summary>
    /// 폴더 노드의 LastModified를 자식 중 가장 최근 날짜로 갱신 (재귀, bottom-up).
    /// </summary>
    private static DateTime PropagateLatestDate(TreeNode node)
    {
        var latest = node.LastModified;
        foreach (var child in node.Children.Values)
        {
            var childDate = PropagateLatestDate(child);
            if (childDate > latest)
                latest = childDate;
        }
        if (node.IsDirectory && latest > node.LastModified)
            node.LastModified = latest;
        return latest;
    }

    private static bool IsDirectoryEntry(ZipArchiveEntry entry)
    {
        return entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
    }

    private sealed class TreeNode
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
        public int Depth { get; set; }
        public Dictionary<string, TreeNode> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class VirtualTree
    {
        public TreeNode Root { get; init; } = new();
        public int VirtualFolderCount { get; init; }
    }

    private static VirtualTree BuildVirtualTree(
        IReadOnlyCollection<ZipArchiveEntry> entries, CancellationToken ct)
    {
        var root = new TreeNode { IsDirectory = true, Depth = -1 };
        var explicitDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allDirPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // First pass: register explicit directory entries
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            if (IsDirectoryEntry(entry))
            {
                explicitDirs.Add(entry.FullName.TrimEnd('/', '\\'));
            }
        }

        // Second pass: build tree
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();

            var fullName = entry.FullName.Replace('\\', '/').TrimEnd('/');
            if (string.IsNullOrEmpty(fullName))
                continue;

            var parts = fullName.Split('/');
            var current = root;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                var isLast = i == parts.Length - 1;
                var partPath = string.Join("/", parts.Take(i + 1));

                var entryDate = entry.LastWriteTime.DateTime;

                if (!current.Children.TryGetValue(part, out var child))
                {
                    var isDir = !isLast || IsDirectoryEntry(entry);
                    child = new TreeNode
                    {
                        Name = part,
                        FullPath = partPath,
                        IsDirectory = isDir,
                        Depth = i,
                        Size = isLast && !isDir ? entry.Length : 0,
                        LastModified = entryDate,
                    };
                    current.Children[part] = child;

                    if (isDir)
                    {
                        allDirPaths.Add(partPath);
                    }
                }
                else
                {
                    if (isLast && !IsDirectoryEntry(entry))
                    {
                        // Update file info if we're revisiting
                        child.Size = entry.Length;
                        child.LastModified = entryDate;
                    }
                    // Propagate newest date to parent folders
                    if (entryDate > child.LastModified)
                        child.LastModified = entryDate;
                }

                current = child;
            }
        }

        // Propagate latest date from children up to parent folders
        PropagateLatestDate(root);

        // Count virtual folders (directories we created that were not explicit zip entries)
        var virtualCount = 0;
        foreach (var dir in allDirPaths)
        {
            if (!explicitDirs.Contains(dir))
                virtualCount++;
        }

        return new VirtualTree { Root = root, VirtualFolderCount = virtualCount };
    }

    private static IReadOnlyList<ArchiveEntryInfo> CollectTopEntries(
        TreeNode root, int maxDepth, int maxItems)
    {
        var result = new List<ArchiveEntryInfo>();
        CollectEntriesRecursive(root, maxDepth, maxItems, result);
        return result;
    }

    private static void CollectEntriesRecursive(
        TreeNode node, int maxDepth, int maxItems, List<ArchiveEntryInfo> result)
    {
        // Sort: directories first, then alphabetical
        var children = node.Children.Values
            .OrderBy(c => !c.IsDirectory)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var child in children)
        {
            if (result.Count >= maxItems)
                return;

            result.Add(new ArchiveEntryInfo
            {
                Name = child.Name,
                FullPath = child.FullPath,
                IsDirectory = child.IsDirectory,
                Size = child.Size,
                LastModified = child.LastModified,
                Depth = child.Depth,
                ChildCount = child.IsDirectory ? child.Children.Count : 0,
            });

            // Recurse into directories if within depth limit
            if (child.IsDirectory && child.Depth + 1 < maxDepth)
            {
                CollectEntriesRecursive(child, maxDepth, maxItems, result);
            }
        }
    }

    #endregion
}
