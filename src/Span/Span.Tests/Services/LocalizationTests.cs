using Span.Services;

namespace Span.Tests.Services;

/// <summary>
/// LocalizationService 무결성 및 다국어 번역 완전성 검증 테스트.
/// LocalizationService는 WinUI 의존성(Windows.Globalization)이 있어 직접 인스턴스화할 수 없으므로,
/// LocalizationService.Entries 튜플 배열을 직접 검증한다.
/// </summary>
[TestClass]
public class LocalizationTests
{
    // Entries 접근을 위한 헬퍼 — LocalizationService는 partial class이며 Entries는 static
    private static readonly (string key, string en, string ko, string ja, string zhHans, string zhHant, string de, string es, string fr, string ptBR)[] Entries
        = LocalizationService.Entries;

    private static readonly string[] LangNames = { "en", "ko", "ja", "zh-Hans", "zh-Hant", "de", "es", "fr", "pt-BR" };

    // ── Data Integrity ──

    [TestMethod]
    public void AllEntries_HaveUniqueKeys()
    {
        var keys = Entries.Select(e => e.key).ToList();
        var duplicates = keys.GroupBy(k => k).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        Assert.AreEqual(0, duplicates.Count,
            $"Duplicate keys found: {string.Join(", ", duplicates)}");
    }

    [TestMethod]
    public void AllEntries_HaveNonEmptyKey()
    {
        foreach (var e in Entries)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(e.key),
                "Found entry with empty key");
        }
    }

    [TestMethod]
    public void AllEntries_HaveNonEmptyEnglish()
    {
        var empty = Entries.Where(e => string.IsNullOrWhiteSpace(e.en)).Select(e => e.key).ToList();
        Assert.AreEqual(0, empty.Count,
            $"Entries with empty English text: {string.Join(", ", empty)}");
    }

    [TestMethod]
    public void AllEntries_HaveAllLanguagesFilled()
    {
        var missing = new List<string>();
        foreach (var e in Entries)
        {
            var fields = new[] { e.en, e.ko, e.ja, e.zhHans, e.zhHant, e.de, e.es, e.fr, e.ptBR };
            for (int i = 0; i < fields.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(fields[i]))
                    missing.Add($"{e.key}[{LangNames[i]}]");
            }
        }
        Assert.AreEqual(0, missing.Count,
            $"Missing translations ({missing.Count}): {string.Join(", ", missing.Take(20))}");
    }

    // ── Toast Keys ──

    [TestMethod]
    public void ToastKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Toast_PathCopied", "Toast_Copied", "Toast_CopiedMultiple",
            "Toast_Cut", "Toast_CutMultiple", "Toast_ShortcutsCreated",
            "Toast_Duplicated", "Toast_DuplicatedMultiple",
            "Toast_Connected", "Toast_AuthFailed", "Toast_ConnectionNotFound",
            "Toast_SocketError", "Toast_TimeoutError", "Toast_ConnectionError",
            "Toast_Undone", "Toast_UndoFailed", "Toast_Redone", "Toast_RedoFailed",
            "Toast_Completed", "Toast_CompletedUndo", "Toast_OperationFailed",
            "Toast_HiddenFilesShown", "Toast_HiddenFilesHidden",
            "Toast_ColumnsEqualized", "Toast_ColumnsAutoFit"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing toast keys: {string.Join(", ", missing)}");
    }

    // ── Operation Keys ──

    [TestMethod]
    public void OperationKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Op_CopySingle", "Op_CopyMultiple", "Op_MoveSingle", "Op_MoveMultiple",
            "Op_DeleteSingle", "Op_DeleteMultiple", "Op_Rename",
            "Op_Cancelled_Copy", "Op_Cancelled_Move", "Op_Cancelled_Delete", "Op_Cancelled_Rename",
            "Op_FailedTo_Copy", "Op_FailedTo_Move", "Op_FailedTo_Delete", "Op_FailedTo_Rename",
            "Op_SomeNotCopied", "Op_SomeNotMoved", "Op_SomeNotDeleted",
            "Op_UnexpectedError", "Op_UnexpectedErrorUndo",
            "Op_PermanentDeleteSingle", "Op_PermanentDeleteMultiple", "Op_CannotUndoPermanent",
            "Op_PathNotFound", "Op_PathTooLong"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing operation keys: {string.Join(", ", missing)}");
    }

    // ── Error Keys ──

    [TestMethod]
    public void ErrorKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Error_FolderNotFound", "Error_NetworkPath", "Error_PathTooLong",
            "Error_ConnectionNotFound", "Error_Timeout", "Error_AuthFailed",
            "Error_Disconnected", "Error_CannotConnect", "Error_AccessDenied",
            "Error_ConnectionFailed", "Error_RemoteGeneric",
            "Error_TerminalInvalidPath", "Error_PathNotExist"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing error keys: {string.Join(", ", missing)}");
    }

    // ── Dialog Button Keys ──

    [TestMethod]
    public void DialogButtonKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "OK", "Cancel", "Delete", "Save", "Connect", "Register",
            "PermanentDelete", "Copy", "Move", "Rename"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing dialog button keys: {string.Join(", ", missing)}");
    }

    // ── Status Bar Keys ──

    [TestMethod]
    public void StatusBarKeys_AllExist()
    {
        var requiredKeys = new[] { "StatusItemCount", "StatusSelected" };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing status bar keys: {string.Join(", ", missing)}");
    }

    // ── Log / Filter Keys ──

    [TestMethod]
    public void LogKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Log_Title", "Log_Clear", "Log_Empty",
            "FilterAll", "FilterError",
            "LogUndo", "LogRedo"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing log keys: {string.Join(", ", missing)}");
    }

    // ── Sidebar / Known Folder Keys ──

    [TestMethod]
    public void SidebarKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Home", "Favorites", "LocalDrives", "Cloud", "Network",
            "DevicesAndDrives", "CloudStorage", "NetworkLocations",
            "KnownFolder_Desktop", "KnownFolder_Downloads",
            "KnownFolder_Documents", "KnownFolder_Pictures",
            "KnownFolder_Music", "KnownFolder_Videos"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing sidebar/known-folder keys: {string.Join(", ", missing)}");
    }

    // ── Settings Keys ──

    [TestMethod]
    public void SettingsKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Settings", "Settings_General", "Settings_Appearance",
            "Settings_Browsing", "Settings_Tools", "Settings_Advanced",
            "Settings_About", "Settings_OpenSourceNav"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing settings keys: {string.Join(", ", missing)}");
    }

    // ── Tab Header Keys ──

    [TestMethod]
    public void TabHeaderKeys_AllExist()
    {
        // Tab header localization에 필요한 키
        var requiredKeys = new[] { "Home", "Settings", "Log_Title" };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing tab header keys: {string.Join(", ", missing)}");
    }

    // ── Format String Placeholders ──

    [TestMethod]
    public void FormatStrings_HaveConsistentPlaceholders()
    {
        // {0}, {1} 등 포맷 플레이스홀더가 모든 언어에서 동일한지 검증
        var issues = new List<string>();

        foreach (var e in Entries)
        {
            var fields = new[] { e.en, e.ko, e.ja, e.zhHans, e.zhHant, e.de, e.es, e.fr, e.ptBR };
            var enPlaceholders = CountPlaceholders(e.en);
            if (enPlaceholders == 0) continue;

            for (int i = 1; i < fields.Length; i++)
            {
                var count = CountPlaceholders(fields[i]);
                if (count != enPlaceholders)
                {
                    issues.Add($"{e.key}[{LangNames[i]}]: expected {enPlaceholders} placeholders, found {count}");
                }
            }
        }

        Assert.AreEqual(0, issues.Count,
            $"Placeholder mismatches ({issues.Count}):\n{string.Join("\n", issues.Take(20))}");
    }

    // ── German Translation Spot-Check ──

    [TestMethod]
    public void German_ToastTranslations_AreNotEnglish()
    {
        // 독일어 번역이 영어와 동일하지 않은지 (번역 누락 감지)
        var toastEntries = Entries.Where(e => e.key.StartsWith("Toast_")).ToList();
        var sameAsEnglish = toastEntries
            .Where(e => e.de == e.en && e.en.Length > 3) // OK, 짧은 건 동일할 수 있음
            .Select(e => e.key)
            .ToList();

        Assert.AreEqual(0, sameAsEnglish.Count,
            $"German translations identical to English: {string.Join(", ", sameAsEnglish)}");
    }

    [TestMethod]
    public void Korean_ToastTranslations_AreNotEnglish()
    {
        var toastEntries = Entries.Where(e => e.key.StartsWith("Toast_")).ToList();
        var sameAsEnglish = toastEntries
            .Where(e => e.ko == e.en && e.en.Length > 3)
            .Select(e => e.key)
            .ToList();

        Assert.AreEqual(0, sameAsEnglish.Count,
            $"Korean translations identical to English: {string.Join(", ", sameAsEnglish)}");
    }

    // ── View Mode / Tooltip Keys ──

    [TestMethod]
    public void ViewModeKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "MillerColumns", "Details", "ViewMode_List", "Icons",
            "ExtraLargeIcons", "LargeIcons", "MediumIcons", "SmallIcons"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing view mode keys: {string.Join(", ", missing)}");
    }

    [TestMethod]
    public void TooltipKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Tooltip_NewTab", "Tooltip_Back", "Tooltip_Forward", "Tooltip_Up",
            "Tooltip_CopyPath", "Tooltip_NewFolder", "Tooltip_NewFile",
            "Tooltip_Cut", "Tooltip_Copy", "Tooltip_Paste",
            "Tooltip_Rename", "Tooltip_Delete", "Tooltip_Sort",
            "Tooltip_SplitView", "Tooltip_Preview",
            "Tooltip_Help", "Tooltip_Log", "Tooltip_Settings"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing tooltip keys: {string.Join(", ", missing)}");
    }

    // ── Sort / Group Keys ──

    [TestMethod]
    public void SortGroupKeys_AllExist()
    {
        var requiredKeys = new[]
        {
            "Name", "Date", "Size", "Type",
            "Ascending", "Descending",
            "GroupBy", "None"
        };

        var keys = new HashSet<string>(Entries.Select(e => e.key));
        var missing = requiredKeys.Where(k => !keys.Contains(k)).ToList();

        Assert.AreEqual(0, missing.Count,
            $"Missing sort/group keys: {string.Join(", ", missing)}");
    }

    // ── Total Entry Count Sanity Check ──

    [TestMethod]
    public void TotalEntryCount_IsReasonable()
    {
        // 현재 ~470개, 최소 450개 이상이어야 함
        Assert.IsTrue(Entries.Length >= 450,
            $"Only {Entries.Length} entries found — expected at least 500");
    }

    // ── Helper ──

    private static int CountPlaceholders(string text)
    {
        int count = 0;
        for (int i = 0; i <= 9; i++)
        {
            if (text.Contains($"{{{i}}}"))
                count++;
        }
        return count;
    }
}
