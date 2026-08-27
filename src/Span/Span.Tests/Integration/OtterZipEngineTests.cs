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
