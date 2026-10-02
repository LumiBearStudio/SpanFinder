using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class LaunchArgumentsTests
{
    private const string Alias = @"C:\Users\u\AppData\Local\Microsoft\WindowsApps\spanfinder.exe";
    private const string PackagedExe = @"C:\Program Files\WindowsApps\LumiBearStudio.SPANFinder_2.0.7.0_x64__zv1cz7a6mb9x2\Span.exe";
    private const string DevLayoutExe = @"D:\Dev\Span\src\Span\Span\bin\x64\Debug\net8.0-windows10.0.19041.0\AppX\Span.exe";
    private const string PackageFolder = @"C:\Program Files\WindowsApps\LumiBearStudio.SPANFinder_2.0.7.0_x64__zv1cz7a6mb9x2";

    private static readonly HashSet<string> Dirs = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\", @"D:\Projects", @"D:\My Folder", PackageFolder,
    };

    private static readonly HashSet<string> Files = new(StringComparer.OrdinalIgnoreCase)
    {
        @"D:\Projects\a.txt", Alias, PackagedExe, DevLayoutExe,
    };

    private static LaunchRequest Parse(string? raw) => LaunchArguments.Parse(raw, Dirs.Contains, Files.Contains);

    // -------------------------------------------------------
    // 앱 실행 별칭 / 기본 파일 관리자 레지스트리 ("…\spanfinder.exe" "%1")
    // -------------------------------------------------------

    [TestMethod]
    public void Alias_WithFolder_ReturnsFolder()
    {
        var r = Parse($"\"{Alias}\" \"D:\\Projects\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"D:\Projects", r.Path);
    }

    [TestMethod]
    public void Alias_WithoutArguments_ReturnsNone()
    {
        // 예전에는 원본 문자열을 돌려줘서 WindowsApps 폴더가 열리고 spanfinder.exe가 선택됐다
        Assert.AreEqual(LaunchKind.None, Parse($"\"{Alias}\" ").Kind);
    }

    [TestMethod]
    public void PackagedOrDevExe_WithoutArguments_ReturnsNone()
    {
        Assert.AreEqual(LaunchKind.None, Parse($"\"{PackagedExe}\"").Kind);
        Assert.AreEqual(LaunchKind.None, Parse($"\"{DevLayoutExe}\"").Kind); // 경로 패턴으로는 안 걸러지는 개발 레이아웃
    }

    [TestMethod]
    public void ManagedDllArgv0_IsDropped()
    {
        // .NET apphost 앱에서 Environment.CommandLine은 argv0으로 Span.dll 경로를 준다(실측) — 파일 인자로 읽히면 안 된다
        const string dll = @"D:\Dev\Span\src\Span\Span\bin\x64\Debug\net8.0-windows10.0.19041.0\AppX\Span.dll";
        var files = new HashSet<string>(Files, StringComparer.OrdinalIgnoreCase) { dll };

        Assert.AreEqual(LaunchKind.None, LaunchArguments.Parse(dll, Dirs.Contains, files.Contains).Kind);
    }

    [TestMethod]
    public void PackagedExe_WithFolder_ReturnsFolder()
    {
        var r = Parse($"\"{PackagedExe}\" \"D:\\Projects\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"D:\Projects", r.Path);
    }

    [TestMethod]
    public void QuotedDriveRoot_IsNotBrokenByBackslashQuote()
    {
        // CommandLineToArgvW / .NET 인자 분리는 "C:\" 를 C:" 로 깨뜨린다
        var r = Parse($"\"{Alias}\" \"C:\\\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"C:\", r.Path);
    }

    [TestMethod]
    public void PackageFolderArgument_IsIgnored()
    {
        // 기본 파일 관리자 상태에서 작업 표시줄 탐색기 아이콘을 누르면 패키지 폴더가 인자로 오는 경우
        Assert.AreEqual(LaunchKind.None, Parse($"\"{Alias}\" \"{PackageFolder}\"").Kind);
    }

    [TestMethod]
    public void LastExistingArgument_Wins()
    {
        var r = Parse($"\"{Alias}\" \"D:\\Projects\" \"D:\\Projects\\a.txt\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"D:\Projects\a.txt", r.Path);
    }

    [TestMethod]
    public void NonexistentPath_ReturnsNone()
    {
        Assert.AreEqual(LaunchKind.None, Parse($"\"{Alias}\" \"Z:\\gone\"").Kind);
    }

    [TestMethod]
    public void EmptyInput_ReturnsNone()
    {
        Assert.AreEqual(LaunchKind.None, Parse(null).Kind);
        Assert.AreEqual(LaunchKind.None, Parse("").Kind);
        Assert.AreEqual(LaunchKind.None, Parse("   ").Kind);
    }

    // -------------------------------------------------------
    // 점프 목록 (argv0 없음, 따옴표 없는 경로)
    // -------------------------------------------------------

    [TestMethod]
    public void JumpList_UnquotedPathWithSpaces_ReturnsWholePath()
    {
        var r = Parse(@"D:\My Folder");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"D:\My Folder", r.Path);
    }

    [TestMethod]
    public void Alias_WithUnquotedPathWithSpaces_ReturnsWholePath()
    {
        var r = Parse($"\"{Alias}\" D:\\My Folder");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual(@"D:\My Folder", r.Path);
    }

    [TestMethod]
    public void NewWindow_IsRecognized_WithOrWithoutArgv0()
    {
        Assert.AreEqual(LaunchKind.NewWindow, Parse("--new-window").Kind);
        Assert.AreEqual(LaunchKind.NewWindow, Parse($"\"{Alias}\" --new-window").Kind);
        Assert.IsTrue(LaunchArguments.IsNewWindow("\"--new-window\""));
        Assert.IsFalse(LaunchArguments.IsNewWindow(@"D:\Projects"));
    }

    // -------------------------------------------------------
    // --select
    // -------------------------------------------------------

    [TestMethod]
    public void Select_File()
    {
        var r = Parse($"\"{PackagedExe}\" --select \"D:\\Projects\\a.txt\"");

        Assert.AreEqual(LaunchKind.Select, r.Kind);
        Assert.AreEqual(@"D:\Projects\a.txt", r.Path);
    }

    [TestMethod]
    public void Select_Folder_IsSelectNotOpen()
    {
        // 폴더여도 Path(열기)가 아니라 Select(부모에서 선택)여야 한다
        var r = Parse("--select \"D:\\Projects\"");

        Assert.AreEqual(LaunchKind.Select, r.Kind);
        Assert.AreEqual(@"D:\Projects", r.Path);
    }

    [TestMethod]
    public void Select_WithoutPath_ReturnsNone()
    {
        Assert.AreEqual(LaunchKind.None, Parse("--select").Kind);
        Assert.AreEqual(LaunchKind.None, Parse("--select \"\"").Kind);
    }

    [TestMethod]
    public void Select_ShellNamespacePath_ReturnsNone()
    {
        // 선택은 파일 시스템 항목에만 — 콜드 스타트와 리다이렉트의 처리가 갈리지 않게 아예 받지 않는다
        Assert.AreEqual(LaunchKind.None, Parse("--select \"::{26EE0668-A00A-44D7-9371-BEB064C98683}\"").Kind);
        Assert.AreEqual(LaunchKind.None, Parse("--select \"shell:RecycleBinFolder\"").Kind);
        Assert.AreEqual(LaunchKind.None, Parse("--select \"shell:downloads\"").Kind);
    }

    // -------------------------------------------------------
    // 셸 가상 폴더
    // -------------------------------------------------------

    [TestMethod]
    public void ShellRecycleBin_ReturnsPath()
    {
        var r = Parse($"\"{Alias}\" \"shell:RecycleBinFolder\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual("shell:RecycleBinFolder", r.Path);
        Assert.IsTrue(LaunchArguments.IsRecycleBin(r.Path));
    }

    [TestMethod]
    public void RecycleBinClsid_IsVirtualFolder()
    {
        // 기존 동작 유지: CLSID 형태의 휴지통은 explorer.exe에 위임된다
        var r = Parse("\"::{645FF040-5081-101B-9F08-00AA002F954E}\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.IsTrue(LaunchArguments.IsVirtualFolder(r.Path));
    }

    [TestMethod]
    public void ThisPC_ReturnsPath_AndIsNotVirtualFolder()
    {
        var r = Parse($"\"{Alias}\" \"::{{20D04FE0-3AEA-1069-A2D8-08002B30309D}}\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.IsTrue(LaunchArguments.IsThisPC(r.Path));
        Assert.IsFalse(LaunchArguments.IsVirtualFolder(r.Path));
    }

    [TestMethod]
    public void ControlPanelClsid_TrailingNul_IsTrimmed()
    {
        var r = Parse("\"::{26EE0668-A00A-44D7-9371-BEB064C98683}\0\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual("::{26EE0668-A00A-44D7-9371-BEB064C98683}", r.Path);
        Assert.IsTrue(LaunchArguments.IsVirtualFolder(r.Path));
    }

    [TestMethod]
    public void ShellKnownFolder_WithArgv0_ReturnsShellPath()
    {
        // 예전에는 원본 문자열 전체("…spanfinder.exe" "shell:downloads")가 돌아와 아무 일도 일어나지 않았다
        var r = Parse($"\"{Alias}\" \"shell:downloads\"");

        Assert.AreEqual(LaunchKind.Path, r.Kind);
        Assert.AreEqual("shell:downloads", r.Path);
    }

    [TestMethod]
    public void SystemPackagePath_Rules()
    {
        Assert.IsTrue(LaunchArguments.IsSystemPackagePath(Alias));
        Assert.IsTrue(LaunchArguments.IsSystemPackagePath(PackageFolder));
        Assert.IsFalse(LaunchArguments.IsSystemPackagePath(@"D:\Projects"));
        Assert.IsFalse(LaunchArguments.IsSystemPackagePath(null));
    }
}
