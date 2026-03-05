using Span.Models;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class ActionLogServiceTests
{
    private string _tempDir = null!;
    private string _logFilePath = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"SpanTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _logFilePath = Path.Combine(_tempDir, "action_log.json");
    }

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    /// <summary>
    /// ActionLogService는 생성자에서 경로가 고정되므로,
    /// 리플렉션으로 _logFilePath를 테스트용 경로로 교체한다.
    /// </summary>
    private ActionLogService CreateService()
    {
        var svc = new ActionLogService();
        // 리플렉션으로 _logFilePath 교체
        var field = typeof(ActionLogService).GetField("_logFilePath",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(svc, _logFilePath);
        // _loaded를 false로 재설정
        var loadedField = typeof(ActionLogService).GetField("_loaded",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        loadedField!.SetValue(svc, false);
        return svc;
    }

    // ── LogOperation ──

    [TestMethod]
    public void LogOperation_항목추가_타임스탬프자동설정()
    {
        var svc = CreateService();
        var before = DateTime.Now;

        svc.LogOperation(new ActionLogEntry
        {
            OperationType = "Copy",
            Description = "Copy test",
            Success = true
        });

        var entries = svc.GetEntries(10);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual("Copy", entries[0].OperationType);
        Assert.IsTrue(entries[0].Timestamp >= before, "타임스탬프가 호출 이전보다 크거나 같아야 함");
    }

    [TestMethod]
    public void LogOperation_여러항목추가_역순조회()
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry { OperationType = "Copy", Description = "first", Success = true });
        svc.LogOperation(new ActionLogEntry { OperationType = "Move", Description = "second", Success = true });
        svc.LogOperation(new ActionLogEntry { OperationType = "Delete", Description = "third", Success = false });

        var entries = svc.GetEntries(10);

        Assert.AreEqual(3, entries.Count);
        // 역순: 가장 최근이 첫 번째
        Assert.AreEqual("Delete", entries[0].OperationType);
        Assert.AreEqual("Move", entries[1].OperationType);
        Assert.AreEqual("Copy", entries[2].OperationType);
    }

    // ── GetEntries ──

    [TestMethod]
    public void GetEntries_빈로그_빈리스트반환()
    {
        var svc = CreateService();
        var entries = svc.GetEntries(50);
        Assert.IsNotNull(entries);
        Assert.AreEqual(0, entries.Count);
    }

    [TestMethod]
    public void GetEntries_Count지정_최근N개만반환()
    {
        var svc = CreateService();
        for (int i = 0; i < 10; i++)
        {
            svc.LogOperation(new ActionLogEntry
            {
                OperationType = "Copy",
                Description = $"op{i}",
                Success = true
            });
        }

        var entries = svc.GetEntries(3);
        Assert.AreEqual(3, entries.Count);
        // 역순: 가장 최근 3개
        Assert.AreEqual("op9", entries[0].Description);
        Assert.AreEqual("op8", entries[1].Description);
        Assert.AreEqual("op7", entries[2].Description);
    }

    [TestMethod]
    public void GetEntries_Count가전체보다큰경우_전체반환()
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry { OperationType = "Copy", Success = true });
        svc.LogOperation(new ActionLogEntry { OperationType = "Move", Success = true });

        var entries = svc.GetEntries(100);
        Assert.AreEqual(2, entries.Count);
    }

    // ── Clear ──

    [TestMethod]
    public void Clear_모든로그삭제()
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry { OperationType = "Copy", Success = true });
        svc.LogOperation(new ActionLogEntry { OperationType = "Move", Success = true });
        Assert.AreEqual(2, svc.GetEntries(10).Count);

        svc.Clear();

        Assert.AreEqual(0, svc.GetEntries(10).Count);
    }

    // ── FIFO Trim ──

    [TestMethod]
    public void LogOperation_최대1000개초과시_FIFO트림()
    {
        var svc = CreateService();

        // MaxEntries = 1000, 1005개 추가
        for (int i = 0; i < 1005; i++)
        {
            svc.LogOperation(new ActionLogEntry
            {
                OperationType = "Copy",
                Description = $"item{i}",
                Success = true
            });
        }

        var entries = svc.GetEntries(2000);
        Assert.AreEqual(1000, entries.Count);
        // 가장 오래된 5개(item0~item4)는 제거되어야 함
        // 역순이므로 마지막이 가장 오래된 것
        Assert.AreEqual("item1004", entries[0].Description);
        Assert.AreEqual("item5", entries[999].Description);
    }

    // ── 다양한 OperationType ──

    [TestMethod]
    [DataRow("Copy")]
    [DataRow("Move")]
    [DataRow("Delete")]
    [DataRow("Rename")]
    [DataRow("Undo")]
    [DataRow("Redo")]
    [DataRow("Compress")]
    [DataRow("Extract")]
    public void LogOperation_다양한OperationType_저장조회(string opType)
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry { OperationType = opType, Success = true });

        var entries = svc.GetEntries(1);
        Assert.AreEqual(1, entries.Count);
        Assert.AreEqual(opType, entries[0].OperationType);
    }

    // ── 에러 항목 ──

    [TestMethod]
    public void LogOperation_실패항목_에러메시지보존()
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry
        {
            OperationType = "Delete",
            Success = false,
            ErrorMessage = "Access denied",
            Description = "Delete locked.txt"
        });

        var entries = svc.GetEntries(1);
        Assert.IsFalse(entries[0].Success);
        Assert.AreEqual("Access denied", entries[0].ErrorMessage);
    }

    // ── SourcePaths / DestinationPath ──

    [TestMethod]
    public void LogOperation_소스경로와대상경로_보존()
    {
        var svc = CreateService();
        svc.LogOperation(new ActionLogEntry
        {
            OperationType = "Copy",
            SourcePaths = new List<string> { @"C:\src\a.txt", @"C:\src\b.txt" },
            DestinationPath = @"D:\dest",
            Success = true,
            ItemCount = 2
        });

        var entries = svc.GetEntries(1);
        Assert.AreEqual(2, entries[0].SourcePaths.Count);
        Assert.AreEqual(@"C:\src\a.txt", entries[0].SourcePaths[0]);
        Assert.AreEqual(@"D:\dest", entries[0].DestinationPath);
        Assert.AreEqual(2, entries[0].ItemCount);
    }
}
