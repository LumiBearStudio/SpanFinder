using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// 폴더 크기를 백그라운드에서 계산하고 캐시하는 서비스.
    /// Details 뷰에서 폴더 크기 표시에 사용.
    /// </summary>
    public class FolderSizeService
    {
        private readonly ConcurrentDictionary<string, long> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.OrdinalIgnoreCase);

        private const int MaxDepth = 8;

        /// <summary>
        /// 폴더 크기 계산 완료 시 발생. (folderPath, sizeInBytes)
        /// </summary>
        public event Action<string, long>? SizeCalculated;

        /// <summary>
        /// 캐시된 크기 반환. 없으면 null.
        /// </summary>
        public long? TryGetCachedSize(string folderPath)
        {
            return _cache.TryGetValue(folderPath, out var size) ? size : null;
        }

        /// <summary>
        /// 폴더 크기를 백그라운드에서 계산 요청. 이미 계산 중이면 무시.
        /// </summary>
        public void RequestCalculation(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return;
            if (_cache.ContainsKey(folderPath)) return;
            if (!_pending.TryAdd(folderPath, 0)) return;

            _ = Task.Run(() =>
            {
                try
                {
                    long size = CalculateFolderSize(folderPath, 0);
                    _cache[folderPath] = size;
                    SizeCalculated?.Invoke(folderPath, size);
                }
                catch
                {
                    _cache[folderPath] = -1; // 접근 불가 표시
                }
                finally
                {
                    _pending.TryRemove(folderPath, out _);
                }
            });
        }

        /// <summary>
        /// 특정 폴더의 캐시 무효화.
        /// </summary>
        public void Invalidate(string folderPath)
        {
            _cache.TryRemove(folderPath, out _);
        }

        private static long CalculateFolderSize(string path, int depth)
        {
            if (depth >= MaxDepth) return 0;

            long total = 0;
            try
            {
                var dirInfo = new DirectoryInfo(path);

                foreach (var file in dirInfo.EnumerateFiles())
                {
                    try { total += file.Length; }
                    catch { /* 보호된 파일 무시 */ }
                }

                foreach (var dir in dirInfo.EnumerateDirectories())
                {
                    try
                    {
                        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) continue; // 심볼릭 링크 제외
                        total += CalculateFolderSize(dir.FullName, depth + 1);
                    }
                    catch { /* 접근 불가 폴더 무시 */ }
                }
            }
            catch { /* 접근 불가 무시 */ }

            return total;
        }
    }
}
