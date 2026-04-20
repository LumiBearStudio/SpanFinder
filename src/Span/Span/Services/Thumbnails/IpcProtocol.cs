using System.Text.Json.Serialization;

namespace Span.Services.Thumbnails;

/// <summary>
/// 메인 ↔ 워커 IPC 메시지 정의 (JSON Lines, UTF-8, '\n' 구분).
/// 워커(Span.Thumbs) 측은 동일한 정의를 별도 파일로 미러 — 어셈블리 share 안 함 (독립성 우선).
///
/// 메시지 흐름:
///   1. Main → Worker: GenRequest    (썸네일 생성 요청)
///   2. Worker → Main: GenResponse   (성공: cachePath / 실패: error)
///   3. Main → Worker: CancelRequest (단일/일괄 취소)
///   4. 양방향: Ping/Pong            (헬스체크)
/// </summary>
internal static class IpcMessageTypes
{
    public const string Gen = "gen";
    public const string Ok = "ok";
    public const string Err = "err";
    public const string Cancel = "cancel";
    public const string CancelBatch = "cancel-batch";
    public const string Ping = "ping";
    public const string Pong = "pong";
    public const string Shutdown = "shutdown";
}

/// <summary>
/// 모든 IPC 메시지의 공통 베이스 — discriminator로 type 사용.
/// System.Text.Json은 polymorphism을 명시적으로 처리해야 하므로 envelope 패턴.
/// </summary>
internal sealed class IpcEnvelope
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>요청 ID (gen/cancel/ok/err에서 사용. ping/pong은 0).</summary>
    [JsonPropertyName("id")]
    public long Id { get; set; }

    // ── Generate Request 필드 (type=gen) ──
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; }

    /// <summary>"SingleItem" / "ListView" / "DocumentsView" 등.</summary>
    [JsonPropertyName("mode")]
    public string? Mode { get; set; }

    /// <summary>OneDrive online-only 파일 — 다운로드 트리거 방지 (P2-4c).</summary>
    [JsonPropertyName("isCloudOnly")]
    public bool IsCloudOnly { get; set; }

    /// <summary>워커가 EXIF 회전을 PNG에 미리 굽기 (P2-4b). Phase 1은 항상 true 권장.</summary>
    [JsonPropertyName("applyExif")]
    public bool ApplyExif { get; set; } = true;

    /// <summary>Light/Dark — 캐시 키에 포함 (P2-4a). Phase 1에서는 메타로만 전달.</summary>
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    /// <summary>96/120/144/192 — 캐시 키에 포함 (P2-4a).</summary>
    [JsonPropertyName("dpi")]
    public uint Dpi { get; set; }

    // ── Generate Response 필드 (type=ok) ──
    [JsonPropertyName("cachePath")]
    public string? CachePath { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("appliedExif")]
    public bool AppliedExif { get; set; }

    // ── Error Response 필드 (type=err) ──
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("retryable")]
    public bool Retryable { get; set; }

    // ── Cancel Batch 필드 (type=cancel-batch) ──
    [JsonPropertyName("minId")]
    public long MinId { get; set; }

    [JsonPropertyName("maxId")]
    public long MaxId { get; set; }

    // ── Pong 필드 (type=pong) ──
    [JsonPropertyName("memMB")]
    public long MemMB { get; set; }

    [JsonPropertyName("completed")]
    public long Completed { get; set; }
}

/// <summary>JSON 직렬화 옵션 — 양쪽에서 동일 사용.</summary>
internal static class IpcJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        // JSON Lines 포맷 — pretty print 안 함
        WriteIndented = false,
        // null 필드 생략 → 메시지 크기 최소화
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}
