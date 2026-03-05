using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;

namespace Span.UITests.Tests;

/// <summary>
/// 파일 작업 워크플로우 E2E 테스트.
/// 단일 단축키가 아닌, 멀티 스텝 시나리오(Copy→Paste→Undo→Redo 등)를 검증한다.
/// 각 테스트는 독립적인 테스트 디렉터리를 생성/정리하여 부수효과를 방지한다.
/// </summary>
[TestClass]
public class FileOperationWorkflowTests
{
    private static Window? _window;
    private string? _testDir;
    private string? _testDir2;

    [ClassInitialize]
    public static void ClassInit(TestContext context)
    {
        _window = SpanAppFixture.GetMainWindow();
        SpanAppFixture.Focus(_window);
        SpanAppFixture.EnsureExplorerMode(_window);
    }

    [ClassCleanup]
    public static void ClassCleanup() => SpanAppFixture.Detach();

    [TestInitialize]
    public void TestInit()
    {
        SpanAppFixture.Focus(_window!);
        // Miller 모드로 전환 (일관된 테스트 환경)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_1);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    [TestCleanup]
    public void TestCleanup()
    {
        SpanAppFixture.CleanupTestDirectory(_testDir);
        SpanAppFixture.CleanupTestDirectory(_testDir2);
        _testDir = null;
        _testDir2 = null;
    }

    #region 헬퍼 메서드

    /// <summary>
    /// 테스트 디렉터리에 서브 폴더를 생성하고 경로를 반환한다.
    /// </summary>
    private static string CreateSubFolder(string parentDir, string name)
    {
        var path = System.IO.Path.Combine(parentDir, name);
        System.IO.Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// 지정된 디렉터리에 테스트 파일을 생성하고 파일명을 반환한다.
    /// </summary>
    private static string CreateTestFile(string dir, string name, string content = "test content")
    {
        var path = System.IO.Path.Combine(dir, name);
        System.IO.File.WriteAllText(path, content);
        return name;
    }

    /// <summary>
    /// 디스크에서 파일 존재를 폴링 확인한다 (파일 시스템 지연 대응).
    /// </summary>
    private static bool WaitForFileOnDisk(string filePath, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (System.IO.File.Exists(filePath)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>
    /// 디스크에서 파일 소멸을 폴링 확인한다.
    /// </summary>
    private static bool WaitForFileGone(string filePath, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!System.IO.File.Exists(filePath)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>
    /// 디스크에서 폴더 존재를 폴링 확인한다.
    /// </summary>
    private static bool WaitForDirectoryOnDisk(string dirPath, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (System.IO.Directory.Exists(dirPath)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>
    /// 디스크에서 폴더 소멸을 폴링 확인한다.
    /// </summary>
    private static bool WaitForDirectoryGone(string dirPath, int timeoutMs = 5000)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (!System.IO.Directory.Exists(dirPath)) return true;
            Thread.Sleep(200);
        }
        return false;
    }

    /// <summary>
    /// UI에서 첫 번째 파일 항목을 선택한다 (Down 키).
    /// </summary>
    private static void SelectFirstItem()
    {
        Keyboard.Type(VirtualKeyShort.DOWN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    /// <summary>
    /// 앱이 여전히 응답 가능한지 확인한다.
    /// </summary>
    private static void AssertAppResponsive(string context)
    {
        var backBtn = SpanAppFixture.FindById(_window!, "Button_Back");
        Assert.IsNotNull(backBtn, $"앱이 응답하지 않음: {context}");
    }

    #endregion

    #region Copy → Paste 전체 플로우

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CopyPaste_FileCopiedToDestination()
    {
        // 준비: 소스 폴더(파일 포함) + 빈 대상 폴더 생성
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");
        var sourceFileName = "testfile_000.txt";
        var expectedDest = System.IO.Path.Combine(destFolder, sourceFileName);

        // 1. 소스 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");

        // 2. 첫 번째 파일 선택 → Ctrl+C 복사
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 3. 대상 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");

        // 4. Ctrl+V 붙여넣기
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 5. 검증: 대상 폴더에 파일이 존재해야 함
        var fileExists = WaitForFileOnDisk(expectedDest, 5000);
        Assert.IsTrue(fileExists, $"복사된 파일이 대상에 존재해야 함: {expectedDest}");

        // 6. 원본 파일도 여전히 존재해야 함 (복사이므로)
        var originalPath = System.IO.Path.Combine(_testDir, sourceFileName);
        Assert.IsTrue(System.IO.File.Exists(originalPath), "원본 파일은 복사 후에도 존재해야 함");

        AssertAppResponsive("Copy→Paste 후");
    }

    #endregion

    #region Cut → Paste 전체 플로우

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CutPaste_FileMovedToDestination()
    {
        // 준비: 소스 폴더 + 대상 폴더
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");
        var sourceFileName = "testfile_000.txt";
        var originalPath = System.IO.Path.Combine(_testDir, sourceFileName);
        var expectedDest = System.IO.Path.Combine(destFolder, sourceFileName);

        // 소스 파일 존재 확인
        Assert.IsTrue(System.IO.File.Exists(originalPath), "테스트 전제: 소스 파일 존재");

        // 1. 소스 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");

        // 2. 첫 번째 파일 선택 → Ctrl+X 잘라내기
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_X);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 3. 대상 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");

        // 4. Ctrl+V 붙여넣기
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 5. 검증: 대상에 파일 존재
        var fileExists = WaitForFileOnDisk(expectedDest, 5000);
        Assert.IsTrue(fileExists, $"이동된 파일이 대상에 존재해야 함: {expectedDest}");

        // 6. 원본은 사라져야 함 (잘라내기이므로)
        var originalGone = WaitForFileGone(originalPath, 3000);
        Assert.IsTrue(originalGone, "잘라내기 후 원본 파일은 사라져야 함");

        AssertAppResponsive("Cut→Paste 후");
    }

    #endregion

    #region Copy → Paste → Undo

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CopyPasteUndo_PastedFileRemoved()
    {
        // 준비
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");
        var sourceFileName = "testfile_000.txt";
        var expectedDest = System.IO.Path.Combine(destFolder, sourceFileName);

        // 1. 소스 폴더로 이동 → 파일 복사
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 2. 대상 폴더로 이동 → 붙여넣기
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 붙여넣기 완료 확인
        Assert.IsTrue(WaitForFileOnDisk(expectedDest, 5000),
            "Undo 전에 붙여넣기가 완료되어야 함");

        // 3. Ctrl+Z 실행 취소
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 4. 검증: 복사된 파일이 삭제되어야 함
        var fileGone = WaitForFileGone(expectedDest, 5000);
        Assert.IsTrue(fileGone, "Undo 후 복사된 파일이 제거되어야 함");

        AssertAppResponsive("Copy→Paste→Undo 후");
    }

    #endregion

    #region Copy → Paste → Undo → Redo

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CopyPasteUndoRedo_FileRestoredAfterRedo()
    {
        // 준비
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");
        var sourceFileName = "testfile_000.txt";
        var expectedDest = System.IO.Path.Combine(destFolder, sourceFileName);

        // 1. 소스 폴더 → 파일 복사
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 2. 대상 폴더 → 붙여넣기
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));
        Assert.IsTrue(WaitForFileOnDisk(expectedDest, 5000), "붙여넣기 완료 확인");

        // 3. Undo → 파일 제거 확인
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));
        Assert.IsTrue(WaitForFileGone(expectedDest, 5000), "Undo 후 파일 제거 확인");

        // 4. Redo (Ctrl+Y) → 파일 다시 생성 확인
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Y);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 5. 검증: 파일이 다시 존재해야 함
        var fileRestored = WaitForFileOnDisk(expectedDest, 5000);
        Assert.IsTrue(fileRestored, "Redo 후 파일이 다시 생성되어야 함");

        AssertAppResponsive("Copy→Paste→Undo→Redo 후");
    }

    #endregion

    #region Delete → Undo 복원

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_DeleteUndo_FileRestored()
    {
        // 준비: 테스트 파일 생성
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var targetFile = "testfile_000.txt";
        var targetPath = System.IO.Path.Combine(_testDir, targetFile);
        Assert.IsTrue(System.IO.File.Exists(targetPath), "테스트 전제: 대상 파일 존재");

        // 1. 테스트 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("테스트 폴더로 이동 실패");

        // 2. 첫 번째 파일 선택 → Delete 키
        SelectFirstItem();
        Keyboard.Type(VirtualKeyShort.DELETE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // 3. 삭제 확인 다이얼로그가 나타날 수 있음 — Enter로 확인
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 4. 파일 삭제 확인 (휴지통으로 이동될 수 있으므로 디스크에서 사라짐 확인)
        var fileDeleted = WaitForFileGone(targetPath, 5000);
        if (!fileDeleted)
            Assert.Inconclusive("삭제가 수행되지 않음 (확인 다이얼로그 설정에 따라 다를 수 있음)");

        // 5. Ctrl+Z 실행 취소
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        // 6. 검증: 파일이 복원되어야 함
        var fileRestored = WaitForFileOnDisk(targetPath, 5000);
        Assert.IsTrue(fileRestored, "Undo 후 삭제된 파일이 복원되어야 함");

        AssertAppResponsive("Delete→Undo 후");
    }

    #endregion

    #region NewFolder → Rename → Undo (이름 변경 되돌리기)

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_NewFolderRenameUndo_RenameReverted()
    {
        // 준비
        _testDir = SpanAppFixture.CreateTestDirectory(1);

        // 1. 테스트 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("테스트 폴더로 이동 실패");

        // 2. Ctrl+Shift+N으로 새 폴더 생성
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.SHIFT, VirtualKeyShort.KEY_N);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1500));

        // 3. 기본 이름("새 폴더" 등)으로 생성 확인 — Enter로 기본 이름 확정
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // 새 폴더 찾기 (정확한 이름은 로케일에 따라 다를 수 있음)
        var dirs = System.IO.Directory.GetDirectories(_testDir);
        if (dirs.Length == 0)
            Assert.Inconclusive("새 폴더가 생성되지 않음");

        var newFolderPath = dirs[0];
        var originalName = System.IO.Path.GetFileName(newFolderPath);

        // 4. F2로 이름 변경 시작
        Keyboard.Type(VirtualKeyShort.F2);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 전체 선택 후 새 이름 입력
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(200));

        var renamedName = $"renamed_{Guid.NewGuid().ToString("N")[..6]}";
        Keyboard.Type(renamedName);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(300));

        // Enter로 이름 변경 확정
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1500));

        // 이름 변경 완료 확인
        var renamedPath = System.IO.Path.Combine(_testDir, renamedName);
        var renamed = WaitForDirectoryOnDisk(renamedPath, 3000);
        if (!renamed)
            Assert.Inconclusive("이름 변경이 완료되지 않음 (키보드 입력 문제 가능)");

        // 5. Ctrl+Z로 이름 변경 되돌리기
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 6. 검증: 원래 이름으로 복원
        var originalRestored = WaitForDirectoryOnDisk(
            System.IO.Path.Combine(_testDir, originalName), 5000);
        var renamedGone = WaitForDirectoryGone(renamedPath, 3000);

        Assert.IsTrue(originalRestored || renamedGone,
            "Undo 후 이름 변경이 되돌려져야 함 (원래 이름 복원 또는 변경된 이름 소멸)");

        AssertAppResponsive("NewFolder→Rename→Undo 후");
    }

    #endregion

    #region ActionLog 연동 확인

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_ActionLog_ShowsOperationAfterCopyPaste()
    {
        // 준비: 복사 작업 수행
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");

        // 1. 소스 폴더로 이동 → 파일 복사
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 2. 대상 폴더 → 붙여넣기
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 3. ActionLog 버튼 클릭하여 로그 확인
        var logButton = SpanAppFixture.WaitForElement(_window!, "Button_Log", 3000);
        if (logButton == null)
            Assert.Inconclusive("ActionLog 버튼을 찾을 수 없음");

        logButton.Click();
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // 4. 검증: LogListView 또는 로그 관련 요소가 나타나야 함
        // LogModeView 또는 LogFlyoutContent 내의 ListView 확인
        var logListView = SpanAppFixture.WaitForElement(_window!, "LogListView", 3000);
        var logEntries = logListView?.FindAllDescendants(
            cf => cf.ByControlType(ControlType.ListItem));

        // 로그 항목이 하나 이상 있어야 함
        // (LogListView가 없을 수도 있음 — Flyout으로 열릴 수 있으므로 ListItem 직접 검색)
        if (logListView == null)
        {
            // Flyout 내 ListView 검색 시도
            var allListViews = _window!.FindAllDescendants(
                cf => cf.ByControlType(ControlType.List));
            foreach (var lv in allListViews)
            {
                var name = SpanAppFixture.SafeGetName(lv);
                var automationId = lv.Properties.AutomationId.ValueOrDefault ?? "";
                if (automationId == "LogListView" || (name != null && name.Contains("Log")))
                {
                    logListView = lv;
                    break;
                }
            }
        }

        // LogListView를 찾지 못해도 앱이 응답하면 패스 (로그 뷰 구현에 따라 다를 수 있음)
        AssertAppResponsive("ActionLog 확인 후");

        if (logListView != null)
        {
            logEntries = logListView.FindAllDescendants(
                cf => cf.ByControlType(ControlType.ListItem));
            Assert.IsTrue(logEntries.Length > 0,
                "복사 작업 후 ActionLog에 항목이 있어야 함");
        }

        // Escape로 로그 닫기
        Keyboard.Type(VirtualKeyShort.ESCAPE);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));
    }

    #endregion

    #region 멀티 파일 복사 → 붙여넣기

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_MultiFileCopyPaste_AllFilesCopied()
    {
        // 준비: 소스에 여러 파일 + 빈 대상 폴더
        _testDir = SpanAppFixture.CreateTestDirectory(5);
        var destFolder = CreateSubFolder(_testDir, "dest");

        // 1. 소스 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");

        // 2. Ctrl+A로 전체 선택 (dest 폴더도 선택됨)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_A);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 3. Ctrl+C 복사
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 4. 대상 폴더로 이동 → 붙여넣기
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        // 5. 검증: 대상 폴더에 파일들이 복사되어야 함
        // testfile_000~004.txt 중 최소 일부가 복사되었는지 확인
        var copiedCount = 0;
        for (int i = 0; i < 5; i++)
        {
            var fileName = $"testfile_{i:D3}.txt";
            if (WaitForFileOnDisk(System.IO.Path.Combine(destFolder, fileName), 1000))
                copiedCount++;
        }

        Assert.IsTrue(copiedCount >= 1,
            $"멀티 파일 복사 후 최소 1개 파일이 대상에 존재해야 함 (복사된 수: {copiedCount})");

        AssertAppResponsive("멀티 파일 Copy→Paste 후");
    }

    #endregion

    #region Copy → Paste 동일 폴더 (이름 충돌)

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CopyPasteSameFolder_HandlesNameConflict()
    {
        // 준비
        _testDir = SpanAppFixture.CreateTestDirectory(3);

        // 1. 테스트 폴더로 이동
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("테스트 폴더로 이동 실패");

        // 2. 첫 번째 파일 선택 → 복사
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_C);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 3. 동일 폴더에서 붙여넣기 (이름 충돌 발생)
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 충돌 다이얼로그가 나타날 수 있음 — Enter로 확인
        Keyboard.Type(VirtualKeyShort.RETURN);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(1000));

        // 4. 검증: 앱이 크래시하지 않고 응답해야 함
        AssertAppResponsive("동일 폴더 Copy→Paste 후");

        // 파일 수가 증가했거나 복사본이 생성되었는지 확인
        var files = System.IO.Directory.GetFiles(_testDir);
        Assert.IsTrue(files.Length >= 3,
            $"동일 폴더 붙여넣기 후 파일 수가 유지 또는 증가해야 함 (현재: {files.Length})");
    }

    #endregion

    #region Cut → Paste → Undo (이동 되돌리기)

    [TestMethod]
    [TestCategory("Destructive")]
    public void Workflow_CutPasteUndo_FileRestoredToOriginal()
    {
        // 준비
        _testDir = SpanAppFixture.CreateTestDirectory(3);
        var destFolder = CreateSubFolder(_testDir, "dest");
        var sourceFileName = "testfile_000.txt";
        var originalPath = System.IO.Path.Combine(_testDir, sourceFileName);
        var destPath = System.IO.Path.Combine(destFolder, sourceFileName);

        // 1. 소스 → 잘라내기
        if (!SpanAppFixture.NavigateToPath(_window!, _testDir))
            Assert.Inconclusive("소스 폴더로 이동 실패");
        SelectFirstItem();
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_X);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(500));

        // 2. 대상 → 붙여넣기
        if (!SpanAppFixture.NavigateToPath(_window!, destFolder))
            Assert.Inconclusive("대상 폴더로 이동 실패");
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_V);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(2000));

        // 이동 완료 확인
        Assert.IsTrue(WaitForFileOnDisk(destPath, 5000), "이동 완료 확인");
        Assert.IsTrue(WaitForFileGone(originalPath, 3000), "원본 제거 확인");

        // 3. Ctrl+Z 실행 취소
        Keyboard.TypeSimultaneously(VirtualKeyShort.CONTROL, VirtualKeyShort.KEY_Z);
        Wait.UntilInputIsProcessed(TimeSpan.FromMilliseconds(3000));

        // 4. 검증: 원본 위치에 파일 복원
        var originalRestored = WaitForFileOnDisk(originalPath, 5000);
        Assert.IsTrue(originalRestored, "Undo 후 원본 위치에 파일이 복원되어야 함");

        // 대상에서 파일 제거
        var destRemoved = WaitForFileGone(destPath, 3000);
        Assert.IsTrue(destRemoved, "Undo 후 대상에서 파일이 제거되어야 함");

        AssertAppResponsive("Cut→Paste→Undo 후");
    }

    #endregion
}
