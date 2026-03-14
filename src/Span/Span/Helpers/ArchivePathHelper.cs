using System;
using System.Collections.Generic;

namespace Span.Helpers;

/// <summary>
/// Static utility class for archive:// path parsing and manipulation.
/// </summary>
public static class ArchivePathHelper
{
    public const string Scheme = "archive";
    public const string Prefix = "archive://";

    /// <summary>
    /// Compound extensions must come before their simple counterparts
    /// so that EndsWith checks match the longest extension first.
    /// </summary>
    private static readonly string[] CompoundExtensions =
    [
        ".tar.gz", ".tar.bz2", ".tar.xz"
    ];

    public static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
        ".tgz", ".tbz2", ".txz",
        ".tar.gz", ".tar.bz2", ".tar.xz",
        ".cab"
    };

    /// <summary>
    /// Returns true if the path starts with "archive://".
    /// </summary>
    public static bool IsArchivePath(string path)
    {
        return !string.IsNullOrEmpty(path)
            && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true if the file path has a known archive extension.
    /// Handles compound extensions like .tar.gz.
    /// </summary>
    public static bool IsArchiveFile(string path)
    {
        if (string.IsNullOrEmpty(path))
            return false;

        // Never treat directories as archive files, even if they have archive extensions (e.g. folder named "8.ZIP")
        try
        {
            if (System.IO.Directory.Exists(path))
            {
                DebugLogger.Log($"[IsArchiveFile] Directory.Exists=true, returning false for: {path}");
                return false;
            }
        }
        catch { /* ignore access errors; fall through to extension check */ }

        // Check compound extensions first (e.g. .tar.gz)
        foreach (var ext in CompoundExtensions)
        {
            if (path.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                DebugLogger.Log($"[IsArchiveFile] Compound ext match '{ext}', returning true for: {path}");
                return true;
            }
        }

        // Fall back to simple extension check
        var extension = System.IO.Path.GetExtension(path);
        var result = !string.IsNullOrEmpty(extension) && ArchiveExtensions.Contains(extension);
        DebugLogger.Log($"[IsArchiveFile] ext='{extension}', result={result} for: {path}");
        return result;
    }

    /// <summary>
    /// Parses an archive:// path into (ArchiveFilePath, InternalPath).
    /// Example: "archive://C:/files/test.zip/src/main.cs"
    ///   → ArchiveFilePath = "C:/files/test.zip", InternalPath = "src/main.cs"
    /// </summary>
    public static (string ArchiveFilePath, string InternalPath) Parse(string archivePath)
    {
        if (string.IsNullOrEmpty(archivePath))
            throw new ArgumentException("Archive path cannot be null or empty.", nameof(archivePath));

        if (!IsArchivePath(archivePath))
            throw new ArgumentException($"Path must start with '{Prefix}'.", nameof(archivePath));

        var pathWithoutPrefix = archivePath.Substring(Prefix.Length);

        // Collect all candidate split points (extension boundary positions)
        // then pick the first one that corresponds to an actual file on disk.
        // This handles cases like "D:\8.ZIP\real_archive.zip\internal\path"
        // where "8.ZIP" is a folder, not an archive file.
        var candidates = new System.Collections.Generic.List<(int boundaryEnd, string ext)>();

        // Compound extensions first
        foreach (var ext in CompoundExtensions)
        {
            int searchFrom = 0;
            while (searchFrom < pathWithoutPrefix.Length)
            {
                var idx = pathWithoutPrefix.IndexOf(ext, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                var boundaryEnd = idx + ext.Length;
                if (boundaryEnd == pathWithoutPrefix.Length
                    || pathWithoutPrefix[boundaryEnd] == '/'
                    || pathWithoutPrefix[boundaryEnd] == '\\')
                {
                    candidates.Add((boundaryEnd, ext));
                }
                searchFrom = idx + 1;
            }
        }

        // Simple extensions
        foreach (var ext in ArchiveExtensions)
        {
            if (ext.StartsWith(".tar.", StringComparison.OrdinalIgnoreCase))
                continue;

            int searchFrom = 0;
            while (searchFrom < pathWithoutPrefix.Length)
            {
                var idx = pathWithoutPrefix.IndexOf(ext, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;
                var boundaryEnd = idx + ext.Length;
                if (boundaryEnd == pathWithoutPrefix.Length
                    || pathWithoutPrefix[boundaryEnd] == '/'
                    || pathWithoutPrefix[boundaryEnd] == '\\')
                {
                    candidates.Add((boundaryEnd, ext));
                }
                searchFrom = idx + 1;
            }
        }

        // Sort by position (leftmost first) and pick the first that is an actual file
        candidates.Sort((a, b) => a.boundaryEnd.CompareTo(b.boundaryEnd));

        foreach (var (boundaryEnd, ext) in candidates)
        {
            var archiveFile = pathWithoutPrefix.Substring(0, boundaryEnd);
            try
            {
                // Skip if this path is a directory (e.g. folder named "8.ZIP")
                if (System.IO.Directory.Exists(archiveFile))
                    continue;
            }
            catch { /* access error; try this candidate */ }

            var internalPath = boundaryEnd < pathWithoutPrefix.Length
                ? pathWithoutPrefix.Substring(boundaryEnd + 1) // skip separator
                : string.Empty;
            return (archiveFile, internalPath);
        }

        // Fallback: if no File.Exists matched (e.g. file was deleted), use first candidate
        if (candidates.Count > 0)
        {
            var first = candidates[0];
            var archiveFile = pathWithoutPrefix.Substring(0, first.boundaryEnd);
            var internalPath = first.boundaryEnd < pathWithoutPrefix.Length
                ? pathWithoutPrefix.Substring(first.boundaryEnd + 1)
                : string.Empty;
            return (archiveFile, internalPath);
        }

        // No known extension found; treat the whole path as the archive file
        return (pathWithoutPrefix, string.Empty);
    }

    /// <summary>
    /// Builds an archive:// path from an archive file path and an internal path.
    /// </summary>
    public static string Combine(string archiveFilePath, string internalPath)
    {
        if (string.IsNullOrEmpty(archiveFilePath))
            throw new ArgumentException("Archive file path cannot be null or empty.", nameof(archiveFilePath));

        if (string.IsNullOrEmpty(internalPath))
            return $"{Prefix}{archiveFilePath}";

        // Normalize: ensure no double separators
        var normalizedArchive = archiveFilePath.TrimEnd('/', '\\');
        var normalizedInternal = internalPath.TrimStart('/', '\\');

        return $"{Prefix}{normalizedArchive}/{normalizedInternal}";
    }

    /// <summary>
    /// Tries to detect an archive file boundary in a plain path (without archive:// prefix)
    /// and build an archive:// URI.
    /// Example: "D:\folder\archive.zip\internal\path" → "archive://D:\folder\archive.zip/internal/path"
    /// Returns null if no archive boundary found.
    /// </summary>
    public static string? TryBuildArchiveUri(string plainPath)
    {
        if (string.IsNullOrEmpty(plainPath) || IsArchivePath(plainPath))
            return null;

        // Walk the path segments and check if any prefix is an actual archive file
        var separators = new[] { '\\', '/' };
        int searchFrom = 0;
        while (searchFrom < plainPath.Length)
        {
            int nextSep = plainPath.IndexOfAny(separators, searchFrom);
            if (nextSep < 0) nextSep = plainPath.Length;

            var candidate = plainPath.Substring(0, nextSep);
            var ext = System.IO.Path.GetExtension(candidate);
            if (!string.IsNullOrEmpty(ext) && ArchiveExtensions.Contains(ext))
            {
                try
                {
                    if (System.IO.File.Exists(candidate))
                    {
                        var internalPath = nextSep < plainPath.Length
                            ? plainPath.Substring(nextSep + 1).Replace('\\', '/')
                            : string.Empty;
                        return Combine(candidate, internalPath);
                    }
                }
                catch { /* access error */ }
            }

            searchFrom = nextSep + 1;
        }

        return null;
    }
}
