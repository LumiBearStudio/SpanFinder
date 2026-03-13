using System.Collections.Generic;

namespace Span.Models
{
    /// <summary>
    /// Git 레포지토리 요약 정보 (미리보기 패널 대시보드용).
    /// </summary>
    public record GitRepoInfo
    {
        public string Branch { get; init; } = "";

        // 레포 전체 수치
        public int ModifiedCount { get; init; }
        public int UntrackedCount { get; init; }
        public int StagedCount { get; init; }
        public int DeletedCount { get; init; }
        public List<GitCommitSummary> RecentCommits { get; init; } = new();
        public List<GitChangedFile> ChangedFiles { get; init; } = new();

        // 현재 폴더 기준 수치
        public string CurrentFolder { get; init; } = "";
        public int FolderModifiedCount { get; init; }
        public int FolderUntrackedCount { get; init; }
        public int FolderStagedCount { get; init; }
        public int FolderDeletedCount { get; init; }
        public List<GitCommitSummary> FolderRecentCommits { get; init; } = new();
        public List<GitChangedFile> FolderChangedFiles { get; init; } = new();
    }

    /// <summary>
    /// 커밋 요약 (해시 7자리 + 상대시간 + 제목).
    /// </summary>
    public record GitCommitSummary(string Hash, string RelativeTime, string Subject);

    /// <summary>
    /// 변경된 파일 (상태 + 경로).
    /// </summary>
    public record GitChangedFile(string Path, GitFileState State);

    /// <summary>
    /// 파일의 마지막 커밋 정보.
    /// </summary>
    public record GitLastCommit(string RelativeTime, string Subject, string Author);
}
