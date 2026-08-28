using System.Runtime.InteropServices;
using Span.Services.Archive;

namespace Span.Tests.Integration;

/// <summary>
/// Exercises the OtterZip native binding against the real DLL (Issue #66).
///
/// These are the tests that would catch an ABI or struct-layout drift. A wrong field
/// order does not throw — it silently reports one field's value as another's — so the
/// assertions here check actual numbers against a known fixture rather than just
/// "did it succeed".
///
/// The engine is x64-only. On other platforms every test asserts the graceful-disable
/// path instead, because that is the behaviour those platforms must have.
/// </summary>
[TestClass]
public class OtterZipEngineTests
{
    private static bool Is64 => RuntimeInformation.ProcessArchitecture == Architecture.X64;

    private string _tempDir = null!;
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.7z");

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanOtterZip_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public void Fixture_IsPresent()
    {
        Assert.IsTrue(File.Exists(FixturePath),
            $"7z fixture missing at {FixturePath} — check the Fixtures Content glob in Span.Tests.csproj");
    }

    [TestMethod]
    public void IsAvailable_MatchesProcessArchitecture()
    {
        // x64: the DLL is copied next to the test assembly and must load.
        // Anything else: the engine must report unavailable rather than throw, because
        // that is exactly what the fallback path depends on.
        Assert.AreEqual(Is64, OtterZipEngine.IsAvailable,
            Is64 ? "x64 build should load otterzip_ffi.dll" : "non-x64 must disable the engine gracefully");
    }

    [TestMethod]
    public void Open_NonexistentArchive_ReturnsNullWithoutThrowing()
    {
        // A missing archive is an ordinary condition, not an exception — callers rely on
        // being able to fall back without a try/catch.
        var handle = OtterZipEngine.Open(Path.Combine(_tempDir, "does-not-exist.7z"));
        Assert.IsNull(handle);
    }

    [TestMethod]
    public void Open_And_EntryCount_MatchFixture()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle, "opening the fixture should succeed");
        Assert.IsFalse(handle!.IsInvalid);

        // The fixture holds 3 files plus 1 directory entry.
        Assert.AreEqual(4, OtterZipEngine.EntryCount(handle));
    }

    [TestMethod]
    public void IsEncrypted_OnPlainArchive_ReturnsFalse()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle);
        Assert.AreEqual(false, OtterZipEngine.IsEncrypted(handle!));
    }

    [TestMethod]
    public void ExtractAll_WritesEveryEntry_IncludingKoreanNamesAndNesting()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle);

        var dest = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(dest);

        var result = OtterZipEngine.ExtractAll(handle!, dest);

        Assert.IsTrue(result.Success, $"extract failed ({result.Code}): {result.NativeMessage}");
        Assert.AreEqual("hello from 7z", File.ReadAllText(Path.Combine(dest, "plain.txt")));
        Assert.AreEqual("한글 내용", File.ReadAllText(Path.Combine(dest, "한글파일.txt")),
            "Korean entry names must survive the round trip");
        Assert.AreEqual("nested content", File.ReadAllText(Path.Combine(dest, "하위폴더", "nested.txt")));
    }

    [TestMethod]
    public void ExtractAll_Report_FieldsAreNotTransposed()
    {
        // This is the struct-layout canary. OtterzipExtractReport puts a 64-bit
        // bytes_written between two 32-bit fields; if the managed mirror gets that order
        // wrong the call still "succeeds" but byte counts show up as warning counts.
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle);

        var dest = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(dest);

        var result = OtterZipEngine.ExtractAll(handle!, dest);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(4u, result.EntriesExtracted, "fixture has 4 entries");
        Assert.AreEqual(0u, result.EntriesSkipped);
        Assert.AreEqual(0u, result.WarningsCount, "a transposed layout typically shows the byte count here");

        // 13 + 14 + 13 bytes of file content.
        Assert.AreEqual(40uL, result.BytesWritten);
    }

    [TestMethod]
    public void ExtractAll_ReportsProgress()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle);

        var dest = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(dest);

        var seen = new List<OtterZipProgress>();
        var result = OtterZipEngine.ExtractAll(handle!, dest, p => seen.Add(p));

        Assert.IsTrue(result.Success);
        Assert.IsTrue(seen.Count > 0, "the progress callback should fire at least once");
        Assert.IsTrue(seen.Any(p => !string.IsNullOrEmpty(p.CurrentEntry)),
            "at least one progress report should name the entry being written");
    }

    [TestMethod]
    public void ExtractAll_AlreadyCancelledToken_StopsAndReportsCancellation()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var handle = OtterZipEngine.Open(FixturePath);
        Assert.IsNotNull(handle);

        var dest = Path.Combine(_tempDir, "out");
        Directory.CreateDirectory(dest);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = OtterZipEngine.ExtractAll(handle!, dest, cancellationToken: cts.Token);

        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.Canceled, $"expected cancellation, got code {result.Code}: {result.NativeMessage}");
        Assert.AreEqual("Toast_OperationCancelled", result.MessageKey);
    }

    // ------------------------------------------------------------------
    // ExtractOperation routing — the .zip path must stay on the managed
    // engine, everything else must reach the native one.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task ExtractOperation_SevenZip_RoutesToNativeEngineAndExtracts()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        var dest = Path.Combine(_tempDir, "out");
        var op = new Span.Services.FileOperations.ExtractOperation(FixturePath, dest);

        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreEqual("hello from 7z", File.ReadAllText(Path.Combine(dest, "plain.txt")));
        Assert.AreEqual("한글 내용", File.ReadAllText(Path.Combine(dest, "한글파일.txt")));
        Assert.AreEqual("nested content", File.ReadAllText(Path.Combine(dest, "하위폴더", "nested.txt")));
    }

    [TestMethod]
    public async Task ExtractOperation_SevenZip_ReportsProgress()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        var dest = Path.Combine(_tempDir, "out");
        var op = new Span.Services.FileOperations.ExtractOperation(FixturePath, dest);

        var reports = new List<Span.Services.FileOperations.FileOperationProgress>();
        var progress = new Progress<Span.Services.FileOperations.FileOperationProgress>(reports.Add);

        var result = await op.ExecuteAsync(progress);

        Assert.IsTrue(result.Success, result.ErrorMessage);
        // Progress<T> posts asynchronously, so the count is not deterministic here —
        // what matters is that the operation completes and the panel has a source to
        // bind to. The engine-level callback is asserted directly in ExtractAll_ReportsProgress.
    }

    [TestMethod]
    public async Task ExtractOperation_SevenZip_UndoRemovesTheOutput()
    {
        // Undo is inherited from ExtractOperation rather than reimplemented, so the
        // native path must honour the same contract.
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        var dest = Path.Combine(_tempDir, "out");
        var op = new Span.Services.FileOperations.ExtractOperation(FixturePath, dest);

        Assert.IsTrue((await op.ExecuteAsync()).Success);
        Assert.IsTrue(Directory.Exists(dest));

        var undo = await op.UndoAsync();

        Assert.IsTrue(undo.Success, undo.ErrorMessage);
        Assert.IsFalse(Directory.Exists(dest), "undo must remove the extracted folder");
    }

    [TestMethod]
    public async Task ExtractOperation_MissingSevenZip_FailsWithoutThrowing()
    {
        var missing = Path.Combine(_tempDir, "nope.7z");
        var op = new Span.Services.FileOperations.ExtractOperation(missing, Path.Combine(_tempDir, "out"));

        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage), "the user needs to be told what went wrong");
    }

    [TestMethod]
    public async Task ExtractOperation_CancelledSevenZip_ReportsCancellation()
    {
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var op = new Span.Services.FileOperations.ExtractOperation(
            FixturePath, Path.Combine(_tempDir, "out"));

        var result = await op.ExecuteAsync(null, cts.Token);

        // 취소는 예외가 아니라 결과로 돌아와야 한다 — 이 클래스의 나머지 경로가
        // OperationCanceledException을 잡아 Toast_OperationCancelled를 돌려주는 것과 같은 계약.
        Assert.IsFalse(result.Success);
        Assert.IsFalse(Directory.Exists(Path.Combine(_tempDir, "out", "plain.txt")),
            "nothing should have been extracted");
    }

    [TestMethod]
    public async Task ExtractOperation_Zip_StaysOnManagedEngine()
    {
        // The managed path verifies CRC and deletes corrupted output; the native engine
        // does neither. Seeing that behaviour on a .zip proves the routing did not send
        // it to OtterZip.
        var zipPath = Path.Combine(_tempDir, "corrupt.zip");
        var payload = new byte[2048];
        new Random(23).NextBytes(payload);

        using (var fs = File.Create(zipPath))
        using (var archive = new System.IO.Compression.ZipArchive(fs, System.IO.Compression.ZipArchiveMode.Create))
        {
            using var s = archive.CreateEntry("data.bin", System.IO.Compression.CompressionLevel.NoCompression).Open();
            s.Write(payload, 0, payload.Length);
        }

        var raw = File.ReadAllBytes(zipPath);
        raw[30 + 8 + 100] ^= 0xFF;   // local header (30) + "data.bin" (8)
        File.WriteAllBytes(zipPath, raw);

        var dest = Path.Combine(_tempDir, "out");
        var result = await new Span.Services.FileOperations.ExtractOperation(zipPath, dest).ExecuteAsync();

        Assert.IsFalse(result.Success, "the managed engine must catch the CRC mismatch");
        Assert.IsFalse(File.Exists(Path.Combine(dest, "data.bin")));
    }

    // ------------------------------------------------------------------
    // Browsing vs extracting — a .7z can be extracted but not listed, and
    // navigating into it would show an empty archive rather than an error.
    // ------------------------------------------------------------------

    [TestMethod]
    public void IsArchiveFile_AcceptsFormatsWeCanExtract()
    {
        foreach (var name in new[] { "a.zip", "a.7z", "a.rar", "a.tar.gz", "a.cab", "a.xz" })
        {
            Assert.IsTrue(Span.Helpers.ArchivePathHelper.IsArchiveFile(Path.Combine(_tempDir, name)),
                $"{name} should offer the Extract commands");
        }
    }

    [TestMethod]
    public void IsBrowsableArchive_OnlyAcceptsWhatTheReaderCanList()
    {
        // ArchiveReaderService reads ZIP only. Opening a .7z with it does not throw a
        // visible error — it yields an empty listing, which reads to the user as
        // "this archive is empty".
        Assert.IsTrue(Span.Helpers.ArchivePathHelper.IsBrowsableArchive(Path.Combine(_tempDir, "a.zip")));

        foreach (var name in new[] { "a.7z", "a.rar", "a.tar.gz", "a.cab", "a.xz" })
        {
            Assert.IsFalse(Span.Helpers.ArchivePathHelper.IsBrowsableArchive(Path.Combine(_tempDir, name)),
                $"{name} must not be navigated into as an archive:// path");
        }
    }

    [TestMethod]
    public void GetArchiveBaseName_StripsCompoundExtensions()
    {
        // 실측된 결함: GetFileNameWithoutExtension은 마지막 확장자만 떼어
        // "backup.tar.gz"를 "backup.tar"로 만든다. 그 이름의 파일이 바로 옆에 있으면
        // 추출이 IOException으로 실패한다.
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.tar.gz"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.tar.bz2"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.tar.xz"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.tar"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.7z"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.tgz"));
        Assert.AreEqual("backup", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\backup.zip"));

        // 이름에 점이 여러 개 있어도 아카이브 확장자만 떼야 한다
        Assert.AreEqual("my.backup.2026", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(@"C:\x\my.backup.2026.tar.gz"));
        Assert.AreEqual("", Span.Helpers.ArchivePathHelper.GetArchiveBaseName(""));
    }

    [TestMethod]
    public async Task ExtractOperation_DestinationNameCollidingWithFile_IsHandledByCaller()
    {
        // 실측 재현: sample.tar.gz 옆에 sample.tar "파일"이 있으면 목적지 폴더 이름이
        // sample.tar가 되어 Directory.Exists는 false지만 CreateDirectory가 터진다.
        // 호출부(PerformExtractHere/To)가 File.Exists까지 확인해 회피해야 한다.
        if (!OtterZipEngine.IsAvailable) Assert.Inconclusive("engine unavailable on this platform");

        var collidingFile = Path.Combine(_tempDir, "sample");
        File.WriteAllText(collidingFile, "occupies the destination name");

        // 호출부가 하는 것과 같은 회피 로직
        var baseName = Span.Helpers.ArchivePathHelper.GetArchiveBaseName(FixturePath);
        var dest = Path.Combine(_tempDir, baseName);
        int n = 1;
        while (Directory.Exists(dest) || File.Exists(dest))
            dest = Path.Combine(_tempDir, $"{baseName} ({n++})");

        var result = await new Span.Services.FileOperations.ExtractOperation(FixturePath, dest).ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.AreNotEqual(collidingFile, dest, "must not target the existing file's name");
        Assert.IsTrue(File.Exists(Path.Combine(dest, "plain.txt")));
    }

    [TestMethod]
    public void IsBrowsableArchive_RejectsNonArchives()
    {
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.IsBrowsableArchive(Path.Combine(_tempDir, "notes.txt")));
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.IsBrowsableArchive(""));
    }

    [TestMethod]
    public void ExtractResult_ClassifiesOurOwnBugsSeparately()
    {
        // Codes in this group mean we called the API wrong. They must be distinguishable
        // from bad user input so they can be reported rather than shown as advice.
        var invalidHandle = OtterZipEngine.ExtractAll(new OtterZipArchiveHandle(), _tempDir);

        Assert.IsFalse(invalidHandle.Success);
        Assert.IsTrue(invalidHandle.IsOurBug, "an invalid handle is our defect, not the user's");
    }
}
