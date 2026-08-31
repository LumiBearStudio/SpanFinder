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

        /// <summary>재생성이 반복 실패하는 경로. _lock 아래에서만 접근한다.</summary>
        private readonly Dictionary<string, (int Count, DateTime FirstAt)> _recreateFailures =
            new(StringComparer.OrdinalIgnoreCase);

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

                // 컬럼 구성이 바뀌었으니 재생성 포기 이력을 지운다 — 사용자가 다시 찾아온
                // 경로는 한 번 더 시도해 볼 가치가 있다.
                foreach (var path in newPaths)
                    _recreateFailures.Remove(path);

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

            // 정상 이벤트가 왔다는 것은 이 경로의 감시가 살아났다는 뜻이다.
            // 재생성 실패 이력을 지워 다음에 오류가 나면 다시 재시도할 수 있게 한다.
            lock (_lock) { _recreateFailures.Remove(folderPath); }

            DebouncedNotify(folderPath);
        }

        /// <summary>
        /// Error 이벤트는 성격이 다른 두 사건을 하나로 합쳐서 보낸다 (.NET 소스 확인):
        ///
        ///   InternalBufferOverflowException — 이벤트가 밀렸을 뿐 watcher는 살아 있다.
        ///     .NET이 다음 루프를 계속 돌리므로 재생성하면 멀쩡한 watcher를 버리는 셈이다.
        ///     문서화된 지침도 "재열거"이지 재생성이 아니다.
        ///
        ///   그 외 Win32 오류 — .NET이 Error를 올리기 전에 이미 EnableRaisingEvents=false를
        ///     실행했다. watcher는 죽어 있고 재생성만이 복구 수단이다.
        ///
        /// 그리고 재생성에 실패했으면 통보하지 않는다. 이전에는 성공 여부와 무관하게
        /// 발사해서, 폴더가 사라진 상황에 캐시 무효화 → 리로드 실패 → 보고 있던 목록이
        /// 비워졌다.
        /// </summary>
        private void OnWatcherError(object sender, ErrorEventArgs e)
        {
            if (sender is not FileSystemWatcher watcher) return;
            var path = watcher.Path;
            var ex = e.GetException();

            if (ex is InternalBufferOverflowException)
            {
                Helpers.DebugLogger.Log($"[FileSystemWatcher] 버퍼 오버플로우(watcher 생존): {path}");
                DebouncedNotify(path, ErrorDebounceMs);
                return;
            }

            Helpers.DebugLogger.Log($"[FileSystemWatcher] 감시 중단됨: {path} - {ex.GetType().Name}: {ex.Message}");

            if (!ShouldAttemptRecreate(path))
            {
                Helpers.DebugLogger.Log($"[FileSystemWatcher] 재생성 포기(반복 실패): {path}");
                return;
            }

            if (RecreateWatcher(path))
                DebouncedNotify(path, ErrorDebounceMs);
        }

        /// <summary>
        /// 짧은 시간에 재생성이 거듭 실패하면 포기한다.
        ///
        /// 알림을 지원하지 않는 파일 서버에서는 watcher 생성 자체는 성공하고 첫 읽기가
        /// 실패한다. 상한이 없으면 생성→실패→재생성의 타이트 루프가 된다. Microsoft가
        /// KB 3092936에서 explorer.exe의 CPU 폭주로 기록한 사고가 정확히 이 형태이고,
        /// "directory change notification을 지원하지 않는 서드파티 파일 서버에서도 같은
        /// 현상이 나타날 수 있다"고 명시돼 있다.
        ///
        /// 포기한 경로는 다음 SetWatchedPaths(컬럼 구성 변경)에서 다시 시도된다.
        /// </summary>
        private bool ShouldAttemptRecreate(string path)
        {
            const int MaxFailures = 3;
            var window = TimeSpan.FromSeconds(60);
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                if (_recreateFailures.TryGetValue(path, out var f) && now - f.FirstAt <= window)
                {
                    if (f.Count >= MaxFailures) return false;
                    _recreateFailures[path] = (f.Count + 1, f.FirstAt);
                }
                else
                {
                    _recreateFailures[path] = (1, now);
                }
            }
            return true;
        }

        /// <summary>
        /// 죽은 watcher를 dispose하고 동일 경로로 새로 생성.
        /// 버퍼 오버플로우 후 watcher는 더 이상 이벤트를 발생시키지 않으므로
        /// 반드시 재생성해야 이후 변경 감지가 유지됨.
        /// </summary>
        private bool RecreateWatcher(string path)
        {
            lock (_lock)
            {
                if (_watchers.TryGetValue(path, out var oldWatcher))
                {
                    oldWatcher.EnableRaisingEvents = false;
                    oldWatcher.Dispose();
                    _watchers.Remove(path);
                }

                if (!Directory.Exists(path)) return false;

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
                    return true;
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[FileSystemWatcher] 재생성 실패: {path} - {ex.Message}");
                    return false;
                }
            }
        }

        private void DebouncedNotify(string folderPath, int delayMs = DebounceMs)
        {
            _debounceTimers.AddOrUpdate(
                folderPath,
                // 신규: 타이머 생성
                _ => new Timer(TimerCallback, folderPath, delayMs, Timeout.Infinite),
                // 기존: 타이머 재설정 (원자적 교체로 경합 조건 방지)
                (_, existing) =>
                {
                    existing.Change(delayMs, Timeout.Infinite);
                    return existing;
                });
        }

        private void TimerCallback(object? state)
        {
            // v1.4.15: ThreadPool Timer callback throw → AppDomain unhandled.
            // PathChanged 구독자 throw가 메인 크래시로 번지지 않도록 봉인.
            try
            {
                if (state is not string folderPath) return;
                if (_debounceTimers.TryRemove(folderPath, out var removed))
                    removed.Dispose();
                PathChanged?.Invoke(folderPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[FileSystemWatcherService.TimerCallback] {ex.Message}");
            }
        }

        private static bool IsLocalPath(string path)
        {
            // UNC 제외. 감시 자체는 SMB에서도 동작한다(실측: \\server\share에서 Created/
            // Changed/Deleted 정상 수신). 켜지 않는 이유는 서버가 CHANGE_NOTIFY를 지원하지
            // 않을 때의 위험 때문이다 — 그 경우 watcher 생성은 성공하고 첫 읽기가 실패해
            // 재생성 루프가 돌 수 있다. Microsoft가 KB 3092936에서 explorer.exe의 CPU
            // 폭주로 기록한 사고가 같은 형태다. Total Commander도 WatchDirs 기본값이
            // 꺼짐이고, Files는 같은 요청(#5869)이 수년째 열려 있다.
            //
            // 켠다면 (1) 여기를 화이트리스트 항목 추가 형태로 바꾸고
            // (2) SetWatchedPaths의 Directory.Exists를 UI 스레드 밖으로 옮겨야 한다
            // (끊긴 공유에서 수십 초 블록). 위 OnWatcherError의 재시도 상한은 그 준비다.
            if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
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
