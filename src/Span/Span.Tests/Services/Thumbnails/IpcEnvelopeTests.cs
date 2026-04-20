using System.Text.Json;
using Span.Services.Thumbnails;

namespace Span.Tests.Services.Thumbnails;

/// <summary>
/// IpcEnvelope JSON 직렬화 검증 (Phase 1, P1-2).
/// 메인 ↔ 워커 간 메시지 손실/왜곡 방지 — round-trip 정확성이 핵심.
/// </summary>
[TestClass]
public class IpcEnvelopeTests
{
    [TestMethod]
    public void GenRequest_RoundTrip_PreservesAllFields()
    {
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Gen,
            Id = 42,
            Path = @"C:\test\video.mp4",
            Size = 256,
            Mode = "SingleItem",
            IsCloudOnly = true,
            ApplyExif = true,
            Theme = "Dark",
            Dpi = 144,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);

        Assert.IsNotNull(back);
        Assert.AreEqual(msg.Type, back.Type);
        Assert.AreEqual(msg.Id, back.Id);
        Assert.AreEqual(msg.Path, back.Path);
        Assert.AreEqual(msg.Size, back.Size);
        Assert.AreEqual(msg.Mode, back.Mode);
        Assert.AreEqual(msg.IsCloudOnly, back.IsCloudOnly);
        Assert.AreEqual(msg.ApplyExif, back.ApplyExif);
        Assert.AreEqual(msg.Theme, back.Theme);
        Assert.AreEqual(msg.Dpi, back.Dpi);
    }

    [TestMethod]
    public void OkResponse_RoundTrip_PreservesAllFields()
    {
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Ok,
            Id = 7,
            CachePath = @"C:\Users\test\AppData\Local\Span\ThumbCache\ab\abc123.png",
            Width = 256,
            Height = 144,
            AppliedExif = true,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);

        Assert.AreEqual(msg.Type, back!.Type);
        Assert.AreEqual(msg.Id, back.Id);
        Assert.AreEqual(msg.CachePath, back.CachePath);
        Assert.AreEqual(msg.Width, back.Width);
        Assert.AreEqual(msg.Height, back.Height);
        Assert.AreEqual(msg.AppliedExif, back.AppliedExif);
    }

    [TestMethod]
    public void ErrResponse_RoundTrip_PreservesError()
    {
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Err,
            Id = 99,
            Error = "AccessDenied",
            Retryable = false,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);

        Assert.AreEqual(IpcMessageTypes.Err, back!.Type);
        Assert.AreEqual("AccessDenied", back.Error);
        Assert.IsFalse(back.Retryable);
    }

    [TestMethod]
    public void CancelBatch_RoundTrip_PreservesRange()
    {
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.CancelBatch,
            MinId = 100,
            MaxId = 200,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);

        Assert.AreEqual(IpcMessageTypes.CancelBatch, back!.Type);
        Assert.AreEqual(100, back.MinId);
        Assert.AreEqual(200, back.MaxId);
    }

    [TestMethod]
    public void Pong_RoundTrip_PreservesMetrics()
    {
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Pong,
            MemMB = 48,
            Completed = 1234,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);

        Assert.AreEqual(IpcMessageTypes.Pong, back!.Type);
        Assert.AreEqual(48, back.MemMB);
        Assert.AreEqual(1234, back.Completed);
    }

    [TestMethod]
    public void Serialize_IsSingleLineNoNewlines()
    {
        // JSON Lines 포맷 보장 — 메시지 내부에 '\n' 없어야 줄 단위 파싱 가능
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Gen,
            Id = 1,
            Path = "test",
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        Assert.IsFalse(json.Contains('\n'), "JSON 직렬화 결과에 개행 없어야 함 (Lines 포맷)");
        Assert.IsFalse(json.Contains('\r'));
    }

    [TestMethod]
    public void Serialize_OmitsNullFields()
    {
        // null 필드 생략 → 메시지 크기 최소화 (DefaultIgnoreCondition.WhenWritingNull)
        var msg = new IpcEnvelope
        {
            Type = IpcMessageTypes.Ping,
            Id = 0,
        };

        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        Assert.IsFalse(json.Contains("\"path\""), $"null path는 생략돼야 함: {json}");
        Assert.IsFalse(json.Contains("\"cachePath\""), $"null cachePath는 생략돼야 함: {json}");
        Assert.IsFalse(json.Contains("\"error\""), $"null error는 생략돼야 함: {json}");
    }

    [TestMethod]
    public void Deserialize_UnknownType_DoesNotThrow()
    {
        // 워커 측이 미래 메시지 타입 받을 때 — 알 수 없으면 무시 가능해야 함
        var json = "{\"type\":\"unknown-future\",\"id\":1}";
        var msg = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);
        Assert.IsNotNull(msg);
        Assert.AreEqual("unknown-future", msg.Type);
    }

    [TestMethod]
    public void Deserialize_CaseInsensitivePropertyNames()
    {
        // 외부 입력 견고성 — 대소문자 차이 허용
        var json = "{\"TYPE\":\"gen\",\"ID\":42,\"PATH\":\"x\"}";
        var msg = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);
        Assert.IsNotNull(msg);
        Assert.AreEqual("gen", msg.Type);
        Assert.AreEqual(42, msg.Id);
        Assert.AreEqual("x", msg.Path);
    }

    [TestMethod]
    public void Deserialize_PathWithBackslash_PreservesEscaping()
    {
        // Windows 경로 직렬화/역직렬화 정확성
        var msg = new IpcEnvelope { Type = IpcMessageTypes.Gen, Path = @"C:\Users\테스트\사진.jpg" };
        var json = JsonSerializer.Serialize(msg, IpcJson.Options);
        var back = JsonSerializer.Deserialize<IpcEnvelope>(json, IpcJson.Options);
        Assert.AreEqual(@"C:\Users\테스트\사진.jpg", back!.Path);
    }
}
