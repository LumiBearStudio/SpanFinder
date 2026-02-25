using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Span.Services
{
    /// <summary>
    /// 활성 탭의 표시 중인 컬럼 경로들을 감시하여 파일 변경 시 자동 새로고침을 트리거하는 서비스.
    /// Created/Deleted/Renamed만 구독 (Changed 제외 — 과다 이벤트 방지).
    /// 300ms 디바운싱으로 대량 변경 시 한 번만 리프레시.
    /// </summary>
    public class FileSystemWatcherService : IDisposable
    {
        private readonly Dictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _lock = new();

        private const int DebounceMs = 300;
        private const int ErrorDebounceMs = 1000; // 버퍼 오버플로우 시 더 긴 대기
        private const int BufferSize = 65536;

        /// <summary>
        /// 파일 변경 감지 시 발생. (changedFolderPath)
        /// UI 스레드 마샬링은 호출자 책임.
        /// </summary>
        public event Action<string>? PathChanged;

        /// <summary>
        /// 감시 경로 목록 갱신. 기존 경로는 유지, 새 경로 추가, 사라진 경로 제거.
        /// 네트워크/원격 경로는 자동 제외.
        /// </summary>
        public void SetWatchedPaths(IEnumerable<string> paths)
        {
            var newPaths = new HashSet<string>(
                paths.Where(p => !string.IsNullOrEmpty(p) && !FileSystemRouter.IsRemotePath(p) && IsLocalPath(p)),
                StringComparer.OrdinalIgnoreCase
            );

            lock (_lock)
            {
                // 제거할 경로
                var toRemove = _watchers.Keys.Where(k => !newPaths.Contains(k)).ToList();
                foreach (var path in toRemove)
                {
                    if (_watchers.TryGetValue(path, out var watcher))
                    {
                        watcher.EnableRaisingEvents = false;
                        watcher.Dispose();
                        _watchers.Remove(path);
                    }
                }

                // 추가할 경로
                foreach (var path in newPaths)
                {
                    if (_watchers.ContainsKey(path)) continue;
                    if (!Directory.Exists(path)) continue;

                    try
                    {
                        var watcher = new FileSystemWatcher(path)
                        {
                            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                            IncludeSubdirectories = false,
                            InternalBufferSize = BufferSize,
                        };

                        watcher.Created += OnFileSystemEvent;
                        watcher.Deleted += OnFileSystemEvent;
                        watcher.Renamed += OnFileSystemEvent;
                        watcher.Error += OnWatcherError;
                        watcher.EnableRaisingEvents = true;

                        _watchers[path] = watcher;
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[FileSystemWatcher] 감시 실패: {path} - {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// 모든 감시 중지.
        /// </summary>
        public void StopAll()
        {
            lock (_lock)
            {
                foreach (var watcher in _watchers.Values)
                {
                    watcher.EnableRaisingEvents = false;
                    watcher.Dispose();
                }
                _watchers.Clear();
            }

            foreach (var timer in _debounceTimers.Values)
                timer.Dispose();
            _debounceTimers.Clear();
        }

        private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
        {
            if (sender is not FileSystemWatcher watcher) return;
            var folderPath = watcher.Path;

            DebouncedNotify(folderPath);
        }

        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (sender is not FileSystemWatcher watcher) return;
            var path = watcher.Path;
            Helpers.DebugLogger.Log($"[FileSystemWatcher] 버퍼 오버플로우: {path} - {e.GetException().Message}");

            // 버퍼 오버플로우 시: watcher 재생성 + 긴 디바운스로 전체 리프레시
            RecreateWatcher(path);
            DebouncedNotify(path, ErrorDebounceMs);
        }

        /// <summary>
        /// 죽은 watcher를 dispose하고 동일 경로로 새로 생성.
        /// 버퍼 오버플로우 후 watcher는 더 이상 이벤트를 발생시키지 않으므로
        /// 반드시 재생성해야 이후 변경 감지가 유지됨.
        /// </summary>
        private void RecreateWatcher(string path)
        {
            lock (_lock)
            {
                if (_watchers.TryGetValue(path, out var oldWatcher))
                {
                    oldWatcher.EnableRaisingEvents = false;
                    oldWatcher.Dispose();
                    _watchers.Remove(path);
                }

                if (!Directory.Exists(path)) return;

                try
                {
                    var newWatcher = new FileSystemWatcher(path)
                    {
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                        IncludeSubdirectories = false,
                        InternalBufferSize = BufferSize,
                    };

                    newWatcher.Created += OnFileSystemEvent;
                    newWatcher.Deleted += OnFileSystemEvent;
                    newWatcher.Renamed += OnFileSystemEvent;
                    newWatcher.Error += OnWatcherError;
                    newWatcher.EnableRaisingEvents = true;

                    _watchers[path] = newWatcher;
                    Helpers.DebugLogger.Log($"[FileSystemWatcher] 재생성 완료: {path}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[FileSystemWatcher] 재생성 실패: {path} - {ex.Message}");
                }
            }
        }

        private void DebouncedNotify(string folderPath, int delayMs = DebounceMs)
        {
            if (_debounceTimers.TryGetValue(folderPath, out var existing))
            {
                existing.Change(delayMs, Timeout.Infinite);
            }
            else
            {
                var timer = new Timer(_ =>
                {
                    // 타이머 실행 후 딕셔너리에서 제거 (메모리 누적 방지)
                    if (_debounceTimers.TryRemove(folderPath, out var removed))
                        removed.Dispose();
                    PathChanged?.Invoke(folderPath);
                }, null, delayMs, Timeout.Infinite);
                _debounceTimers[folderPath] = timer;
            }
        }

        private static bool IsLocalPath(string path)
        {
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC 경로 제외
            if (path.Length >= 2 && path[1] == ':') return true; // C:\... 등
            return false;
        }

        public void Dispose()
        {
            StopAll();
            GC.SuppressFinalize(this);
        }
    }
}
