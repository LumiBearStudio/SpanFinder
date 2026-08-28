using System.IO.Compression;
using Span.Services.Archive;

namespace Span.Tests.Integration;

/// <summary>
/// Issue #64 — copying a file out of an archive, and dragging one to another app.
///
/// Both fail for the same reason: "archive://C:\x.zip/inner.txt" is not a path any program
/// can open. The fix stages the entry as a real file first. These tests cover that staging.
/// </summary>
[TestClass]
public class ArchiveEntryStagingTests
{
    private string _tempDir = null!;
    private string _zipPath = null!;

    private const string KoreanEntry = "한글파일.txt";

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanStaging_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);

        _zipPath = Path.Combine(_tempDir, "bundle.zip");
        using var fs = File.Create(_zipPath);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);

        Write(archive, "readme.txt", "top level");
        Write(archive, KoreanEntry, "한글 내용");
        Write(archive, "docs/guide.txt", "nested guide");
        Write(archive, "docs/images/logo.txt", "deep file");

        static void Write(ZipArchive a, string name, string content)
        {
            using var w = new StreamWriter(a.CreateEntry(name).Open());
            w.Write(content);
        }
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    private string Entry(string internalPath) =>
        Span.Helpers.ArchivePathHelper.Combine(_zipPath, internalPath);

    // ------------------------------------------------------------------
    // 미리보기 스테이징 — 선택할 때마다 도는 경로라 비용이 묶여 있어야 한다.
    // ------------------------------------------------------------------

    [TestMethod]
    public async Task StageForPreview_SmallEntry_ProducesAReadableFile()
    {
        var size = new FileInfo(_zipPath).Length; // 상한만 넘지 않으면 됨
        var staged = await ArchiveEntryStaging.StageForPreviewAsync(
            Entry("readme.txt"), entrySize: "top level".Length, maxBytes: 16 * 1024 * 1024);

        Assert.IsNotNull(staged);
        Assert.AreEqual("top level", File.ReadAllText(staged!));
        Assert.IsTrue(Path.GetFileName(staged!).EndsWith("readme.txt"),
            "staged name should still end with the original file name so the extension survives");
    }

    [TestMethod]
    public async Task StageForPreview_OversizedEntry_IsRejectedWithoutOpeningTheArchive()
    {
        // 크기는 목록에서 이미 알고 있으므로 큰 항목은 압축을 건드리지도 않아야 한다.
        var staged = await ArchiveEntryStaging.StageForPreviewAsync(
            Entry("readme.txt"), entrySize: 64L * 1024 * 1024, maxBytes: 16 * 1024 * 1024);

        Assert.IsNull(staged);
    }

    [TestMethod]
    public async Task StageForPreview_SameEntryTwice_ReusesTheSameFile()
    {
        // 화살표로 앞뒤로 훑을 때 매번 다시 꺼내면 안 된다.
        long size = "top level".Length;
        var first = await ArchiveEntryStaging.StageForPreviewAsync(Entry("readme.txt"), size, 16 * 1024 * 1024);
        Assert.IsNotNull(first);
        var firstWrite = File.GetLastWriteTimeUtc(first!);

        var second = await ArchiveEntryStaging.StageForPreviewAsync(Entry("readme.txt"), size, 16 * 1024 * 1024);

        Assert.AreEqual(first, second, "the same entry must map to the same staged file");
        Assert.AreEqual(firstWrite, File.GetLastWriteTimeUtc(second!), "it must not be re-extracted");
    }

    [TestMethod]
    public async Task StageForPreview_NonArchivePath_ReturnsNull()
    {
        var real = Path.Combine(_tempDir, "plain.txt");
        File.WriteAllText(real, "x");

        Assert.IsNull(await ArchiveEntryStaging.StageForPreviewAsync(real, 1, 16 * 1024 * 1024));
    }

    [TestMethod]
    public async Task StageForPreview_MissingEntry_ReturnsNullWithoutThrowing()
    {
        Assert.IsNull(await ArchiveEntryStaging.StageForPreviewAsync(
            Entry("nope.txt"), 10, 16 * 1024 * 1024));
    }

    [TestMethod]
    public void PreviewResolver_OnlyStagesFormatsThatNeedARealFile()
    {
        // 미디어는 스트리밍이라 제외 — 선택만으로 수백 MB를 꺼내면 안 된다.
        // 아카이브/폴더/일반은 애초에 파일이 필요 없다.
        foreach (var t in new[] { Span.Models.PreviewType.Media, Span.Models.PreviewType.Archive,
                                  Span.Models.PreviewType.Folder, Span.Models.PreviewType.Generic,
                                  Span.Models.PreviewType.None })
        {
            var task = Span.Helpers.ArchivePreviewResolver.ResolveAsync(
                t, Entry("readme.txt"), 10, CancellationToken.None);
            Assert.IsNull(task.GetAwaiter().GetResult(), $"{t} should not be staged");
        }
    }

    [TestMethod]
    public async Task PreviewResolver_StagesTextAndImageKinds()
    {
        var staged = await Span.Helpers.ArchivePreviewResolver.ResolveAsync(
            Span.Models.PreviewType.Text, Entry("readme.txt"), "top level".Length, CancellationToken.None);

        Assert.IsNotNull(staged);
        Assert.IsTrue(File.Exists(staged!));
    }

    [TestMethod]
    public async Task PreviewResolver_RejectsOversizedEntries()
    {
        var staged = await Span.Helpers.ArchivePreviewResolver.ResolveAsync(
            Span.Models.PreviewType.Text, Entry("readme.txt"),
            Span.Helpers.ArchivePreviewResolver.MaxArchivePreviewBytes + 1, CancellationToken.None);

        Assert.IsNull(staged);
    }

    [TestMethod]
    public void ArchiveFileExists_TracksTheUnderlyingArchive()
    {
        // Miller 컬럼 유효성 검사가 쓰는 판정. archive:// 는 디렉터리가 아니라
        // Directory.Exists가 항상 false이고, 그걸 그대로 믿으면 압축 안에서 복사해
        // 붙여넣을 때마다 열어 둔 아카이브 컬럼이 닫힌다.
        var archiveRoot = Span.Helpers.ArchivePathHelper.Combine(_zipPath, "");
        Assert.IsFalse(Directory.Exists(archiveRoot), "전제: archive:// 는 디렉터리로 존재하지 않는다");
        Assert.IsTrue(Span.Helpers.ArchivePathHelper.ArchiveFileExists(archiveRoot),
            "근거 아카이브 파일이 있으면 유효한 위치다");

        Assert.IsTrue(Span.Helpers.ArchivePathHelper.ArchiveFileExists(Entry("readme.txt")));

        // 아카이브가 사라지면 더 이상 유효하지 않다
        var gone = Span.Helpers.ArchivePathHelper.Combine(Path.Combine(_tempDir, "gone.zip"), "");
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.ArchiveFileExists(gone));

        // 일반 경로는 이 판정의 대상이 아니다
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.ArchiveFileExists(_tempDir));
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.ArchiveFileExists(""));
    }

    [TestMethod]
    public void ContainsArchiveEntry_DetectsArchivePaths()
    {
        Assert.IsTrue(ArchiveEntryStaging.ContainsArchiveEntry([@"C:\a.txt", Entry("readme.txt")]));
        Assert.IsFalse(ArchiveEntryStaging.ContainsArchiveEntry([@"C:\a.txt", @"C:\b.txt"]));
        Assert.IsFalse(ArchiveEntryStaging.ContainsArchiveEntry(null));
    }

    [TestMethod]
    public async Task Materialize_SingleEntry_ProducesARealFileWithTheSameContent()
    {
        var result = await ArchiveEntryStaging.MaterializeAsync([Entry("readme.txt")]);

        Assert.AreEqual(1, result.Count);
        Assert.IsFalse(Span.Helpers.ArchivePathHelper.IsArchivePath(result[0]),
            "the caller must receive a real path, not an archive:// one");
        Assert.IsTrue(File.Exists(result[0]), "the staged file must exist on disk");
        Assert.AreEqual("readme.txt", Path.GetFileName(result[0]), "the original name should be kept");
        Assert.AreEqual("top level", File.ReadAllText(result[0]));
    }

    [TestMethod]
    public async Task Materialize_KoreanEntryName_SurvivesStaging()
    {
        var result = await ArchiveEntryStaging.MaterializeAsync([Entry(KoreanEntry)]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual(KoreanEntry, Path.GetFileName(result[0]));
        Assert.AreEqual("한글 내용", File.ReadAllText(result[0]));
    }

    [TestMethod]
    public async Task Materialize_NestedEntry_KeepsOnlyTheFileName()
    {
        // Dropping "docs/guide.txt" onto the desktop should produce "guide.txt", not a
        // "docs" folder — the user dragged the file, not its parent.
        var result = await ArchiveEntryStaging.MaterializeAsync([Entry("docs/guide.txt")]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("guide.txt", Path.GetFileName(result[0]));
        Assert.AreEqual("nested guide", File.ReadAllText(result[0]));
    }

    [TestMethod]
    public async Task Materialize_FolderEntry_ProducesTheWholeSubtree()
    {
        var result = await ArchiveEntryStaging.MaterializeAsync([Entry("docs")]);

        Assert.AreEqual(1, result.Count);
        Assert.IsTrue(Directory.Exists(result[0]), "a folder entry should stage as a folder");
        Assert.AreEqual("docs", Path.GetFileName(result[0]));
        Assert.AreEqual("nested guide", File.ReadAllText(Path.Combine(result[0], "guide.txt")));
        Assert.AreEqual("deep file", File.ReadAllText(Path.Combine(result[0], "images", "logo.txt")));
    }

    [TestMethod]
    public async Task Materialize_MixedPaths_LeavesRealPathsUntouched()
    {
        var realFile = Path.Combine(_tempDir, "outside.txt");
        File.WriteAllText(realFile, "not in an archive");

        var result = await ArchiveEntryStaging.MaterializeAsync([realFile, Entry("readme.txt")]);

        Assert.AreEqual(2, result.Count);
        CollectionAssert.Contains(result, realFile, "non-archive paths must pass through unchanged");
        Assert.IsTrue(result.Any(p => Path.GetFileName(p) == "readme.txt"));
    }

    [TestMethod]
    public async Task Materialize_MultipleEntries_AllStaged()
    {
        var result = await ArchiveEntryStaging.MaterializeAsync(
            [Entry("readme.txt"), Entry(KoreanEntry), Entry("docs/guide.txt")]);

        Assert.AreEqual(3, result.Count);
        Assert.IsTrue(result.All(File.Exists));
    }

    [TestMethod]
    public async Task Materialize_MissingEntry_IsDroppedWithoutFailingTheBatch()
    {
        // One unreadable entry should not cost the user the other four files they dragged.
        var result = await ArchiveEntryStaging.MaterializeAsync(
            [Entry("does-not-exist.txt"), Entry("readme.txt")]);

        Assert.AreEqual(1, result.Count);
        Assert.AreEqual("readme.txt", Path.GetFileName(result[0]));
    }

    [TestMethod]
    public async Task Materialize_MissingArchiveFile_ReturnsEmptyWithoutThrowing()
    {
        var ghost = Span.Helpers.ArchivePathHelper.Combine(
            Path.Combine(_tempDir, "ghost.zip"), "readme.txt");

        var result = await ArchiveEntryStaging.MaterializeAsync([ghost]);

        Assert.AreEqual(0, result.Count);
    }

    [TestMethod]
    public async Task Materialize_NoArchiveEntries_DoesNoWork()
    {
        var realFile = Path.Combine(_tempDir, "plain.txt");
        File.WriteAllText(realFile, "x");

        var result = await ArchiveEntryStaging.MaterializeAsync([realFile]);

        CollectionAssert.AreEqual(new[] { realFile }, result);
    }

    [TestMethod]
    public async Task Materialize_StagesOutsideTheArchiveFolder()
    {
        // The staged copy must not land next to the archive — that would look to the user
        // like the app scattered files into their folder.
        var result = await ArchiveEntryStaging.MaterializeAsync([Entry("readme.txt")]);

        Assert.AreEqual(1, result.Count);
        var stagedDir = Path.GetDirectoryName(Path.GetFullPath(result[0]))!;
        Assert.IsFalse(stagedDir.StartsWith(_tempDir, StringComparison.OrdinalIgnoreCase),
            "staging must not write into the archive's own folder");
    }

    [TestMethod]
    public async Task Materialize_EntryEscapingTheStagingRoot_IsSkipped()
    {
        // Entry names come from the archive and are attacker-controlled. A folder drag
        // must not be able to write outside the staging folder.
        var evilZip = Path.Combine(_tempDir, "evil.zip");
        using (var fs = File.Create(evilZip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(archive.CreateEntry("payload/safe.txt").Open()))
                w.Write("safe");
            using (var w = new StreamWriter(archive.CreateEntry("payload/../../escaped.txt").Open()))
                w.Write("escaped!");
        }

        var result = await ArchiveEntryStaging.MaterializeAsync(
            [Span.Helpers.ArchivePathHelper.Combine(evilZip, "payload")]);

        Assert.AreEqual(1, result.Count);
        var staged = result[0];
        Assert.IsTrue(File.Exists(Path.Combine(staged, "safe.txt")), "the safe entry should still be staged");

        var parent = Directory.GetParent(staged)!.FullName;
        Assert.IsFalse(File.Exists(Path.Combine(parent, "escaped.txt")));
        Assert.IsFalse(File.Exists(Path.Combine(Directory.GetParent(parent)!.FullName, "escaped.txt")),
            "no entry may be written outside the staging root");
    }
}
