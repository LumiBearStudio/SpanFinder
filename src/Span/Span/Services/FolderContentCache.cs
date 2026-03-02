using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// Application-level directory content cache.
    /// Caches raw IFileSystemItem models (not ViewModels) keyed by normalized folder path.
    /// On cache hit, FolderViewModel creates ViewModels from cached models (CPU only, no disk I/O).
    /// On miss, disk I/O populates the cache.
    /// Thread-safe via ConcurrentDictionary.
    /// </summary>
    public class FolderContentCache
    {
        public record CachedFolder(
            List<FolderItem> Folders,
            List<FileItem> Files,
            DateTime DirectoryLastWriteTimeUtc,
            bool IncludesHidden,
            long AccessTick);

        private readonly ConcurrentDictionary<string, CachedFolder> _cache
            = new(StringComparer.OrdinalIgnoreCase);

        private long _accessCounter; // LRU 순서 추적용 단조 증가 카운터

        private const int MaxEntries = 500;

        /// <summary>
        /// Try to get cached folder content. Returns null if not cached or stale.
        /// Validates freshness by comparing Directory.GetLastWriteTimeUtc (single metadata call, ~0.1ms).
        /// </summary>
        public CachedFolder? TryGet(string path, bool showHidden)
        {
            if (!_cache.TryGetValue(path, out var cached))
                return null;

            // Freshness check: compare directory last write time
            try
            {
                var currentWriteTime = Directory.GetLastWriteTimeUtc(path);
                if (currentWriteTime != cached.DirectoryLastWriteTimeUtc)
                {
                    // Stale — remove and return null
                    _cache.TryRemove(path, out _);
                    return null;
                }
            }
            catch
            {
                // Directory might no longer exist
                _cache.TryRemove(path, out _);
                return null;
            }

            // Hidden files setting mismatch — need fresh enumeration
            if (cached.IncludesHidden != showHidden)
            {
                _cache.TryRemove(path, out _);
                return null;
            }

            // LRU: 접근 시각 갱신
            var tick = System.Threading.Interlocked.Increment(ref _accessCounter);
            _cache[path] = cached with { AccessTick = tick };

            return cached;
        }

        /// <summary>
        /// Store folder content in cache.
        /// </summary>
        public void Set(string path, List<FolderItem> folders, List<FileItem> files, bool showHidden)
        {
            // LRU Eviction: 가장 오래된 항목부터 제거
            if (_cache.Count >= MaxEntries)
            {
                int toRemove = MaxEntries / 10;
                var oldest = _cache
                    .OrderBy(kvp => kvp.Value.AccessTick)
                    .Take(toRemove)
                    .Select(kvp => kvp.Key)
                    .ToList();
                foreach (var key in oldest)
                    _cache.TryRemove(key, out _);
            }

            try
            {
                var writeTime = Directory.GetLastWriteTimeUtc(path);
                var tick = System.Threading.Interlocked.Increment(ref _accessCounter);
                _cache[path] = new CachedFolder(folders, files, writeTime, showHidden, tick);
            }
            catch
            {
                // Directory might not exist anymore
            }
        }

        /// <summary>
        /// Invalidate a specific path (e.g., after file operations).
        /// </summary>
        public void Invalidate(string path)
        {
            _cache.TryRemove(path, out _);
        }

        /// <summary>
        /// Clear entire cache (e.g., on settings change).
        /// </summary>
        public void Clear()
        {
            _cache.Clear();
        }

        /// <summary>
        /// Number of cached entries (for diagnostics).
        /// </summary>
        public int Count => _cache.Count;
    }
}
