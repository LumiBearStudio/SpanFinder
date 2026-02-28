namespace Span.Tests;

/// <summary>
/// Auto-creates test fixture folders/files at E:\TEST for integration and stress tests.
/// Call EnsureTestFixtures() before tests that need real file system structures.
/// </summary>
public static class TestFixtureHelper
{
    public const string TestRoot = @"E:\TEST";

    public static string SmallFolder => Path.Combine(TestRoot, "SmallFolder");
    public static string LargeFolder => Path.Combine(TestRoot, "LargeFolder");
    public static string DeepFolder => Path.Combine(TestRoot, "DeepFolder");
    public static string MixedTypes => Path.Combine(TestRoot, "MixedTypes");
    public static string HiddenFiles => Path.Combine(TestRoot, "HiddenFiles");
    public static string ConflictTest => Path.Combine(TestRoot, "ConflictTest");
    public static string ReadOnlyFolder => Path.Combine(TestRoot, "ReadOnlyFolder");
    public static string UnicodeNames => Path.Combine(TestRoot, "UnicodeNames");
    public static string StressTest => Path.Combine(TestRoot, "StressTest");

    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>
    /// Ensures test fixtures exist at E:\TEST. Safe to call multiple times.
    /// </summary>
    public static void EnsureTestFixtures()
    {
        lock (_lock)
        {
            if (_initialized) return;

            if (!Directory.Exists(Path.GetPathRoot(TestRoot)))
                throw new InvalidOperationException($"Drive {Path.GetPathRoot(TestRoot)} is not available. Mount the drive or skip integration tests.");

            Directory.CreateDirectory(TestRoot);

            CreateSmallFolder();
            CreateLargeFolder();
            CreateDeepFolder();
            CreateMixedTypes();
            CreateHiddenFiles();
            CreateConflictTest();
            CreateReadOnlyFolder();
            CreateUnicodeNames();
            // StressTest is created on demand by stress tests

            _initialized = true;
        }
    }

    /// <summary>
    /// Creates the StressTest folder with the specified number of files.
    /// </summary>
    public static void EnsureStressTestFolder(int fileCount = 10000)
    {
        if (Directory.Exists(StressTest) && Directory.GetFiles(StressTest).Length >= fileCount)
            return;

        Directory.CreateDirectory(StressTest);

        for (int i = 0; i < fileCount; i++)
        {
            var path = Path.Combine(StressTest, $"stress_{i:D5}.txt");
            if (!File.Exists(path))
                File.WriteAllText(path, $"Stress test file {i}");
        }
    }

    /// <summary>
    /// Creates a temporary copy directory for file operation tests.
    /// Returns the path to the temp directory.
    /// </summary>
    public static string CreateTempCopyDir(string prefix = "SpanTest")
    {
        var tempDir = Path.Combine(TestRoot, $"_temp_{prefix}_{Guid.NewGuid():N}"[..32]);
        Directory.CreateDirectory(tempDir);
        return tempDir;
    }

    /// <summary>
    /// Safely deletes a temporary directory.
    /// </summary>
    public static void CleanupTempDir(string path)
    {
        if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
        if (!path.StartsWith(TestRoot, StringComparison.OrdinalIgnoreCase)) return;

        try
        {
            // Remove read-only attributes first
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var attrs = File.GetAttributes(file);
                if (attrs.HasFlag(FileAttributes.ReadOnly))
                    File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
            }
            Directory.Delete(path, recursive: true);
        }
        catch { /* best effort */ }
    }

    // ── Private creation methods ──────────────────────────────

    private static void CreateSmallFolder()
    {
        Directory.CreateDirectory(SmallFolder);
        for (int i = 1; i <= 10; i++)
        {
            var path = Path.Combine(SmallFolder, $"test{i}.txt");
            if (!File.Exists(path))
                File.WriteAllText(path, $"Test file content {i}");
        }
    }

    private static void CreateLargeFolder()
    {
        Directory.CreateDirectory(LargeFolder);
        for (int i = 1; i <= 1000; i++)
        {
            var path = Path.Combine(LargeFolder, $"file_{i:D4}.txt");
            if (!File.Exists(path))
                File.WriteAllText(path, $"Large folder file {i}");
        }
    }

    private static void CreateDeepFolder()
    {
        var current = DeepFolder;
        Directory.CreateDirectory(current);

        for (int level = 1; level <= 10; level++)
        {
            current = Path.Combine(current, $"level{level}");
            Directory.CreateDirectory(current);
        }

        var deepFile = Path.Combine(current, "deep.txt");
        if (!File.Exists(deepFile))
            File.WriteAllText(deepFile, "Deep nested file content");
    }

    private static void CreateMixedTypes()
    {
        Directory.CreateDirectory(MixedTypes);

        var files = new Dictionary<string, string>
        {
            ["image.png"] = "fake-png-content",
            ["doc.pdf"] = "fake-pdf-content",
            ["video.mp4"] = "fake-mp4-content",
            ["code.cs"] = "namespace Test { class Foo {} }",
            ["archive.zip"] = "fake-zip-content",
            ["data.json"] = "{\"key\": \"value\"}",
            ["readme.md"] = "# Readme",
            ["style.css"] = "body { color: red; }",
            ["script.js"] = "console.log('hello');",
            ["spreadsheet.xlsx"] = "fake-xlsx-content"
        };

        foreach (var (name, content) in files)
        {
            var path = Path.Combine(MixedTypes, name);
            if (!File.Exists(path))
                File.WriteAllText(path, content);
        }
    }

    private static void CreateHiddenFiles()
    {
        Directory.CreateDirectory(HiddenFiles);

        var visibleFile = Path.Combine(HiddenFiles, "visible.txt");
        if (!File.Exists(visibleFile))
            File.WriteAllText(visibleFile, "visible file");

        var hiddenFile = Path.Combine(HiddenFiles, ".hidden_file.txt");
        if (!File.Exists(hiddenFile))
        {
            File.WriteAllText(hiddenFile, "hidden file");
            File.SetAttributes(hiddenFile, File.GetAttributes(hiddenFile) | FileAttributes.Hidden);
        }

        var hiddenDir = Path.Combine(HiddenFiles, ".hidden_dir");
        if (!Directory.Exists(hiddenDir))
        {
            Directory.CreateDirectory(hiddenDir);
            File.SetAttributes(hiddenDir, File.GetAttributes(hiddenDir) | FileAttributes.Hidden);
            File.WriteAllText(Path.Combine(hiddenDir, "inside.txt"), "inside hidden dir");
        }
    }

    private static void CreateConflictTest()
    {
        Directory.CreateDirectory(ConflictTest);

        var files = new[] { "duplicate.txt", "duplicate (1).txt", "unique.txt" };
        foreach (var name in files)
        {
            var path = Path.Combine(ConflictTest, name);
            if (!File.Exists(path))
                File.WriteAllText(path, $"Content of {name}");
        }
    }

    private static void CreateReadOnlyFolder()
    {
        Directory.CreateDirectory(ReadOnlyFolder);

        var readOnlyFile = Path.Combine(ReadOnlyFolder, "readonly.txt");
        if (!File.Exists(readOnlyFile))
        {
            File.WriteAllText(readOnlyFile, "read only content");
            File.SetAttributes(readOnlyFile, File.GetAttributes(readOnlyFile) | FileAttributes.ReadOnly);
        }

        var normalFile = Path.Combine(ReadOnlyFolder, "normal.txt");
        if (!File.Exists(normalFile))
            File.WriteAllText(normalFile, "normal file in read-only folder");
    }

    private static void CreateUnicodeNames()
    {
        Directory.CreateDirectory(UnicodeNames);

        var files = new Dictionary<string, string>
        {
            ["한글파일.txt"] = "Korean content",
            ["日本語.txt"] = "Japanese content",
            ["émojis.txt"] = "File with accented name",
            ["中文文件.txt"] = "Chinese content",
            ["Ñoño.txt"] = "Spanish tilde content"
        };

        foreach (var (name, content) in files)
        {
            var path = Path.Combine(UnicodeNames, name);
            if (!File.Exists(path))
                File.WriteAllText(path, content);
        }
    }
}
