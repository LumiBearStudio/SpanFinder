using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Span.Helpers;

/// <summary>
/// Issue #72: 이미지를 탐색기처럼 "폴더 보기 문맥"과 함께 열어, 사진 앱에서 ←/→로 같은 폴더의
/// 다른 사진으로 넘어갈 수 있게 한다.
///
/// 탐색기는 "열기" 명령에 자기 폴더 보기(DefView)를 site로 붙인다. 사진 앱의 기본 열기 처리기
/// (DelegateExecute {BFEC0C93-…}, windows.system.launcher.dll)는 site에서 SID_SFolderView →
/// IFolderView2를 얻어 폴더·정렬·선택 위치를 열 파일 항목에 숨겨 붙이고, 사진 앱은 그것으로 이웃
/// 목록을 만든다. 경로만 넘기는 ShellExecute에는 site가 없어 사진 한 장만 열렸다.
/// 여기서는 SPAN이 그 폴더 보기를 흉내 낸 최소 site를 SEE_MASK_FLAG_HINST_IS_SITE(공식 플래그)로 넘긴다.
///
/// 실측(Windows 11 26200, 사진 앱 2026.11090): 일반·다운로드(MOTW) 사진 모두 이동되고 확인 창 없음
/// (파일 활성화 경로라 사진 앱의 "신뢰할 수 있는 출처" 검사가 없다), 넘긴 정렬 순서를 따름,
/// SPAN 패키지 안에서도 동작. ms-photos:viewer URI는 다운로드 사진에서 확인 창이 떠 쓰지 않는다.
///
/// 처리기가 site를 읽는 방식은 문서화되지 않았다. Windows가 바꾸면 처리기는 원래 선택으로 열어
/// 지금처럼 사진 한 장만 보인다(안전한 실패). 로그의 CallMask가 0이면 그 경우다.
/// </summary>
internal static class FolderContextLauncher
{
    private const string PhotosAppUserModelId = "Microsoft.Windows.Photos_8wekyb3d8bbwe!App";

    /// <summary>"Packaged CWA File Type Association Execute Command" — site를 읽는 사진 앱의 기본 열기 처리기.</summary>
    private const string LauncherDelegateExecuteClsid = "{BFEC0C93-0B7D-4F2C-B09C-AFFFC4BDAE78}";

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".bmp", ".dib", ".tif", ".tiff", ".webp",
        ".heic", ".heif", ".avif", ".ico", ".jxr", ".wdp",
        ".dng", ".arw", ".cr2", ".cr3", ".nef", ".nrw", ".orf", ".rw2", ".raf", ".srw", ".pef",
    };

    /// <summary>
    /// 경로 형태만 본다(디스크·레지스트리 접근 없음): 드라이브 문자로 시작하는 로컬 경로의 이미지 파일.
    /// UNC·압축 내부·원격(ftp/sftp)은 제외.
    /// </summary>
    public static bool IsCandidatePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || ArchivePathHelper.IsArchivePath(path)) return false;
        if (path.Length < 3 || !char.IsAsciiLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) return false;
        return ImageExtensions.Contains(Path.GetExtension(path));
    }

    /// <summary>
    /// 이 PC에서 새 방식으로 열 대상인지: <see cref="IsCandidatePath"/> + 고정·이동식 드라이브 +
    /// 기본 앱이 사진 앱이고 그 열기 처리기가 site를 읽는 런처일 때. 아니면 기존 열기를 쓴다.
    /// </summary>
    public static bool IsApplicable(string? path)
    {
        if (!IsCandidatePath(path)) return false;
        try
        {
            var driveType = new DriveInfo(path!.Substring(0, 1)).DriveType;
            if (driveType != DriveType.Fixed && driveType != DriveType.Removable) return false;

            var ext = Path.GetExtension(path);
            return string.Equals(QueryAssociation(AssocStrAppId, ext, null), PhotosAppUserModelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(QueryAssociation(AssocStrDelegateExecute, ext, "open"), LauncherDelegateExecuteClsid, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[FolderContextLauncher] applicability check failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// SPAN의 정렬(FolderViewModel.CurrentSortBy 값)을 Windows 속성 정렬로 바꾼다. 알 수 없으면 정렬 없음
    /// (사진 앱 기본 순서 = 이름순).
    /// </summary>
    public static SortColumn[] BuildSortColumns(string? sortBy, bool ascending)
    {
        PropertyKey? key = sortBy switch
        {
            "Name" => PropertyKey.ItemNameDisplay,
            "DateModified" => PropertyKey.DateModified,
            "Size" => PropertyKey.Size,
            "Type" => PropertyKey.ItemTypeText,
            _ => null,
        };
        return key is { } k
            ? new[] { new SortColumn { Key = k, Direction = ascending ? 1 : -1 } }
            : Array.Empty<SortColumn>();
    }

    /// <summary>표시 순서 판별에 쓰는 파일 항목의 정렬 값.</summary>
    public readonly record struct SortProbe(string Name, DateTime Modified, long Size, string Type);

    // 종류는 마지막: 한 폴더의 사진은 대개 종류가 같아 거의 판별력이 없다.
    private static readonly string[] DetectableSorts = { "Name", "DateModified", "Size", "Type" };

    /// <summary>
    /// 화면에 보이는 파일 순서와 맞는 정렬을 고른다. 폴더의 정렬 상태(<paramref name="preferredSortBy"/>)가
    /// 순서와 맞으면 그것을, 아니면 이름·수정한 날짜·크기·종류 순으로 처음 맞는 것을 돌려준다.
    /// 맞는 게 없으면 폴더의 정렬 상태. 같은 값끼리는 순서를 따지지 않되, 후보 정렬은 값이 실제로
    /// 달라지는 곳이 한 번은 있어야 맞는 것으로 본다(모두 같은 값이면 어떤 순서와도 "맞아" 버린다).
    /// </summary>
    public static (string? SortBy, bool Ascending) DetectDisplayedSort(
        IReadOnlyList<SortProbe> files, string? preferredSortBy, bool preferredAscending)
    {
        if (files.Count < 2) return (preferredSortBy, preferredAscending);
        if (preferredSortBy != null && IsOrdered(files, preferredSortBy, preferredAscending, requireDistinct: false))
            return (preferredSortBy, preferredAscending);
        foreach (var sortBy in DetectableSorts)
        {
            if (IsOrdered(files, sortBy, true, requireDistinct: true)) return (sortBy, true);
            if (IsOrdered(files, sortBy, false, requireDistinct: true)) return (sortBy, false);
        }
        return (preferredSortBy, preferredAscending);
    }

    private static bool IsOrdered(IReadOnlyList<SortProbe> files, string sortBy, bool ascending, bool requireDistinct)
    {
        bool sawDistinct = false;
        for (int i = 1; i < files.Count; i++)
        {
            int c = sortBy switch
            {
                "Name" => NaturalStringComparer.Instance.Compare(files[i - 1].Name, files[i].Name),
                "DateModified" => files[i - 1].Modified.CompareTo(files[i].Modified),
                "Size" => files[i - 1].Size.CompareTo(files[i].Size),
                "Type" => string.Compare(files[i - 1].Type, files[i].Type, StringComparison.OrdinalIgnoreCase),
                _ => 1, // 모르는 정렬: 맞지 않음
            };
            if (ascending ? c > 0 : c < 0) return false;
            if (c != 0) sawDistinct = true;
        }
        return sawDistinct || !requireDistinct;
    }

    /// <summary>
    /// 폴더 보기 문맥과 함께 연다. 전용 STA 스레드에서 실행한다 — ShellExecuteEx가 site 호출과 앱 활성화를
    /// 끝낼 때까지 호출 스레드를 붙잡으므로 UI 스레드를 쓰지 않는다. false면 호출자가 기존 방식으로 연다.
    /// </summary>
    public static Task<bool> TryOpenAsync(string filePath, string? sortBy, bool ascending, int indexHint)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.TrySetResult(Launch(filePath, BuildSortColumns(sortBy, ascending), Math.Max(0, indexHint))); }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FolderContextLauncher] launch failed: {ex.GetType().Name}: {ex.Message}");
                tcs.TrySetResult(false);
            }
        })
        {
            IsBackground = true,
            Name = "SpanFolderContextLaunch",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }

    private static bool Launch(string filePath, SortColumn[] sort, int indexHint)
    {
        string? folder = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(folder) || SHParseDisplayName(folder, IntPtr.Zero, out IntPtr folderPidl, 0, out _) < 0)
            return false;

        FolderViewSite site;
        try
        {
            // 정렬이 있으면 처리기가 IColumnManager로 보이는 컬럼도 읽는다 — 정렬 키가 들어 있게 한다.
            var columns = sort.Length > 0
                ? new[] { PropertyKey.ItemNameDisplay, PropertyKey.DateModified, PropertyKey.ItemTypeText, PropertyKey.Size }
                : Array.Empty<PropertyKey>();
            site = new FolderViewSite(folderPidl, indexHint, sort, columns);
        }
        finally { ILFree(folderPidl); }

        IntPtr siteUnknown = Marshal.GetIUnknownForObject(site);
        try
        {
            var sei = new ShellExecuteInfo
            {
                cbSize = Marshal.SizeOf<ShellExecuteInfo>(),
                fMask = SeeMaskInvokeIdList | SeeMaskFlagHinstIsSite | SeeMaskFlagNoUi | SeeMaskNoAsync | SeeMaskFlagLogUsage,
                lpFile = filePath,
                nShow = 1, // SW_SHOWNORMAL
                hInstApp = siteUnknown, // [in] site. [out] 결과 코드로 덮어써질 수 있다 — 해제는 siteUnknown으로만.
            };
            bool ok = ShellExecuteExW(ref sei);
            int error = ok ? 0 : Marshal.GetLastWin32Error();
            string sortText = sort.Length > 0 ? $"pid{sort[0].Key.PropertyId}{(sort[0].Direction > 0 ? "+" : "-")}" : "none";
            DebugLogger.Log($"[FolderContextLauncher] {Path.GetFileName(filePath)} ok={ok} err={error} CallMask=0x{site.CallMask:X} sort={sortText} index={indexHint}");

            // 사용자가 취소한 경우(예: 앱 선택 창) 다시 열지 않는다
            return ok || error == ErrorCancelled;
        }
        finally { Marshal.Release(siteUnknown); }
    }

    // ── 연결 정보 조회 ─────────────────────────────────────────

    private const int AssocStrDelegateExecute = 18; // ASSOCSTR_DELEGATEEXECUTE
    private const int AssocStrAppId = 21;           // ASSOCSTR_APPID

    private static string? QueryAssociation(int what, string ext, string? verb)
    {
        var sb = new StringBuilder(256);
        uint cch = (uint)sb.Capacity;
        return AssocQueryStringW(0, what, ext, verb, sb, ref cch) == 0 ? sb.ToString() : null;
    }

    // ── P/Invoke ───────────────────────────────────────────────

    private const int SeeMaskInvokeIdList = 0x0000000C;
    private const int SeeMaskNoAsync = 0x00000100;
    private const int SeeMaskFlagNoUi = 0x00000400;
    private const int SeeMaskFlagLogUsage = 0x04000000;
    private const int SeeMaskFlagHinstIsSite = 0x08000000;
    private const int ErrorCancelled = 1223;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int cbSize;
        public int fMask;
        public IntPtr hwnd;
        public string? lpVerb;
        public string? lpFile;
        public string? lpParameters;
        public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        public string? lpClass;
        public IntPtr hkeyClass;
        public int dwHotKey;
        public IntPtr hIconOrMonitor;
        public IntPtr hProcess;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo sei);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryStringW(int flags, int str, string assoc, string? extra, StringBuilder? outBuf, ref uint cchOut);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr pbc, out IntPtr pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    internal static extern uint ILGetSize(IntPtr pidl);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;

    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }

    private static readonly Guid FmtidStorage = new("b725f130-47ef-101a-a5f1-02608c9eebac");
    public static readonly PropertyKey ItemTypeText = new(FmtidStorage, 4);
    public static readonly PropertyKey ItemNameDisplay = new(FmtidStorage, 10);
    public static readonly PropertyKey Size = new(FmtidStorage, 12);
    public static readonly PropertyKey DateModified = new(FmtidStorage, 14);
}

/// <summary>SORTCOLUMN — Direction: SORT_ASCENDING = 1, SORT_DESCENDING = -1.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SortColumn
{
    public PropertyKey Key;
    public int Direction;
}

[ComImport, Guid("6d5140c1-7436-11ce-8034-00aa006009fa"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IServiceProviderNative
{
    [PreserveSig] int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject);
}

/// <summary>IFolderView + IFolderView2 전체 vtable (ShObjIdl_core.h 순서).</summary>
[ComImport, Guid("1af3a467-214f-4298-908e-06b03e0b39f9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView2Native
{
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint viewMode);
    [PreserveSig] int GetFolder(ref Guid riid, out IntPtr ppv);
    [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
    [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
    [PreserveSig] int Items(uint uFlags, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetSelectionMarkedItem(out int piItem);
    [PreserveSig] int GetFocusedItem(out int piItem);
    [PreserveSig] int GetItemPosition(IntPtr pidl, IntPtr ppt);
    [PreserveSig] int GetSpacing(IntPtr ppt);
    [PreserveSig] int GetDefaultSpacing(IntPtr ppt);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int iItem, uint dwFlags);
    [PreserveSig] int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags);
    [PreserveSig] int SetGroupBy(IntPtr key, int fAscending);
    [PreserveSig] int GetGroupBy(IntPtr pkey, IntPtr pfAscending);
    [PreserveSig] int SetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr propvar);
    [PreserveSig] int GetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr ppropvar);
    [PreserveSig] int SetTileViewProperties(IntPtr pidl, IntPtr pszPropList);
    [PreserveSig] int SetExtendedTileViewProperties(IntPtr pidl, IntPtr pszPropList);
    [PreserveSig] int SetText(int iType, IntPtr pwszText);
    [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
    [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
    [PreserveSig] int GetSortColumnCount(out int pcColumns);
    [PreserveSig] int SetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetSortColumns(IntPtr rgSortColumns, int cColumns);
    [PreserveSig] int GetItem(int iItem, ref Guid riid, out IntPtr ppv);
    [PreserveSig] int GetVisibleItem(int iStart, int fPrevious, out int piItem);
    [PreserveSig] int GetSelectedItem(int iStart, out int piItem);
    [PreserveSig] int GetSelection(int fNoneImpliesFolder, out IntPtr ppsia);
    [PreserveSig] int GetSelectionState(IntPtr pidl, out uint pdwFlags);
    [PreserveSig] int InvokeVerbOnSelection(IntPtr pszVerb);
    [PreserveSig] int SetViewModeAndIconSize(uint uViewMode, int iImageSize);
    [PreserveSig] int GetViewModeAndIconSize(out uint puViewMode, out int piImageSize);
    [PreserveSig] int SetGroupSubsetCount(uint cVisibleRows);
    [PreserveSig] int GetGroupSubsetCount(out uint pcVisibleRows);
    [PreserveSig] int SetRedraw(int fRedrawOn);
    [PreserveSig] int IsMoveInSameFolder();
    [PreserveSig] int DoRename();
}

[ComImport, Guid("1079acfc-29bd-11d3-8e0d-00c04f6837d5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistIdListNative
{
    [PreserveSig] int GetClassID(out Guid pClassId);
    [PreserveSig] int SetIDList(IntPtr pidl);
    [PreserveSig] int GetIDList(out IntPtr ppidl);
}

[ComImport, Guid("d8ec27bb-3f3b-4042-b10a-4acfd924d453"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IColumnManagerNative
{
    [PreserveSig] int SetColumnInfo(IntPtr propkey, IntPtr pcmci);
    [PreserveSig] int GetColumnInfo(IntPtr propkey, IntPtr pcmci);
    [PreserveSig] int GetColumnCount(uint dwFlags, out uint puCount);
    [PreserveSig] int GetColumns(uint dwFlags, IntPtr rgkeyOrder, uint cColumns);
    [PreserveSig] int SetColumns(IntPtr rgkeyOrder, uint cVisible);
}

/// <summary>
/// 탐색기 폴더 보기(DefView)를 흉내 낸 최소 site. 사진 앱 열기 처리기가 실제로 부르는 것만 구현하고
/// 나머지는 E_NOTIMPL. 생성 후 바뀌지 않는 데이터만 돌려준다(셸이 동기 호출, 예외 없음).
/// 처리기 호출 순서(windows.system.launcher.dll 26200 분석·실측): QueryService(SID_SFolderView, IFolderView2)
/// → GetSelectedItem → IPersistIDList.GetIDList → GetViewModeAndIconSize → GetGroupBy(pfAscending=NULL)
/// → GetSortColumnCount → (정렬이 있으면) GetSortColumns, IColumnManager.GetColumnCount/GetColumns.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class FolderViewSite : IServiceProviderNative, IFolderView2Native, IPersistIdListNative, IColumnManagerNative
{
    private const int S_OK = 0, S_FALSE = 1;
    private const int E_NOTIMPL = unchecked((int)0x80004001);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
    private const int E_POINTER = unchecked((int)0x80004003);
    private const int E_INVALIDARG = unchecked((int)0x80070057);

    private static readonly Guid SidFolderView = new("cde725b0-ccc9-4519-917e-325d72fab4ce"); // == IID_IFolderView
    private static readonly Guid IidFolderView = new("cde725b0-ccc9-4519-917e-325d72fab4ce");
    private static readonly Guid IidFolderView2 = new("1af3a467-214f-4298-908e-06b03e0b39f9");
    private const uint FvmDetails = 4;

    private readonly byte[] _folderPidl; // 관리 메모리 사본 — 셸이 늦게 불러도 수명 문제 없음
    private readonly int _index;
    private readonly SortColumn[] _sort;
    private readonly PropertyKey[] _columns;

    /// <summary>진단: 1=QueryService 2=GetSelectedItem 4=GetIDList 8=GetSortColumnCount 16=IColumnManager.</summary>
    public int CallMask => _callMask;
    private volatile int _callMask;

    public FolderViewSite(IntPtr folderPidl, int index, SortColumn[] sort, PropertyKey[] columns)
    {
        int cb = (int)FolderContextLauncher.ILGetSize(folderPidl);
        _folderPidl = new byte[cb];
        Marshal.Copy(folderPidl, _folderPidl, 0, cb);
        _index = index;
        _sort = sort;
        _columns = columns;
    }

    // IServiceProvider — 폴더 보기만 제공. 나머지 서비스는 site가 없을 때와 같게.
    public int QueryService(ref Guid guidService, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        if (guidService != SidFolderView || (riid != IidFolderView2 && riid != IidFolderView)) return E_NOINTERFACE;
        _callMask |= 1;
        IntPtr unknown = Marshal.GetIUnknownForObject(this);
        try { return Marshal.QueryInterface(unknown, ref riid, out ppvObject); }
        finally { Marshal.Release(unknown); }
    }

    // IFolderView2 — 처리기가 부르는 메서드
    public int GetSelectedItem(int iStart, out int piItem)
    {
        _callMask |= 2;
        if (iStart <= _index) { piItem = _index; return S_OK; }
        piItem = -1; // DefView와 같이: 더 이상 선택 항목 없음
        return S_FALSE;
    }
    public int GetViewModeAndIconSize(out uint puViewMode, out int piImageSize) { puViewMode = FvmDetails; piImageSize = 16; return S_OK; }
    public int GetGroupBy(IntPtr pkey, IntPtr pfAscending)
    {
        if (pkey == IntPtr.Zero) return E_POINTER;
        Marshal.StructureToPtr(default(PropertyKey), pkey, false); // PKEY_Null = 묶지 않음
        if (pfAscending != IntPtr.Zero) Marshal.WriteInt32(pfAscending, 1); // 처리기는 NULL을 넘긴다
        return S_OK;
    }
    public int GetSortColumnCount(out int pcColumns) { _callMask |= 8; pcColumns = _sort.Length; return S_OK; }
    public int GetSortColumns(IntPtr rgSortColumns, int cColumns)
    {
        if (rgSortColumns == IntPtr.Zero || cColumns < 0 || cColumns > _sort.Length) return E_INVALIDARG;
        int size = Marshal.SizeOf<SortColumn>();
        for (int i = 0; i < cColumns; i++) Marshal.StructureToPtr(_sort[i], rgSortColumns + i * size, false);
        return S_OK;
    }
    public int GetCurrentViewMode(out uint pViewMode) { pViewMode = FvmDetails; return S_OK; }
    public int GetSelectionMarkedItem(out int piItem) { piItem = _index; return S_OK; }
    public int GetFocusedItem(out int piItem) { piItem = _index; return S_OK; }
    public int GetCurrentFolderFlags(out uint pdwFlags) { pdwFlags = 0; return S_OK; }
    public int GetAutoArrange() => S_FALSE;
    public int IsMoveInSameFolder() => S_FALSE;

    // IPersistIDList
    public int GetClassID(out Guid pClassId) { pClassId = Guid.Empty; return E_NOTIMPL; }
    public int SetIDList(IntPtr pidl) => E_NOTIMPL;
    public int GetIDList(out IntPtr ppidl)
    {
        _callMask |= 4;
        ppidl = Marshal.AllocCoTaskMem(_folderPidl.Length); // 호출자가 ILFree(CoTaskMemFree)로 해제
        Marshal.Copy(_folderPidl, 0, ppidl, _folderPidl.Length);
        return S_OK;
    }

    // IColumnManager — 정렬이 있을 때만 불린다
    public int GetColumnCount(uint dwFlags, out uint puCount) { _callMask |= 16; puCount = (uint)_columns.Length; return S_OK; }
    public int GetColumns(uint dwFlags, IntPtr rgkeyOrder, uint cColumns)
    {
        if (rgkeyOrder == IntPtr.Zero || cColumns > _columns.Length) return E_INVALIDARG;
        int size = Marshal.SizeOf<PropertyKey>();
        for (int i = 0; i < cColumns; i++) Marshal.StructureToPtr(_columns[i], rgkeyOrder + i * size, false);
        return S_OK;
    }
    public int SetColumnInfo(IntPtr propkey, IntPtr pcmci) => E_NOTIMPL;
    public int GetColumnInfo(IntPtr propkey, IntPtr pcmci) => E_NOTIMPL;
    public int SetColumns(IntPtr rgkeyOrder, uint cVisible) => E_NOTIMPL;

    // 나머지 — 쓰지 않음
    public int SetCurrentViewMode(uint viewMode) => E_NOTIMPL;
    public int GetFolder(ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return E_NOTIMPL; }
    public int Item(int iItemIndex, out IntPtr ppidl) { ppidl = IntPtr.Zero; return E_NOTIMPL; }
    public int ItemCount(uint uFlags, out int pcItems) { pcItems = 0; return E_NOTIMPL; }
    public int Items(uint uFlags, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return E_NOTIMPL; }
    public int GetItemPosition(IntPtr pidl, IntPtr ppt) => E_NOTIMPL;
    public int GetSpacing(IntPtr ppt) => E_NOTIMPL;
    public int GetDefaultSpacing(IntPtr ppt) => E_NOTIMPL;
    public int SelectItem(int iItem, uint dwFlags) => E_NOTIMPL;
    public int SelectAndPositionItems(uint cidl, IntPtr apidl, IntPtr apt, uint dwFlags) => E_NOTIMPL;
    public int SetGroupBy(IntPtr key, int fAscending) => E_NOTIMPL;
    public int SetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr propvar) => E_NOTIMPL;
    public int GetViewProperty(IntPtr pidl, IntPtr propkey, IntPtr ppropvar) => E_NOTIMPL;
    public int SetTileViewProperties(IntPtr pidl, IntPtr pszPropList) => E_NOTIMPL;
    public int SetExtendedTileViewProperties(IntPtr pidl, IntPtr pszPropList) => E_NOTIMPL;
    public int SetText(int iType, IntPtr pwszText) => E_NOTIMPL;
    public int SetCurrentFolderFlags(uint dwMask, uint dwFlags) => E_NOTIMPL;
    public int SetSortColumns(IntPtr rgSortColumns, int cColumns) => E_NOTIMPL;
    public int GetItem(int iItem, ref Guid riid, out IntPtr ppv) { ppv = IntPtr.Zero; return E_NOTIMPL; }
    public int GetVisibleItem(int iStart, int fPrevious, out int piItem) { piItem = -1; return E_NOTIMPL; }
    public int GetSelection(int fNoneImpliesFolder, out IntPtr ppsia) { ppsia = IntPtr.Zero; return E_NOTIMPL; }
    public int GetSelectionState(IntPtr pidl, out uint pdwFlags) { pdwFlags = 0; return E_NOTIMPL; }
    public int InvokeVerbOnSelection(IntPtr pszVerb) => E_NOTIMPL;
    public int SetViewModeAndIconSize(uint uViewMode, int iImageSize) => E_NOTIMPL;
    public int SetGroupSubsetCount(uint cVisibleRows) => E_NOTIMPL;
    public int GetGroupSubsetCount(out uint pcVisibleRows) { pcVisibleRows = 0; return E_NOTIMPL; }
    public int SetRedraw(int fRedrawOn) => E_NOTIMPL;
    public int DoRename() => E_NOTIMPL;
}
