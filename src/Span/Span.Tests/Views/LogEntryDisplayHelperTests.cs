using Span.Views;

namespace Span.Tests.Views;

[TestClass]
public class LogEntryDisplayHelperTests
{
    // ── GetOperationGlyph ──

    [TestMethod]
    [DataRow("Copy", "\uE8C8")]
    [DataRow("Move", "\uE8DE")]
    [DataRow("Delete", "\uE74D")]
    [DataRow("Rename", "\uE8AC")]
    [DataRow("NewFolder", "\uE8B7")]
    [DataRow("Undo", "\uE7A7")]
    [DataRow("Redo", "\uE7A6")]
    [DataRow("Compress", "\uE8C5")]
    [DataRow("Extract", "\uE8B7")]
    public void GetOperationGlyph_알려진작업유형_올바른글리프반환(string opType, string expectedGlyph)
    {
        Assert.AreEqual(expectedGlyph, LogEntryDisplayHelper.GetOperationGlyph(opType));
    }

    [TestMethod]
    [DataRow("Unknown")]
    [DataRow("")]
    [DataRow("SomeNewType")]
    public void GetOperationGlyph_알수없는작업유형_기본글리프반환(string opType)
    {
        Assert.AreEqual("\uE946", LogEntryDisplayHelper.GetOperationGlyph(opType));
    }

    [TestMethod]
    public void GetOperationGlyph_NewFolder와Extract_같은글리프()
    {
        // 두 작업 유형 모두 \uE8B7 사용
        Assert.AreEqual(
            LogEntryDisplayHelper.GetOperationGlyph("NewFolder"),
            LogEntryDisplayHelper.GetOperationGlyph("Extract"));
    }

    // ── FormatTime ──

    [TestMethod]
    public void FormatTime_오늘_HHmmss형식()
    {
        var now = new DateTime(2026, 3, 5, 14, 30, 0);
        var timestamp = new DateTime(2026, 3, 5, 10, 15, 30);

        var result = LogEntryDisplayHelper.FormatTime(timestamp, now);

        Assert.AreEqual("10:15:30", result);
    }

    [TestMethod]
    public void FormatTime_어제_어제HHmm형식()
    {
        var now = new DateTime(2026, 3, 5, 14, 30, 0);
        var timestamp = new DateTime(2026, 3, 4, 18, 45, 0);

        var result = LogEntryDisplayHelper.FormatTime(timestamp, now);

        Assert.AreEqual("어제 18:45", result);
    }

    [TestMethod]
    public void FormatTime_이전날짜_MMddHHmm형식()
    {
        var now = new DateTime(2026, 3, 5, 14, 30, 0);
        var timestamp = new DateTime(2026, 2, 28, 9, 0, 0);

        var result = LogEntryDisplayHelper.FormatTime(timestamp, now);

        // "MM/dd HH:mm" 형식이지만 날짜 구분자는 로케일에 따라 다를 수 있음
        var expected = timestamp.ToString("MM/dd HH:mm");
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void FormatTime_자정직전과자정직후_날짜구분정확()
    {
        var now = new DateTime(2026, 3, 5, 0, 1, 0);
        var justBeforeMidnight = new DateTime(2026, 3, 4, 23, 59, 59);
        var justAfterMidnight = new DateTime(2026, 3, 5, 0, 0, 1);

        Assert.AreEqual("어제 23:59", LogEntryDisplayHelper.FormatTime(justBeforeMidnight, now));
        Assert.AreEqual("00:00:01", LogEntryDisplayHelper.FormatTime(justAfterMidnight, now));
    }

    // ── BuildFileDetails ──

    [TestMethod]
    public void BuildFileDetails_null소스_빈리스트()
    {
        var result = LogEntryDisplayHelper.BuildFileDetails(null);
        Assert.IsNotNull(result);
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void BuildFileDetails_빈리스트_빈리스트()
    {
        var result = LogEntryDisplayHelper.BuildFileDetails(new List<string>());
        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public void BuildFileDetails_단일파일_전체경로표시()
    {
        var paths = new List<string> { @"C:\Users\test\file.txt" };
        var result = LogEntryDisplayHelper.BuildFileDetails(paths);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(@"C:\Users\test\file.txt", result[0]);
    }

    [TestMethod]
    public void BuildFileDetails_다중파일_파일명만표시()
    {
        var paths = new List<string>
        {
            @"C:\folder\file1.txt",
            @"C:\folder\file2.txt",
            @"D:\other\file3.doc"
        };
        var result = LogEntryDisplayHelper.BuildFileDetails(paths);

        Assert.AreEqual(3, result.Count);
        Assert.AreEqual("file1.txt", result[0]);
        Assert.AreEqual("file2.txt", result[1]);
        Assert.AreEqual("file3.doc", result[2]);
    }

    [TestMethod]
    public void BuildFileDetails_maxItems초과시_더보기메시지()
    {
        var paths = new List<string>();
        for (int i = 0; i < 25; i++)
            paths.Add($@"C:\folder\file{i}.txt");

        var result = LogEntryDisplayHelper.BuildFileDetails(paths, maxItems: 20);

        Assert.AreEqual(21, result.Count); // 20개 + "... and 5 more"
        Assert.AreEqual("... and 5 more", result[20]);
    }

    [TestMethod]
    public void BuildFileDetails_maxItems초과_커스텀포맷()
    {
        var paths = new List<string>();
        for (int i = 0; i < 25; i++)
            paths.Add($@"C:\folder\file{i}.txt");

        var result = LogEntryDisplayHelper.BuildFileDetails(paths, maxItems: 20, moreFormat: "... 외 {0}개");

        Assert.AreEqual("... 외 5개", result[20]);
    }

    [TestMethod]
    public void BuildFileDetails_정확히maxItems_더보기없음()
    {
        var paths = new List<string>();
        for (int i = 0; i < 20; i++)
            paths.Add($@"C:\folder\file{i}.txt");

        var result = LogEntryDisplayHelper.BuildFileDetails(paths, maxItems: 20);

        Assert.AreEqual(20, result.Count);
    }

    // ── DetermineOpenFolderPath ──

    [TestMethod]
    public void DetermineOpenFolderPath_소스만있으면_부모경로()
    {
        var sources = new List<string> { @"C:\Users\test\file.txt" };
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(sources, null);

        Assert.AreEqual(@"C:\Users\test", result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_대상경로있으면_대상우선()
    {
        var sources = new List<string> { @"C:\src\file.txt" };
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(sources, @"D:\dest");

        Assert.AreEqual(@"D:\dest", result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_소스없음_대상만()
    {
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(null, @"D:\dest");

        Assert.AreEqual(@"D:\dest", result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_모두null_null반환()
    {
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(null, null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_빈리스트_대상없으면_null()
    {
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(new List<string>(), null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_리모트소스_무시()
    {
        // 리모트 경로는 무시되어야 함
        var sources = new List<string> { "ftp://server/path/file.txt" };
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(sources, null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void DetermineOpenFolderPath_리모트대상_무시_소스부모사용()
    {
        var sources = new List<string> { @"C:\local\file.txt" };
        var result = LogEntryDisplayHelper.DetermineOpenFolderPath(
            sources, "ftp://remote/dest");

        // 리모트 대상은 무시되므로 소스 부모 경로 사용
        Assert.AreEqual(@"C:\local", result);
    }

    // ── GetFileName ──

    [TestMethod]
    public void GetFileName_로컬경로_파일명추출()
    {
        Assert.AreEqual("file.txt", LogEntryDisplayHelper.GetFileName(@"C:\folder\file.txt"));
    }

    [TestMethod]
    public void GetFileName_FTP경로_파일명추출()
    {
        Assert.AreEqual("document.pdf", LogEntryDisplayHelper.GetFileName("ftp://server/path/document.pdf"));
    }

    [TestMethod]
    public void GetFileName_SFTP경로_파일명추출()
    {
        Assert.AreEqual("data.csv", LogEntryDisplayHelper.GetFileName("sftp://host/home/user/data.csv"));
    }

    [TestMethod]
    public void GetFileName_빈문자열_빈문자열반환()
    {
        Assert.AreEqual("", LogEntryDisplayHelper.GetFileName(""));
    }

    [TestMethod]
    public void GetFileName_null_null반환()
    {
        Assert.IsNull(LogEntryDisplayHelper.GetFileName(null!));
    }

    [TestMethod]
    public void GetFileName_파일명만_그대로반환()
    {
        Assert.AreEqual("test.txt", LogEntryDisplayHelper.GetFileName("test.txt"));
    }

    [TestMethod]
    public void GetFileName_FTP_URLEncoded_디코딩()
    {
        Assert.AreEqual("파일 이름.txt",
            LogEntryDisplayHelper.GetFileName("ftp://server/path/%ED%8C%8C%EC%9D%BC%20%EC%9D%B4%EB%A6%84.txt"));
    }

    // ── ErrorFilter 상수 검증 ──

    [TestMethod]
    public void ErrorFilter_상수값검증()
    {
        Assert.AreEqual("__Error__", LogEntryDisplayHelper.ErrorFilter);
    }

    // ── CountErrors ──

    [TestMethod]
    public void CountErrors_에러없음_0반환()
    {
        var entries = new List<Span.Models.ActionLogEntry>
        {
            new() { Success = true },
            new() { Success = true }
        };
        Assert.AreEqual(0, LogEntryDisplayHelper.CountErrors(entries));
    }

    [TestMethod]
    public void CountErrors_혼합항목_에러수정확()
    {
        var entries = new List<Span.Models.ActionLogEntry>
        {
            new() { Success = true },
            new() { Success = false },
            new() { Success = true },
            new() { Success = false },
            new() { Success = false }
        };
        Assert.AreEqual(3, LogEntryDisplayHelper.CountErrors(entries));
    }

    [TestMethod]
    public void CountErrors_빈리스트_0반환()
    {
        Assert.AreEqual(0, LogEntryDisplayHelper.CountErrors(new List<Span.Models.ActionLogEntry>()));
    }
}
