using Span.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// git.exe CLI 기반 Git 상태 조회 서비스.
    /// 미리보기 패널(Tier 1/2)과 Details 뷰(Tier 3)에 Git 정보를 제공.
    /// </summary>
    public class GitStatusService : IDisposable
    {
        private string? _gitExePath;
        private string? _gitVersion;
        private bool _detected;
        private readonly object _detectLock = new();

        // 레포 루트 캐시: 경로 → 레포 루트 (null = 레포 아님)
        private readonly ConcurrentDictionary<string, string?> _repoRootCache = new(StringComparer.OrdinalIgnoreCase);

        // Tier 3 캐시: 레포 루트 → (상태 딕셔너리, 타임스탬프)
        private readonly ConcurrentDictionary<string, (Dictionary<string, GitFileState> States, DateTime Updated)> _statusCache
            = new(StringComparer.OrdinalIgnoreCase);

        private const int CommandTimeoutMs = 8000;
        private const int DetectTimeoutMs = 3000;
        private const int MaxOutputBytes = 128 * 1024; // 128KB stdout 제한
        private const long MaxIndexSize = 5 * 1024 * 1024; // 5MB .git/index 제한
        private const int MaxRepoSearchDepth = 15;
        private const int MaxRecentCommits = 5;

        /// <summary>
        /// git.exe가 감지되었는지 여부.
        /// </summary>
        public bool IsAvailable
        {
            get
            {
                EnsureDetected();
                return _gitExePath != null;
            }
        }

        /// <summary>
        /// git 버전 문자열 (표시용).
        /// </summary>
        public string? GitVersion
        {
            get
            {
                EnsureDetected();
                return _gitVersion;
            }
        }

        /// <summary>
        /// git.exe 경로 (디버그용).
        /// </summary>
        public string? GitExePath
        {
            get
            {
                EnsureDetected();
                return _gitExePath;
            }
        }

        // ── 감지 ──

        private void EnsureDetected()
        {
            if (_detected) return;
            lock (_detectLock)
            {
                if (_detected) return;
                _gitExePath = DetectGitExe();
                if (_gitExePath != null)
                {
                    _gitVersion = GetGitVersion(_gitExePath);
                    Helpers.DebugLogger.Log($"[GitStatus] Detected: {_gitExePath} ({_gitVersion})");
                }
                else
                {
                    Helpers.DebugLogger.Log("[GitStatus] git.exe not found");
                }
                _detected = true;
            }
        }

        private static string? DetectGitExe()
        {
            try
            {
                // 1. PATH에서 검색 (where git)
                var result = RunProcessSync("where", "git", DetectTimeoutMs);
                if (result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut))
                {
                    var firstLine = result.StdOut.Split('\n')[0].Trim();
                    if (File.Exists(firstLine))
                        return firstLine;
                }
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[GitStatus] where git detection failed: {ex.Message}"); }

            // 2. 기본 설치 경로 폴백
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "cmd", "git.exe"),
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private static string? GetGitVersion(string gitExe)
        {
            try
            {
                var result = RunProcessSync(gitExe, "--version", DetectTimeoutMs);
                if (result.ExitCode == 0)
                    return result.StdOut.Trim().Replace("git version ", "");
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[GitStatus] git version check failed: {ex.Message}"); }
            return null;
        }

        // ── 레포 루트 탐색 ──

        /// <summary>
        /// 주어진 경로에서 역방향으로 .git 디렉토리를 탐색하여 레포 루트 반환.
        /// 결과는 캐시됨. null = 레포가 아님.
        /// </summary>
        public string? FindRepoRoot(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            // 네트워크/이동식 드라이브 스킵
            if (IsNetworkOrRemovable(path)) return null;

            // 파일이면 디렉토리로
            var dir = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return null;

            // 캐시 확인 (정확히 이 디렉토리를 조회한 적 있는지)
            if (_repoRootCache.TryGetValue(dir, out var cached))
                return cached;

            // 역방향 탐색
            string? current = dir;
            for (int i = 0; i < MaxRepoSearchDepth && current != null; i++)
            {
                var gitDir = Path.Combine(current, ".git");
                if (Directory.Exists(gitDir) || File.Exists(gitDir)) // .git 파일도 가능 (worktree)
                {
                    _repoRootCache[dir] = current;
                    return current;
                }

                var parent = Path.GetDirectoryName(current);
                if (parent == null || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                    break;
                current = parent;
            }

            _repoRootCache[dir] = null;
            return null;
        }

        /// <summary>
        /// 네트워크 경로 또는 이동식 드라이브 여부 확인.
        /// </summary>
        public static bool IsNetworkOrRemovable(string path)
        {
            try
            {
                if (path.StartsWith(@"\\", StringComparison.Ordinal))
                    return true;

                var root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root)) return false;

                var driveInfo = new DriveInfo(root);
                return driveInfo.DriveType == DriveType.Network
                    || driveInfo.DriveType == DriveType.Removable
                    || driveInfo.DriveType == DriveType.CDRom;
            }
            catch
            {
                return false;
            }
        }

        // ── Tier 1: 파일 마지막 커밋 ──

        /// <summary>
        /// 파일의 마지막 커밋 정보를 반환.
        /// git log -1 --format="%cr|%s|%an" -- {relativePath}
        /// </summary>
        public async Task<GitLastCommit?> GetLastCommitAsync(string filePath, CancellationToken ct)
        {
            if (!IsAvailable) return null;

            var repoRoot = FindRepoRoot(filePath);
            if (repoRoot == null) return null;

            // 상대 경로 계산
            var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');

            var result = await RunGitAsync(
                repoRoot,
                $"log -1 --format=\"%cr|%s|%an\" -- \"{relativePath}\"",
                ct);

            if (result == null || result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.StdOut))
                return null;

            var parts = result.StdOut.Trim().Split('|', 3);
            if (parts.Length < 3) return null;

            return new GitLastCommit(parts[0], parts[1], parts[2]);
        }

        // ── Tier 2: 레포 대시보드 ──

        /// <summary>
        /// Git 레포 요약 정보를 반환 (브랜치, 상태, 최근 커밋, 변경 파일).
        /// 두 git 명령을 병렬 실행.
        /// </summary>
        public async Task<GitRepoInfo?> GetRepoInfoAsync(string folderPath, CancellationToken ct)
        {
            if (!IsAvailable) return null;

            var repoRoot = FindRepoRoot(folderPath);
            if (repoRoot == null) return null;

            // 병렬 실행: status + log
            var statusTask = RunGitAsync(repoRoot, "status -sb", ct);
            var logTask = RunGitAsync(repoRoot, $"log -{MaxRecentCommits} --format=\"%h|%cr|%s\"", ct);

            await Task.WhenAll(statusTask, logTask);

            var statusResult = statusTask.Result;
            var logResult = logTask.Result;

            if (statusResult == null) return null;

            return ParseRepoInfo(statusResult, logResult, repoRoot);
        }

        private static GitRepoInfo ParseRepoInfo(ProcessResult statusResult, ProcessResult? logResult, string repoRoot)
        {
            var lines = statusResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            var branch = "";
            int modified = 0, untracked = 0, staged = 0, deleted = 0;
            var changedFiles = new List<GitChangedFile>();

            foreach (var line in lines)
            {
                if (line.StartsWith("## "))
                {
                    // "## main...origin/main" or "## main"
                    var branchPart = line[3..];
                    var dotIdx = branchPart.IndexOf("...", StringComparison.Ordinal);
                    branch = dotIdx >= 0 ? branchPart[..dotIdx] : branchPart.Trim();
                    continue;
                }

                if (line.Length < 3) continue;
                char x = line[0], y = line[1];
                var path = line[3..].Trim();

                var state = ParseXY(x, y);
                changedFiles.Add(new GitChangedFile(path, state));

                switch (state)
                {
                    case GitFileState.Modified: modified++; break;
                    case GitFileState.Untracked: untracked++; break;
                    case GitFileState.Added: staged++; break;
                    case GitFileState.Deleted: deleted++; break;
                }
            }

            // 커밋 파싱
            var commits = new List<GitCommitSummary>();
            if (logResult?.ExitCode == 0 && !string.IsNullOrWhiteSpace(logResult.StdOut))
            {
                foreach (var line in logResult.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = line.Split('|', 3);
                    if (parts.Length >= 3)
                        commits.Add(new GitCommitSummary(parts[0], parts[1], parts[2]));
                }
            }

            return new GitRepoInfo
            {
                Branch = branch,
                ModifiedCount = modified,
                UntrackedCount = untracked,
                StagedCount = staged,
                DeletedCount = deleted,
                RecentCommits = commits,
                ChangedFiles = changedFiles,
            };
        }

        // ── Tier 3: 폴더 파일 상태 (Details 뷰) ──

        /// <summary>
        /// 레포 전체의 파일 상태를 딕셔너리로 반환 (캐시 활용).
        /// git status --porcelain=v1 -z        /// </summary>
        public async Task<Dictionary<string, GitFileState>?> GetFolderStatesAsync(
            string folderPath, CancellationToken ct)
        {
            if (!IsAvailable) return null;

            var repoRoot = FindRepoRoot(folderPath);
            if (repoRoot == null) return null;

            // 대형 레포 방어: .git/index 크기 체크
            var indexPath = Path.Combine(repoRoot, ".git", "index");
            try
            {
                if (File.Exists(indexPath) && new FileInfo(indexPath).Length > MaxIndexSize)
                {
                    Helpers.DebugLogger.Log($"[GitStatus] Skipping large repo: {repoRoot} (index > {MaxIndexSize / 1024 / 1024}MB)");
                    return null;
                }
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[GitStatus] Index size check failed: {ex.Message}"); }

            // 캐시 확인 (30초 이내)
            if (_statusCache.TryGetValue(repoRoot, out var cached)
                && (DateTime.UtcNow - cached.Updated).TotalSeconds < 30)
            {
                return cached.States;
            }

            var result = await RunGitAsync(repoRoot, "status --porcelain=v1 -z", ct);
            if (result == null || result.ExitCode != 0) return null;

            var states = ParsePorcelainOutput(result.StdOut, repoRoot);
            _statusCache[repoRoot] = (states, DateTime.UtcNow);
            return states;
        }

        /// <summary>
        /// 캐시에서 동기적으로 파일 상태 조회. 캐시 미스 시 null.
        /// </summary>
        public GitFileState? GetCachedState(string filePath)
        {
            var repoRoot = FindRepoRoot(filePath);
            if (repoRoot == null) return null;

            if (!_statusCache.TryGetValue(repoRoot, out var cached))
                return null;

            var relativePath = Path.GetRelativePath(repoRoot, filePath).Replace('\\', '/');
            if (cached.States.TryGetValue(relativePath, out var state))
                return state;

            // 캐시에 없으면 Clean (git status는 변경된 파일만 출력)
            return GitFileState.Clean;
        }

        /// <summary>
        /// 특정 레포의 캐시를 무효화.
        /// </summary>
        public void InvalidateRepo(string repoRoot)
        {
            _statusCache.TryRemove(repoRoot, out _);
        }

        // ── 파싱 ──

        private static Dictionary<string, GitFileState> ParsePorcelainOutput(string output, string repoRoot)
        {
            var result = new Dictionary<string, GitFileState>(StringComparer.OrdinalIgnoreCase);

            // -z 플래그: NUL 구분자
            var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                if (entry.Length < 3) continue;

                char x = entry[0], y = entry[1];
                // entry[2] is space
                var path = entry[3..];
                var state = ParseXY(x, y);
                result[path] = state;

                // Rename/Copy: 다음 항목은 원래 경로 → 건너뜀
                if (x == 'R' || x == 'C')
                    i++;
            }

            return result;
        }

        private static GitFileState ParseXY(char x, char y)
        {
            // 충돌 상태 우선
            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
                return GitFileState.Conflicted;

            // 워킹 트리 상태 우선 (사용자가 보는 것)
            if (y == 'M') return GitFileState.Modified;
            if (y == 'D') return GitFileState.Deleted;

            // 스테이징 상태
            if (x == 'M') return GitFileState.Modified;
            if (x == 'A') return GitFileState.Added;
            if (x == 'D') return GitFileState.Deleted;
            if (x == 'R') return GitFileState.Renamed;

            // Untracked
            if (x == '?' && y == '?') return GitFileState.Untracked;

            // Ignored
            if (x == '!' && y == '!') return GitFileState.Ignored;

            return GitFileState.Clean;
        }

        // ── 프로세스 실행 ──

        private async Task<ProcessResult?> RunGitAsync(string repoRoot, string arguments, CancellationToken ct)
        {
            if (_gitExePath == null) return null;

            try
            {
                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = _gitExePath,
                    Arguments = $"--no-optional-locks -C \"{repoRoot}\" {arguments}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                process.Start();

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(CommandTimeoutMs);

                var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
                await process.WaitForExitAsync(cts.Token);

                // stdout 크기 제한
                if (stdout.Length > MaxOutputBytes)
                    stdout = stdout[..MaxOutputBytes];

                return new ProcessResult(process.ExitCode, stdout, "");
            }
            catch (OperationCanceledException)
            {
                throw; // 상위에서 처리
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[GitStatus] Error running git: {ex.Message}");
                return null;
            }
        }

        private static ProcessResult RunProcessSync(string fileName, string arguments, int timeoutMs)
        {
            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            process.Start();

            // Read stdout and stderr concurrently to avoid deadlock
            // (if either buffer fills, the process blocks and ReadToEnd never returns)
            var stderrTask = Task.Run(() => process.StandardError.ReadToEnd());
            var stdout = process.StandardOutput.ReadToEnd();
            process.WaitForExit(timeoutMs);

            if (!process.HasExited)
            {
                try { process.Kill(true); } catch { }
                return new ProcessResult(-1, "", "timeout");
            }

            var stderr = stderrTask.GetAwaiter().GetResult();
            return new ProcessResult(process.ExitCode, stdout, stderr);
        }

        private record ProcessResult(int ExitCode, string StdOut, string StdErr);

        // ── Dispose ──

        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _repoRootCache.Clear();
            _statusCache.Clear();
        }
    }
}
