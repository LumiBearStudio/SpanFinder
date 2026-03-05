using Span.Models;

namespace Span.Views;

/// <summary>
/// LogEntryDisplay의 순수 로직을 분리한 헬퍼.
/// WinUI 의존성 없이 단위 테스트 가능한 정적 메서드 제공.
/// </summary>
public static class LogEntryDisplayHelper
{
    /// <summary>에러 필터 상수.</summary>
    public const string ErrorFilter = "__Error__";

    /// <summary>에러 항목 수 (뱃지 표시용).</summary>
    public static int CountErrors(List<ActionLogEntry> allEntries)
        => allEntries.Count(e => !e.Success);

    /// <summary>
    /// 작업 유형별 글리프 코드 반환.
    /// </summary>
    public static string GetOperationGlyph(string operationType) => operationType switch
    {
        "Copy" => "\uE8C8",
        "Move" => "\uE8DE",
        "Delete" => "\uE74D",
        "Rename" => "\uE8AC",
        "NewFolder" => "\uE8B7",
        "Undo" => "\uE7A7",
        "Redo" => "\uE7A6",
        "Compress" => "\uE8C5",
        "Extract" => "\uE8B7",
        _ => "\uE946"
    };

    /// <summary>
    /// 타임스탬프를 포맷된 문자열로 변환.
    /// 오늘→HH:mm:ss, 어제→"어제 HH:mm", 이전→MM/dd HH:mm.
    /// </summary>
    public static string FormatTime(DateTime timestamp, DateTime now)
    {
        if (timestamp.Date == now.Date)
            return timestamp.ToString("HH:mm:ss");
        if (timestamp.Date == now.Date.AddDays(-1))
            return $"어제 {timestamp:HH:mm}";
        return timestamp.ToString("MM/dd HH:mm");
    }

    /// <summary>
    /// 소스 경로 목록에서 파일 상세 리스트를 생성.
    /// 단일 파일→전체 경로, 다중→파일명만, maxItems 초과→"... 외 N개" 추가.
    /// </summary>
    public static List<string> BuildFileDetails(List<string>? sourcePaths, int maxItems = 20, string? moreFormat = null)
    {
        var details = new List<string>();
        if (sourcePaths == null || sourcePaths.Count == 0)
            return details;

        var paths = sourcePaths.Take(maxItems).ToList();
        bool showFullPath = sourcePaths.Count == 1;
        foreach (var path in paths)
        {
            details.Add(showFullPath ? path : GetFileName(path));
        }
        if (sourcePaths.Count > maxItems)
        {
            var fmt = moreFormat ?? "... and {0} more";
            details.Add(string.Format(fmt, sourcePaths.Count - maxItems));
        }
        return details;
    }

    /// <summary>
    /// 폴더 열기 대상 경로 결정.
    /// Copy/Move → destinationPath, Delete/Rename → 소스 부모 경로.
    /// </summary>
    public static string? DetermineOpenFolderPath(
        List<string>? sourcePaths,
        string? destinationPath,
        Func<string, bool>? isRemotePath = null)
    {
        isRemotePath ??= path => !string.IsNullOrEmpty(path) && path.Contains("://") && !path.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

        string? result = null;

        // 소스 경로의 부모 디렉토리
        if (sourcePaths != null && sourcePaths.Count > 0)
        {
            var firstPath = sourcePaths[0];
            try
            {
                if (!string.IsNullOrEmpty(firstPath) && !isRemotePath(firstPath))
                {
                    var parent = Path.GetDirectoryName(firstPath);
                    if (!string.IsNullOrEmpty(parent))
                        result = parent;
                }
            }
            catch { }
        }

        // Copy/Move의 경우 대상 경로 우선
        if (!string.IsNullOrEmpty(destinationPath) && !isRemotePath(destinationPath))
        {
            result = destinationPath;
        }

        return result;
    }

    /// <summary>
    /// 경로에서 파일명 추출 (FTP/SFTP URI도 지원).
    /// </summary>
    public static string GetFileName(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        try
        {
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) &&
                (uri.Scheme == "ftp" || uri.Scheme == "sftp"))
            {
                var segments = uri.AbsolutePath.TrimEnd('/').Split('/');
                return segments.Length > 0 ? Uri.UnescapeDataString(segments[^1]) : path;
            }
            return Path.GetFileName(path);
        }
        catch
        {
            return path;
        }
    }
}
