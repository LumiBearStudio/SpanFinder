namespace Span.Models;

/// <summary>
/// 파일 조작 로그 항목. ActionLogService가 Copy/Move/Delete/Rename 등의 작업 기록을 관리한다.
/// LogFlyoutContent에서 사용자에게 최근 작업 이력을 표시하는 데 사용된다.
/// </summary>
public class ActionLogEntry
{
    /// <summary>작업 수행 시각.</summary>
    public DateTime Timestamp { get; set; }

    /// <summary>작업 유형 문자열 ("Copy", "Move", "Delete", "Rename", "NewFolder" 등).</summary>
    public string OperationType { get; set; } = string.Empty;

    /// <summary>원본 파일/폴더 경로 목록.</summary>
    public List<string> SourcePaths { get; set; } = new();

    /// <summary>대상 경로 (복사/이동의 경우). 삭제 시 null.</summary>
    public string? DestinationPath { get; set; }

    /// <summary>작업 성공 여부.</summary>
    public bool Success { get; set; }

    /// <summary>실패 시 오류 메시지.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>사용자에게 표시할 작업 설명 문자열.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>처리된 항목 수.</summary>
    public int ItemCount { get; set; }
}
