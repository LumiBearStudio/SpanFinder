using System.Text.Json.Serialization;

namespace Span.Thumbs;

/// <summary>
/// 메인 ↔ 워커 IPC 메시지 (Span/Services/Thumbnails/IpcProtocol.cs와 미러).
/// 어셈블리 share 안 함 — 워커 독립성 우선.
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

internal sealed class IpcEnvelope
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("id")] public long Id { get; set; }

    [JsonPropertyName("path")] public string? Path { get; set; }
    [JsonPropertyName("size")] public int Size { get; set; }
    [JsonPropertyName("mode")] public string? Mode { get; set; }
    [JsonPropertyName("isCloudOnly")] public bool IsCloudOnly { get; set; }
    [JsonPropertyName("applyExif")] public bool ApplyExif { get; set; } = true;
    [JsonPropertyName("theme")] public string? Theme { get; set; }
    [JsonPropertyName("dpi")] public uint Dpi { get; set; }

    [JsonPropertyName("cachePath")] public string? CachePath { get; set; }
    [JsonPropertyName("width")] public int Width { get; set; }
    [JsonPropertyName("height")] public int Height { get; set; }
    [JsonPropertyName("appliedExif")] public bool AppliedExif { get; set; }

    [JsonPropertyName("error")] public string? Error { get; set; }
    [JsonPropertyName("retryable")] public bool Retryable { get; set; }

    [JsonPropertyName("minId")] public long MinId { get; set; }
    [JsonPropertyName("maxId")] public long MaxId { get; set; }

    [JsonPropertyName("memMB")] public long MemMB { get; set; }
    [JsonPropertyName("completed")] public long Completed { get; set; }
}

internal static class IpcJson
{
    public static readonly System.Text.Json.JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };
}
