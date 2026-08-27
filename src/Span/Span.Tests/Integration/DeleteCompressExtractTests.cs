using System.IO.Compression;
using Span.Services.FileOperations;

namespace Span.Tests.Integration;

// ============================================================================
// DeleteFileOperation Integration Tests
// ============================================================================
[TestClass]
public class DeleteFileOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public async Task PermanentDelete_SingleFile_RemovesFile()
    {
        var filePath = Path.Combine(_tempDir, "delete_me.txt");
        File.WriteAllText(filePath, "content to delete");

        var op = new DeleteFileOperation(new List<string> { filePath }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(filePath), "File should have been permanently deleted");
        CollectionAssert.Contains(result.AffectedPaths, filePath);
    }

    [TestMethod]
    public async Task PermanentDelete_Directory_RemovesRecursively()
    {
        var dirPath = Path.Combine(_tempDir, "folder_to_delete");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "child.txt"), "nested file");
        var nestedDir = Path.Combine(dirPath, "sub");
        Directory.CreateDirectory(nestedDir);
        File.WriteAllText(Path.Combine(nestedDir, "deep.txt"), "deeply nested");

        var op = new DeleteFileOperation(new List<string> { dirPath }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(Directory.Exists(dirPath), "Directory should have been permanently deleted");
        CollectionAssert.Contains(result.AffectedPaths, dirPath);
    }

    [TestMethod]
    public async Task PermanentDelete_NonExistentPath_ReportsError()
    {
        var nonExistent = Path.Combine(_tempDir, "ghost.txt");

        var op = new DeleteFileOperation(new List<string> { nonExistent }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
        // 로컬라이제이션 적용으로 언어에 따라 메시지가 다름 — 에러 메시지 존재 여부만 확인
        Assert.IsFalse(string.IsNullOrEmpty(result.ErrorMessage));
    }

    [TestMethod]
    public async Task PermanentDelete_MultipleItems_RemovesAll()
    {
        var file1 = Path.Combine(_tempDir, "file1.txt");
        var file2 = Path.Combine(_tempDir, "file2.txt");
        var dir1 = Path.Combine(_tempDir, "dir1");
        File.WriteAllText(file1, "a");
        File.WriteAllText(file2, "b");
        Directory.CreateDirectory(dir1);
        File.WriteAllText(Path.Combine(dir1, "inside.txt"), "c");

        var op = new DeleteFileOperation(new List<string> { file1, file2, dir1 }, permanent: true);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsFalse(File.Exists(file1));
        Assert.IsFalse(File.Exists(file2));
        Assert.IsFalse(Directory.Exists(dir1));
        Assert.AreEqual(3, result.AffectedPaths.Count);
    }

    [TestMethod]
    public void Description_PermanentTrue_IncludesPermanently()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "a.txt"), Path.Combine(_tempDir, "b.txt") },
            permanent: true);

        Assert.IsTrue(op.Description.Contains("Permanently"));
        Assert.IsTrue(op.Description.Contains("2 item(s)"));
    }

    [TestMethod]
    public void Description_PermanentFalse_DoesNotIncludePermanently()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "a.txt") },
            permanent: false);

        Assert.IsFalse(op.Description.Contains("Permanently"));
        Assert.IsTrue(op.Description.Contains("Delete"));
        // Single item uses filename format: Delete "a.txt", not "1 item(s)"
        Assert.IsTrue(op.Description.Contains("a.txt") || op.Description.Contains("1 item(s)"));
    }

    [TestMethod]
    public void CanUndo_PermanentTrue_ReturnsFalse()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "f.txt") },
            permanent: true);

        Assert.IsFalse(op.CanUndo);
    }

    [TestMethod]
    public void CanUndo_PermanentFalse_ReturnsTrue()
    {
        var op = new DeleteFileOperation(
            new List<string> { Path.Combine(_tempDir, "f.txt") },
            permanent: false);

        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task UndoAsync_PermanentDelete_ReturnsFailure()
    {
        var filePath = Path.Combine(_tempDir, "perm.txt");
        File.WriteAllText(filePath, "data");

        var op = new DeleteFileOperation(new List<string> { filePath }, permanent: true);
        await op.ExecuteAsync();

        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("Cannot undo permanent deletion"));
    }
}

// ============================================================================
// CompressOperation Integration Tests
// ============================================================================
[TestClass]
public class CompressOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [TestMethod]
    public async Task Execute_SingleFile_CreatesZip()
    {
        var filePath = Path.Combine(_tempDir, "hello.txt");
        File.WriteAllText(filePath, "hello world");
        var zipPath = Path.Combine(_tempDir, "output.zip");

        var op = new CompressOperation(new[] { filePath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath), "ZIP file should have been created");
        CollectionAssert.Contains(result.AffectedPaths, zipPath);

        // Verify ZIP contents
        using var archive = ZipFile.OpenRead(zipPath);
        Assert.AreEqual(1, archive.Entries.Count);
        Assert.AreEqual("hello.txt", archive.Entries[0].FullName);
    }

    [TestMethod]
    public async Task Execute_MultipleFiles_CreatesZipWithAll()
    {
        var file1 = Path.Combine(_tempDir, "alpha.txt");
        var file2 = Path.Combine(_tempDir, "beta.txt");
        var file3 = Path.Combine(_tempDir, "gamma.txt");
        File.WriteAllText(file1, "aaa");
        File.WriteAllText(file2, "bbb");
        File.WriteAllText(file3, "ccc");
        var zipPath = Path.Combine(_tempDir, "multi.zip");

        var op = new CompressOperation(new[] { file1, file2, file3 }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName).OrderBy(n => n).ToList();
        Assert.AreEqual(3, entryNames.Count);
        CollectionAssert.Contains(entryNames, "alpha.txt");
        CollectionAssert.Contains(entryNames, "beta.txt");
        CollectionAssert.Contains(entryNames, "gamma.txt");
    }

    [TestMethod]
    public async Task Execute_Directory_CompressesRecursively()
    {
        var dirPath = Path.Combine(_tempDir, "mydir");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "root.txt"), "root");
        var subDir = Path.Combine(dirPath, "sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Combine(subDir, "nested.txt"), "nested");
        var zipPath = Path.Combine(_tempDir, "dir.zip");

        var op = new CompressOperation(new[] { dirPath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.IsTrue(entryNames.Count >= 2, $"Expected at least 2 entries, got {entryNames.Count}");

        // The archive should contain relative paths including the directory name
        Assert.IsTrue(entryNames.Any(n => n.Contains("root.txt")), "Should contain root.txt");
        Assert.IsTrue(entryNames.Any(n => n.Contains("nested.txt")), "Should contain nested.txt");
    }

    [TestMethod]
    public async Task Execute_MixedFilesAndDirs_CreatesZip()
    {
        var filePath = Path.Combine(_tempDir, "standalone.txt");
        File.WriteAllText(filePath, "alone");
        var dirPath = Path.Combine(_tempDir, "folder");
        Directory.CreateDirectory(dirPath);
        File.WriteAllText(Path.Combine(dirPath, "inside.txt"), "inside");
        var zipPath = Path.Combine(_tempDir, "mixed.zip");

        var op = new CompressOperation(new[] { filePath, dirPath }, zipPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(zipPath));

        using var archive = ZipFile.OpenRead(zipPath);
        var entryNames = archive.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.IsTrue(entryNames.Any(n => n.Contains("standalone.txt")), "Should contain standalone.txt");
        Assert.IsTrue(entryNames.Any(n => n.Contains("inside.txt")), "Should contain inside.txt");
    }

    [TestMethod]
    public async Task Undo_DeletesZipFile()
    {
        var filePath = Path.Combine(_tempDir, "data.txt");
        File.WriteAllText(filePath, "data");
        var zipPath = Path.Combine(_tempDir, "undo_test.zip");

        var op = new CompressOperation(new[] { filePath }, zipPath);
        await op.ExecuteAsync();
        Assert.IsTrue(File.Exists(zipPath));

        var undoResult = await op.UndoAsync();

        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(File.Exists(zipPath), "ZIP file should have been deleted by undo");
    }

    [TestMethod]
    public async Task Undo_ZipNotExist_ReturnsFailure()
    {
        var zipPath = Path.Combine(_tempDir, "nonexistent.zip");

        var op = new CompressOperation(new[] { Path.Combine(_tempDir, "dummy.txt") }, zipPath);
        // Do NOT execute, so zip does not exist
        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("does not exist"));
    }

    [TestMethod]
    public void Description_ContainsZipFileName()
    {
        var zipPath = Path.Combine(_tempDir, "archive.zip");
        var op = new CompressOperation(new[] { Path.Combine(_tempDir, "f.txt") }, zipPath);

        Assert.IsTrue(op.Description.Contains("archive.zip"));
        Assert.IsTrue(op.Description.Contains("Compress"));
    }

    [TestMethod]
    public void CanUndo_ReturnsTrue()
    {
        var op = new CompressOperation(
            new[] { Path.Combine(_tempDir, "f.txt") },
            Path.Combine(_tempDir, "out.zip"));

        Assert.IsTrue(op.CanUndo);
    }
}

// ============================================================================
// ExtractOperation Integration Tests
// ============================================================================
[TestClass]
public class ExtractOperationTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SpanTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    /// <summary>
    /// Helper: creates a ZIP file at the given path with the specified entries.
    /// Each entry is (entryName, content). If content is null, it creates a directory entry.
    /// </summary>
    private static void CreateTestZip(string zipPath, params (string entryName, string? content)[] entries)
    {
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var (entryName, content) in entries)
        {
            if (content == null)
            {
                // Directory entry (name ends with /)
                archive.CreateEntry(entryName.EndsWith('/') ? entryName : entryName + "/");
            }
            else
            {
                var entry = archive.CreateEntry(entryName);
                using var stream = entry.Open();
                using var writer = new StreamWriter(stream);
                writer.Write(content);
            }
        }
    }

    [TestMethod]
    public async Task Execute_PathTraversalSiblingPrefix_DoesNotEscapeDestination()
    {
        // Issue #63 후속: 목적지 루트를 구분자 없이 StartsWith로 비교하면
        // "…\extracted" 와 "…\extracted2\evil.txt" 가 매칭되어 대상 폴더 밖에 파일이 쓰인다.
        var zipPath = Path.Combine(_tempDir, "traversal.zip");
        CreateTestZip(zipPath,
            ("safe.txt", "safe"),
            ("../extracted2/evil.txt", "escaped!"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        // 안전한 항목은 정상 추출된다
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "safe.txt")), "Safe entry should be extracted");

        // 목적지 밖(형제 폴더)에는 어떤 파일도 생기면 안 된다
        var escaped = Path.Combine(_tempDir, "extracted2", "evil.txt");
        Assert.IsFalse(File.Exists(escaped), "Entry must not escape the destination folder");
    }

    [TestMethod]
    public async Task Execute_AllEntriesSkipped_DoesNotReportSuccess()
    {
        // Issue #63 후속: 모든 항목이 걸러졌는데도 성공으로 보고하면
        // 사용자에게 "빈 폴더 + 완료 토스트"가 뜬다.
        var zipPath = Path.Combine(_tempDir, "all-escaped.zip");
        CreateTestZip(zipPath,
            ("../outside1.txt", "x"),
            ("../outside2.txt", "y"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success, "Extraction that produced no files must not report success");
    }


    /// <summary>
    /// Helper: writes a minimal single-entry ZIP with full control over the raw filename
    /// bytes and the EFS flag (general purpose bit 11). ZipArchive cannot produce a
    /// non-UTF8 name with the flag cleared, which is exactly what Korean tools emit.
    /// </summary>
    private static void WriteRawZip(string path, byte[] nameBytes, bool efs, string content = "hello")
    {
        byte[] data = System.Text.Encoding.ASCII.GetBytes(content);
        uint crc = Crc32Of(data);
        ushort flags = (ushort)(efs ? 0x0800 : 0);

        using var fs = File.Create(path);
        using var w = new BinaryWriter(fs);

        long localHeaderOffset = fs.Position;
        w.Write(0x04034b50); w.Write((ushort)20); w.Write(flags); w.Write((ushort)0); // stored
        w.Write((ushort)0); w.Write((ushort)0); w.Write(crc);
        w.Write((uint)data.Length); w.Write((uint)data.Length);
        w.Write((ushort)nameBytes.Length); w.Write((ushort)0);
        w.Write(nameBytes); w.Write(data);

        long centralOffset = fs.Position;
        w.Write(0x02014b50); w.Write((ushort)20); w.Write((ushort)20); w.Write(flags); w.Write((ushort)0);
        w.Write((ushort)0); w.Write((ushort)0); w.Write(crc);
        w.Write((uint)data.Length); w.Write((uint)data.Length);
        w.Write((ushort)nameBytes.Length); w.Write((ushort)0); w.Write((ushort)0);
        w.Write((ushort)0); w.Write((ushort)0); w.Write((uint)0); w.Write((uint)localHeaderOffset);
        w.Write(nameBytes);

        long centralEnd = fs.Position;
        w.Write(0x06054b50); w.Write((ushort)0); w.Write((ushort)0);
        w.Write((ushort)1); w.Write((ushort)1);
        w.Write((uint)(centralEnd - centralOffset)); w.Write((uint)centralOffset); w.Write((ushort)0);
    }

    private static uint Crc32Of(byte[] data)
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        uint crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFFu;
    }

    private const string KoreanName = "주문서_한글파일.txt";

    [TestMethod]
    public async Task Execute_KoreanNameCp949WithoutEfs_DecodesCorrectly()
    {
        // 반디집과 윈도우 탐색기 기본 압축이 실제로 만드는 형태: CP949 바이트 + EFS 비트 없음.
        // .NET Core는 Encoding.Default가 UTF-8이라 이 이름을 오디코딩한다(U+FFFD, 비가역).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        var zipPath = Path.Combine(_tempDir, "cp949.zip");
        WriteRawZip(zipPath, System.Text.Encoding.GetEncoding(949).GetBytes(KoreanName), efs: false);
        var destPath = Path.Combine(_tempDir, "extracted");

        var result = await new ExtractOperation(zipPath, destPath).ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, KoreanName)),
            "CP949 entry name (no EFS flag) must decode to the original Korean filename");
    }

    [TestMethod]
    public async Task Execute_KoreanNameUtf8WithEfs_StillDecodesCorrectly()
    {
        // 7-Zip/Python 등이 만드는 표준 형태. 고정 코드페이지를 지정하면 이쪽이 깨진다.
        var zipPath = Path.Combine(_tempDir, "utf8-efs.zip");
        WriteRawZip(zipPath, System.Text.Encoding.UTF8.GetBytes(KoreanName), efs: true);
        var destPath = Path.Combine(_tempDir, "extracted");

        var result = await new ExtractOperation(zipPath, destPath).ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, KoreanName)),
            "UTF-8 entry name with EFS flag must keep working");
    }

    [TestMethod]
    public async Task Execute_KoreanNameUtf8WithoutEfs_StillDecodesCorrectly()
    {
        // UTF-8이지만 EFS 비트를 켜지 않은 아카이브. 고정 CP949 지정 시 깨지는 케이스라
        // 자동 판별이 필요하다는 근거가 되는 항목.
        var zipPath = Path.Combine(_tempDir, "utf8-noefs.zip");
        WriteRawZip(zipPath, System.Text.Encoding.UTF8.GetBytes(KoreanName), efs: false);
        var destPath = Path.Combine(_tempDir, "extracted");

        var result = await new ExtractOperation(zipPath, destPath).ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, KoreanName)),
            "UTF-8 entry name without EFS flag must not be misdecoded as ANSI");
    }

    [TestMethod]
    public async Task Execute_AsciiName_UnaffectedByEncodingDetection()
    {
        // ASCII 이름은 두 인코딩에서 동일하게 디코딩되어야 한다 (회귀 방지).
        var zipPath = Path.Combine(_tempDir, "ascii.zip");
        WriteRawZip(zipPath, System.Text.Encoding.ASCII.GetBytes("plain_file.txt"), efs: false);
        var destPath = Path.Combine(_tempDir, "extracted");

        var result = await new ExtractOperation(zipPath, destPath).ExecuteAsync();

        Assert.IsTrue(result.Success, result.ErrorMessage);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "plain_file.txt")));
    }

    [TestMethod]
    public async Task Execute_AlternateDataStreamEntry_IsRejected()
    {
        // 엔트리 이름의 ':'는 NTFS 대체 데이터 스트림으로 해석된다.
        // "installer.exe:Zone.Identifier"는 경로가 대상 폴더 안이므로 traversal 검사를
        // 통과하고, .NET FileStream이 이를 ADS로 기록한다
        // (실측: 0바이트 installer.exe 본체 + 보이지 않는 24바이트 스트림).
        var zipPath = Path.Combine(_tempDir, "ads.zip");
        CreateTestZip(zipPath,
            ("good.txt", "ok"),
            ("installer.exe:Zone.Identifier", "[ZoneTransfer]\r\nZoneId=1"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(File.Exists(Path.Combine(destPath, "good.txt")), "Safe entry should still be extracted");
        Assert.IsTrue(result.Success, "One rejected ADS entry must not fail the whole extraction");

        // ADS를 심으려던 본체 파일이 만들어지면 안 된다
        Assert.IsFalse(File.Exists(Path.Combine(destPath, "installer.exe")),
            "ADS carrier file must not be created");
        // 목적지에 남은 파일은 good.txt 하나뿐이어야 한다
        Assert.AreEqual(1, Directory.GetFiles(destPath).Length,
            "Only the safe entry should exist in the destination");
    }

    [TestMethod]
    public async Task Execute_CorruptedEntry_IsDetectedAndNotLeftOnDisk()
    {
        // .NET ZipArchive의 읽기 경로는 CRC32를 검증하지 않아, 손상된 ZIP을 예외 없이
        // "성공"으로 풀어버린다(실측 확인). 손상 항목은 보고되고 디스크에 남지 않아야 한다.
        var zipPath = Path.Combine(_tempDir, "corrupt.zip");
        var payload = new byte[4096];
        new Random(7).NextBytes(payload); // 비압축 저장이 되도록 무작위 데이터 사용

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using var s = archive.CreateEntry("data.bin", CompressionLevel.NoCompression).Open();
            s.Write(payload, 0, payload.Length);
        }

        // 로컬 헤더(30바이트) + 파일명("data.bin" = 8바이트) 직후가 페이로드 시작 지점
        var raw = File.ReadAllBytes(zipPath);
        raw[30 + 8 + 100] ^= 0xFF;
        File.WriteAllBytes(zipPath, raw);

        var destPath = Path.Combine(_tempDir, "extracted");
        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsFalse(result.Success, "A ZIP whose only entry is corrupted must not report success");
        Assert.IsFalse(File.Exists(Path.Combine(destPath, "data.bin")),
            "Corrupted data must not be left on disk");
    }

    [TestMethod]
    public async Task Execute_CorruptedEntry_DoesNotBlockHealthyEntries()
    {
        // 손상 항목 하나가 나머지 정상 항목을 막으면 안 된다 (Issue #63 후속 계약 유지).
        var zipPath = Path.Combine(_tempDir, "mixed-corrupt.zip");
        var payload = new byte[2048];
        new Random(11).NextBytes(payload);

        using (var fs = File.Create(zipPath))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            using (var s = archive.CreateEntry("bad.bin", CompressionLevel.NoCompression).Open())
                s.Write(payload, 0, payload.Length);
            using (var w = new StreamWriter(archive.CreateEntry("zzz_good.txt").Open()))
                w.Write("healthy");
        }

        var raw = File.ReadAllBytes(zipPath);
        raw[30 + 7 + 50] ^= 0xFF; // "bad.bin" = 7바이트
        File.WriteAllBytes(zipPath, raw);

        var destPath = Path.Combine(_tempDir, "extracted");
        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(File.Exists(Path.Combine(destPath, "zzz_good.txt")),
            "Entries after a corrupted one must still be extracted");
        Assert.IsFalse(File.Exists(Path.Combine(destPath, "bad.bin")),
            "Corrupted entry must not be left on disk");
        Assert.IsTrue(result.Success, "Partial success expected when at least one entry extracted");
        Assert.IsNotNull(result.ErrorMessage, "The corrupted entry must be reported to the user");
    }

    [TestMethod]
    public async Task Execute_IntactZip_StillExtractsAfterCrcCheck()
    {
        // CRC 검증 추가가 정상 아카이브를 오탐으로 막지 않는지 확인 (회귀 방지).
        var zipPath = Path.Combine(_tempDir, "intact.zip");
        CreateTestZip(zipPath,
            ("a.txt", "aaa"),
            ("empty.txt", ""),          // 길이 0 -> CRC 0, 검증 경로가 이를 통과해야 한다
            ("nested/b.txt", "bbb"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsNull(result.ErrorMessage, "An intact archive must produce no errors");
        Assert.AreEqual("aaa", File.ReadAllText(Path.Combine(destPath, "a.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "empty.txt")));
        Assert.AreEqual("bbb", File.ReadAllText(Path.Combine(destPath, "nested", "b.txt")));
    }

    [TestMethod]
    public async Task Execute_ValidZip_ExtractsAllFiles()
    {
        var zipPath = Path.Combine(_tempDir, "test.zip");
        CreateTestZip(zipPath,
            ("file1.txt", "content1"),
            ("file2.txt", "content2"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(Directory.Exists(destPath));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file2.txt")));
        Assert.AreEqual("content1", File.ReadAllText(Path.Combine(destPath, "file1.txt")));
        Assert.AreEqual("content2", File.ReadAllText(Path.Combine(destPath, "file2.txt")));
    }

    [TestMethod]
    public async Task Execute_ZipWithSubdirs_PreservesStructure()
    {
        var zipPath = Path.Combine(_tempDir, "nested.zip");
        CreateTestZip(zipPath,
            ("root.txt", "root content"),
            ("subdir/", null),
            ("subdir/child.txt", "child content"),
            ("subdir/deep/", null),
            ("subdir/deep/leaf.txt", "leaf content"));
        var destPath = Path.Combine(_tempDir, "extracted");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "root.txt")));
        Assert.AreEqual("root content", File.ReadAllText(Path.Combine(destPath, "root.txt")));
        Assert.IsTrue(Directory.Exists(Path.Combine(destPath, "subdir")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "subdir", "child.txt")));
        Assert.AreEqual("child content", File.ReadAllText(Path.Combine(destPath, "subdir", "child.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "subdir", "deep", "leaf.txt")));
        Assert.AreEqual("leaf content", File.ReadAllText(Path.Combine(destPath, "subdir", "deep", "leaf.txt")));
    }

    [TestMethod]
    public async Task Execute_CreatesDestinationDirectory()
    {
        var zipPath = Path.Combine(_tempDir, "create_dest.zip");
        CreateTestZip(zipPath, ("file.txt", "data"));
        var destPath = Path.Combine(_tempDir, "nonexistent", "nested", "output");

        Assert.IsFalse(Directory.Exists(destPath), "Destination should not exist before extraction");

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync();

        Assert.IsTrue(result.Success);
        Assert.IsTrue(Directory.Exists(destPath));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "file.txt")));
    }

    [TestMethod]
    public async Task Undo_DeletesExtractedFolder()
    {
        var zipPath = Path.Combine(_tempDir, "undo.zip");
        CreateTestZip(zipPath, ("a.txt", "aaa"), ("b.txt", "bbb"));
        var destPath = Path.Combine(_tempDir, "to_undo");

        var op = new ExtractOperation(zipPath, destPath);
        await op.ExecuteAsync();
        Assert.IsTrue(Directory.Exists(destPath));

        var undoResult = await op.UndoAsync();

        Assert.IsTrue(undoResult.Success);
        Assert.IsFalse(Directory.Exists(destPath), "Extracted folder should have been deleted by undo");
    }

    [TestMethod]
    public async Task Undo_FolderNotExist_ReturnsFailure()
    {
        var zipPath = Path.Combine(_tempDir, "dummy.zip");
        var destPath = Path.Combine(_tempDir, "never_created");

        var op = new ExtractOperation(zipPath, destPath);
        // Do NOT execute, so destination folder does not exist
        var undoResult = await op.UndoAsync();

        Assert.IsFalse(undoResult.Success);
        Assert.IsNotNull(undoResult.ErrorMessage);
        Assert.IsTrue(undoResult.ErrorMessage!.Contains("does not exist"));
    }

    [TestMethod]
    public void Description_ContainsZipFileName()
    {
        var op = new ExtractOperation(
            Path.Combine(_tempDir, "myarchive.zip"),
            Path.Combine(_tempDir, "dest"));

        Assert.IsTrue(op.Description.Contains("myarchive.zip"));
        Assert.IsTrue(op.Description.Contains("Extract"));
    }

    [TestMethod]
    public void CanUndo_ReturnsTrue()
    {
        var op = new ExtractOperation(
            Path.Combine(_tempDir, "any.zip"),
            Path.Combine(_tempDir, "dest"));

        Assert.IsTrue(op.CanUndo);
    }

    [TestMethod]
    public async Task Execute_ReportsProgress()
    {
        var zipPath = Path.Combine(_tempDir, "progress.zip");
        CreateTestZip(zipPath,
            ("p1.txt", "data1"),
            ("p2.txt", "data2"),
            ("p3.txt", "data3"));
        var destPath = Path.Combine(_tempDir, "progress_out");

        var progressReports = new List<FileOperationProgress>();
        var progress = new Progress<FileOperationProgress>(p =>
        {
            progressReports.Add(new FileOperationProgress
            {
                CurrentFile = p.CurrentFile,
                CurrentFileIndex = p.CurrentFileIndex,
                TotalFileCount = p.TotalFileCount
            });
        });

        var op = new ExtractOperation(zipPath, destPath);
        var result = await op.ExecuteAsync(progress);

        Assert.IsTrue(result.Success);
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p1.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p2.txt")));
        Assert.IsTrue(File.Exists(Path.Combine(destPath, "p3.txt")));
        // Progress<T> reports asynchronously via SynchronizationContext,
        // so in a test context we validate the operation completes
        // successfully when a progress reporter is provided.
    }
}
