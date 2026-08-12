using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Span.Services.FileOperations;

/// <summary>
/// Issue #61: Windows 탐색기와 동일한 IFileOperation(Vista+) 기반 삭제.
///
/// 기존 SHFileOperation은 폴더를 통째로 넘기면 내부 재귀 삭제가 끝날 때까지 제어가
/// 돌아오지 않아 (1) 진행률이 0%에 고정되고 (2) 취소를 관측할 지점이 없었다.
/// IFileOperation은 진행 싱크(IFileOperationProgressSink)로 항목별 콜백을 주므로
/// 실시간 진행률과 즉시 취소가 가능하다.
///
/// 중요: 폴더를 통째로 넘겨도 휴지통에는 "폴더 하나"로 들어가므로 기존 Undo(휴지통
/// 복원) 로직을 그대로 사용할 수 있다 — 하위 항목을 개별 삭제하는 방식과 달리
/// 휴지통이 수백 개로 흩어지지 않는다.
///
/// COM 아파트먼트: 셸 COM은 STA에서 호출해야 안전하므로 전용 STA 스레드에서 실행한다.
/// </summary>
internal static class ShellDeleteWithProgress
{
    // ── 셸 COM 상수 ──
    private const uint FOF_SILENT = 0x0004;
    private const uint FOF_NOCONFIRMATION = 0x0010;
    private const uint FOF_ALLOWUNDO = 0x0040;      // 휴지통으로 이동
    private const uint FOF_NOERRORUI = 0x0400;
    private const uint FOFX_RECYCLEONDELETE = 0x00080000;

    private const int E_ABORT = unchecked((int)0x80004004);
    private const int COPYENGINE_E_USER_CANCELLED = unchecked((int)0x80270000);

    /// <summary>삭제 결과 — 성공 시 Error=null. MissingPaths는 셸이 찾지 못한 경로들.</summary>
    internal sealed record DeleteOutcome(
        string? Error, bool Cancelled, List<string> DeletedPaths, List<string> MissingPaths);

    /// <summary>
    /// 지정 경로들을 삭제한다(휴지통 또는 영구). 진행률/취소 지원.
    /// </summary>
    /// <param name="paths">삭제 대상 경로들</param>
    /// <param name="permanent">true면 영구 삭제, false면 휴지통</param>
    /// <param name="onProgress">(퍼센트 0-100, 현재 항목명) 콜백 — UI 스레드 아님</param>
    /// <param name="ct">취소 토큰 — 항목 경계에서 즉시 관측된다</param>
    internal static DeleteOutcome Execute(
        IReadOnlyList<string> paths,
        bool permanent,
        Action<int, string>? onProgress,
        CancellationToken ct)
    {
        DeleteOutcome outcome = new(null, false, new List<string>(), new List<string>());

        // 셸 COM은 STA 전용 — 전용 스레드에서 실행하고 완료까지 대기
        var thread = new Thread(() => outcome = ExecuteCore(paths, permanent, onProgress, ct));
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        return outcome;
    }

    private static DeleteOutcome ExecuteCore(
        IReadOnlyList<string> paths,
        bool permanent,
        Action<int, string>? onProgress,
        CancellationToken ct)
    {
        var deleted = new List<string>();
        var missing = new List<string>();
        IFileOperation? op = null;
        uint cookie = 0;
        ProgressSink? sink = null;

        try
        {
            var clsid = new Guid("3ad05575-8857-4850-9277-11b85bdb8e09"); // CLSID_FileOperation
            var iid = typeof(IFileOperation).GUID;
            object comObj = CoCreateInstanceWrapper(clsid, iid);
            op = (IFileOperation)comObj;

            uint flags = FOF_SILENT | FOF_NOCONFIRMATION | FOF_NOERRORUI;
            if (!permanent) flags |= FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE;
            op.SetOperationFlags(flags);

            sink = new ProgressSink(ct, onProgress, deleted);
            op.Advise(sink, out cookie);

            int queued = 0;
            foreach (var path in paths)
            {
                if (ct.IsCancellationRequested) break;

                // 셸이 존재하지 않는 경로에도 아이템을 만들어 줄 수 있으므로 먼저 실제 존재 확인
                if (!System.IO.File.Exists(path) && !System.IO.Directory.Exists(path))
                {
                    missing.Add(path);
                    continue;
                }

                IShellItem? item = CreateShellItem(path);
                if (item == null) { missing.Add(path); continue; } // 셸 아이템 생성 실패
                try { op.DeleteItem(item, null); queued++; }
                finally { Marshal.ReleaseComObject(item); }
            }

            if (ct.IsCancellationRequested)
                return new DeleteOutcome(null, true, deleted, missing);

            if (queued > 0)
            {
                op.PerformOperations();
                op.GetAnyOperationsAborted(out bool aborted);
                bool cancelled = aborted || ct.IsCancellationRequested || sink.Cancelled;
                return new DeleteOutcome(null, cancelled, deleted, missing);
            }

            return new DeleteOutcome(null, false, deleted, missing);
        }
        catch (COMException ex) when (ex.HResult == E_ABORT || ex.HResult == COPYENGINE_E_USER_CANCELLED)
        {
            return new DeleteOutcome(null, true, deleted, missing);
        }
        catch (Exception ex)
        {
            return new DeleteOutcome(ex.Message, ct.IsCancellationRequested, deleted, missing);
        }
        finally
        {
            try { if (op != null && cookie != 0) op.Unadvise(cookie); } catch { }
            try { if (op != null) Marshal.ReleaseComObject(op); } catch { }
        }
    }

    private static object CoCreateInstanceWrapper(Guid clsid, Guid iid)
    {
        // CoCreateInstance로 직접 IFileOperation을 요청 — Activator + 캐스팅 경로는
        // RCW가 IUnknown으로 만들어져 QueryInterface가 실패하는 경우가 있다.
        int hr = CoCreateInstance(ref clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, ref iid, out object obj);
        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
        return obj;
    }

    private const uint CLSCTX_INPROC_SERVER = 1;

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CoCreateInstance(
        ref Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    private static IShellItem? CreateShellItem(string path)
    {
        try
        {
            var iid = typeof(IShellItem).GUID;
            int hr = SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out IShellItem item);
            return hr == 0 ? item : null;
        }
        catch { return null; }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem ppv);

    /// <summary>
    /// 진행 싱크 — 항목 삭제 전후로 호출된다. 취소 요청 시 E_ABORT를 반환해
    /// 셸이 진행 중인 작업을 즉시 중단하게 한다 (탐색기의 취소 버튼과 동일 메커니즘).
    /// </summary>
    private sealed class ProgressSink : IFileOperationProgressSink
    {
        private readonly CancellationToken _ct;
        private readonly Action<int, string>? _onProgress;
        private readonly List<string> _deleted;
        private int _lastPercent = -1;

        // 진행 보고 스로틀 — 셸은 항목마다 콜백하므로 그대로 보고하면 항목 수만큼
        // COM 이름 조회 + UI 디스패치가 발생해 삭제 자체가 느려진다 (기존 복사 경로와 동일 정책).
        private readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private long _lastReportMs = -1000;
        private const long ReportIntervalMs = 100;

        private bool ShouldReport()
        {
            long now = _sw.ElapsedMilliseconds;
            if (now - _lastReportMs < ReportIntervalMs) return false;
            _lastReportMs = now;
            return true;
        }

        internal bool Cancelled { get; private set; }

        internal ProgressSink(CancellationToken ct, Action<int, string>? onProgress, List<string> deleted)
        {
            _ct = ct;
            _onProgress = onProgress;
            _deleted = deleted;
        }

        private int AbortIfCancelled()
        {
            if (_ct.IsCancellationRequested) { Cancelled = true; return E_ABORT; }
            return 0;
        }

        public int StartOperations() => AbortIfCancelled();
        public int FinishOperations(int hrResult) => 0;
        public int PreRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName) => AbortIfCancelled();
        public int PostRenameItem(uint dwFlags, IShellItem psiItem, string pszNewName, int hrRename, IShellItem psiNewlyCreated) => 0;
        public int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => AbortIfCancelled();
        public int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrMove, IShellItem psiNewlyCreated) => 0;
        public int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName) => AbortIfCancelled();
        public int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, string pszNewName, int hrCopy, IShellItem psiNewlyCreated) => 0;

        public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
        {
            // 취소 관측 지점 — 하위 항목마다 호출되므로 폴더 내부에서도 즉시 중단된다
            int abort = AbortIfCancelled();
            if (abort != 0) return abort;

            try
            {
                // 이름 조회(COM)와 UI 보고는 스로틀 — 취소 관측은 위에서 항상 수행됨
                if (_onProgress != null && psiItem != null && ShouldReport())
                {
                    psiItem.GetDisplayName(SIGDN_PARENTRELATIVEPARSING, out IntPtr namePtr);
                    if (namePtr != IntPtr.Zero)
                    {
                        string? name = Marshal.PtrToStringUni(namePtr);
                        Marshal.FreeCoTaskMem(namePtr);
                        if (!string.IsNullOrEmpty(name))
                            _onProgress(_lastPercent < 0 ? 0 : _lastPercent, name!);
                    }
                }
            }
            catch { /* 표시용이므로 실패 무시 */ }
            return 0;
        }

        public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated)
        {
            // 삭제된 경로 목록은 호출부가 실제 파일 존재 여부로 판정하므로 여기서
            // 항목마다 COM 이름 조회를 하지 않는다 (수천 개 항목에서 순수 오버헤드).
            return 0;
        }

        public int UpdateProgress(uint iWorkTotal, uint iWorkSoFar)
        {
            int abort = AbortIfCancelled();
            if (abort != 0) return abort;

            if (iWorkTotal > 0 && _onProgress != null)
            {
                int pct = (int)Math.Min(100, iWorkSoFar * 100.0 / iWorkTotal);
                // 퍼센트가 바뀐 경우에만, 그것도 스로틀 간격 내에서만 보고
                if (pct != _lastPercent && ShouldReport())
                {
                    _lastPercent = pct;
                    _onProgress(pct, string.Empty);
                }
            }
            return 0;
        }

        public int ResetTimer() => 0;
        public int PauseTimer() => 0;
        public int ResumeTimer() => 0;

        private const uint SIGDN_PARENTRELATIVEPARSING = 0x80018001;
        private const uint SIGDN_FILESYSPATH = 0x80058000;
    }

    // ══════════════ COM 인터페이스 선언 ══════════════

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IShellItem
    {
        void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        void GetParent(out IShellItem ppsi);
        void GetDisplayName(uint sigdnName, out IntPtr ppszName);
        void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        void Compare(IShellItem psi, uint hint, out int piOrder);
    }

    // IID_IFileOperation (CLSID_FileOperation과 다른 값이므로 혼동 주의)
    [ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperation
    {
        void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
        void Unadvise(uint dwCookie);
        void SetOperationFlags(uint dwOperationFlags);
        void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
        void SetProgressDialog(IntPtr popd);
        void SetProperties(IntPtr pproparray);
        void SetOwnerWindow(IntPtr hwndOwner);
        void ApplyPropertiesToItem(IShellItem psiItem);
        void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink? pfopsItem);
        void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IFileOperationProgressSink? pfopsItem);
        void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IFileOperationProgressSink? pfopsItem);
        void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
        void DeleteItem(IShellItem psiItem, IFileOperationProgressSink? pfopsItem);
        void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
        void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IFileOperationProgressSink? pfopsItem);
        void PerformOperations();
        void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IFileOperationProgressSink
    {
        [PreserveSig] int StartOperations();
        [PreserveSig] int FinishOperations(int hrResult);
        [PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);
        [PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);
        [PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
        [PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);
        [PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);
        [PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated);
        [PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
        [PreserveSig] int ResetTimer();
        [PreserveSig] int PauseTimer();
        [PreserveSig] int ResumeTimer();
    }
}
