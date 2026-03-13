using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Dispatching;
using Span.Models;
using Span.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Span.ViewModels
{
    /// <summary>
    /// Git 상태 바 뷰모델. 탐색기 하단에 브랜치명과 변경 파일 요약을 표시.
    /// 기존 미리보기 패널의 Git Tier 2 대시보드를 대체하여 상시 표시.
    /// </summary>
    public partial class GitStatusBarViewModel : ObservableObject, IDisposable
    {
        private readonly GitStatusService? _gitService;
        private readonly LocalizationService? _locService;
        private readonly DispatcherQueue _dispatcherQueue;
        private CancellationTokenSource? _currentCts;
        private bool _disposed;
        private double _lastAvailableWidth;
        private string? _pendingPath;
        private System.Threading.Timer? _debounceTimer;
        private const int DebounceMs = 300;

        // --- Visibility ---

        [ObservableProperty] private bool _isVisible;

        // --- Branch ---

        [ObservableProperty] private string _branch = "";

        // --- Status summary ---

        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private int _totalChanges;
        [ObservableProperty] private int _modifiedCount;
        [ObservableProperty] private int _stagedCount;
        [ObservableProperty] private int _untrackedCount;
        [ObservableProperty] private int _deletedCount;

        // --- Flyout detail ---

        [ObservableProperty] private string _fullStatusText = "";
        [ObservableProperty] private string _recentCommits = "";
        [ObservableProperty] private string _changedFiles = "";

        public GitStatusBarViewModel()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();

            try
            {
                var settings = App.Current.Services.GetService<ISettingsService>();
                if (settings != null && settings.ShowGitIntegration)
                {
                    _gitService = App.Current.Services.GetService<GitStatusService>();
                    if (_gitService != null && !_gitService.IsAvailable)
                        _gitService = null; // git.exe not installed
                }
            }
            catch
            {
                // DI not available (design-time, tests)
            }

            try
            {
                _locService = App.Current.Services.GetService<LocalizationService>();
            }
            catch
            {
                // Optional
            }
        }

        /// <summary>
        /// 경로가 변경될 때 호출. Git 레포 여부를 판단하고 상태 정보를 업데이트.
        /// </summary>
        public Task UpdateForPathAsync(string? currentPath, CancellationToken ct = default)
        {
            if (_disposed) return Task.CompletedTask;

            if (string.IsNullOrEmpty(currentPath) || _gitService == null)
            {
                Clear();
                return Task.CompletedTask;
            }

            // 디바운싱: 300ms 내 연속 호출을 병합하여 git 명령 실행 최소화
            _pendingPath = currentPath;
            _currentCts?.Cancel();

            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(_ =>
            {
                if (_disposed) return;
                _dispatcherQueue.TryEnqueue(() => _ = ExecuteUpdateAsync(ct));
            }, null, DebounceMs, Timeout.Infinite);

            return Task.CompletedTask;
        }

        private async Task ExecuteUpdateAsync(CancellationToken ct)
        {
            if (_disposed) return;

            var path = _pendingPath;
            if (string.IsNullOrEmpty(path) || _gitService == null)
            {
                Clear();
                return;
            }

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _currentCts?.Dispose();
            _currentCts = cts;
            var linkedToken = cts.Token;

            try
            {
                var repoInfo = await _gitService.GetRepoInfoAsync(path, linkedToken);
                if (linkedToken.IsCancellationRequested) return;

                if (repoInfo == null)
                {
                    RunOnUI(() => Clear());
                    return;
                }

                RunOnUI(() => ApplyRepoInfo(repoInfo));
            }
            catch (OperationCanceledException)
            {
                // Normal cancellation during rapid navigation
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[GitStatusBar] Error: {ex.Message}");
                RunOnUI(() => Clear());
            }
        }

        /// <summary>
        /// GitRepoInfo를 각 프로퍼티에 반영 (UI 스레드에서 호출).
        /// </summary>
        private void ApplyRepoInfo(GitRepoInfo info)
        {
            IsVisible = true;
            Branch = info.Branch;

            ModifiedCount = info.ModifiedCount;
            StagedCount = info.StagedCount;
            UntrackedCount = info.UntrackedCount;
            DeletedCount = info.DeletedCount;
            TotalChanges = info.ModifiedCount + info.StagedCount
                         + info.UntrackedCount + info.DeletedCount;

            // Full status for flyout
            FullStatusText = BuildFullStatusText(info);

            // Recent commits for flyout
            RecentCommits = FormatRecentCommits(info.RecentCommits);

            // Changed files for flyout
            ChangedFiles = FormatChangedFiles(info.ChangedFiles);

            // Responsive status text
            UpdateStatusText(_lastAvailableWidth);
        }

        /// <summary>
        /// 사용 가능한 너비에 따라 상태 텍스트를 조정.
        /// UI에서 SizeChanged 이벤트 시 호출.
        /// </summary>
        public void UpdateStatusText(double availableWidth)
        {
            _lastAvailableWidth = availableWidth;

            if (!IsVisible)
            {
                StatusText = "";
                return;
            }

            if (availableWidth >= 500)
            {
                // Detailed: "2 modified . 1 staged . 3 untracked"
                var parts = new List<string>(4);
                if (ModifiedCount > 0) parts.Add($"{ModifiedCount} modified");
                if (StagedCount > 0) parts.Add($"{StagedCount} staged");
                if (UntrackedCount > 0) parts.Add($"{UntrackedCount} untracked");
                if (DeletedCount > 0) parts.Add($"{DeletedCount} deleted");

                StatusText = parts.Count > 0
                    ? string.Join(" \u00b7 ", parts)
                    : "clean";
            }
            else if (availableWidth >= 350)
            {
                // Compact: "6 changes"
                StatusText = TotalChanges > 0
                    ? $"{TotalChanges} changes"
                    : "clean";
            }
            else
            {
                // Minimal: branch name only (shown separately in UI)
                StatusText = "";
            }
        }

        /// <summary>
        /// Flyout용 전체 상태 텍스트 생성.
        /// </summary>
        private string BuildFullStatusText(GitRepoInfo info)
        {
            var parts = new List<string>(4);
            if (info.ModifiedCount > 0) parts.Add($"{info.ModifiedCount} modified");
            if (info.StagedCount > 0) parts.Add($"{info.StagedCount} staged");
            if (info.UntrackedCount > 0) parts.Add($"{info.UntrackedCount} untracked");
            if (info.DeletedCount > 0) parts.Add($"{info.DeletedCount} deleted");

            return parts.Count > 0
                ? string.Join(", ", parts)
                : "No changes (clean)";
        }

        /// <summary>
        /// 최근 커밋 목록을 포맷. 최대 5개.
        /// </summary>
        private static string FormatRecentCommits(List<GitCommitSummary> commits)
        {
            if (commits == null || commits.Count == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var c in commits.Take(5))
            {
                sb.AppendLine($"{c.Hash}  {c.RelativeTime}");
                sb.AppendLine($"  {c.Subject}");
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 변경된 파일 목록을 포맷. 최대 20개, 초과 시 "(+N more)" 표시.
        /// </summary>
        private static string FormatChangedFiles(List<GitChangedFile> files)
        {
            if (files == null || files.Count == 0)
                return "";

            var sb = new StringBuilder();
            foreach (var f in files.Take(20))
            {
                var marker = f.State switch
                {
                    GitFileState.Modified => "M",
                    GitFileState.Added => "A",
                    GitFileState.Deleted => "D",
                    GitFileState.Renamed => "R",
                    GitFileState.Untracked => "?",
                    GitFileState.Conflicted => "!",
                    _ => " ",
                };
                sb.AppendLine($"{marker}  {f.Path}");
            }

            if (files.Count > 20)
                sb.AppendLine($"(+{files.Count - 20} more)");

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// 모든 프로퍼티를 초기화하고 상태 바를 숨김.
        /// </summary>
        public void Clear()
        {
            IsVisible = false;
            Branch = "";
            StatusText = "";
            TotalChanges = 0;
            ModifiedCount = 0;
            StagedCount = 0;
            UntrackedCount = 0;
            DeletedCount = 0;
            FullStatusText = "";
            RecentCommits = "";
            ChangedFiles = "";
        }

        /// <summary>
        /// UI 스레드에서 액션을 실행. 이미 UI 스레드이면 직접 실행.
        /// </summary>
        private void RunOnUI(Action action)
        {
            if (_dispatcherQueue.HasThreadAccess)
                action();
            else
                _dispatcherQueue.TryEnqueue(() => action());
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }
}
