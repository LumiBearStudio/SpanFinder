using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Span.Helpers;

/// <summary>
/// Win32 JobObject 래퍼 — 자식 워커 프로세스가 메인 종료 시 자동 종료되도록 보장.
/// 외부 라이브러리 0 (직접 P/Invoke).
///
/// 사용 패턴:
///   var job = new JobObjectHelper();
///   var workerProc = Process.Start(...);
///   job.AssignProcess(workerProc);
///   // 메인 종료 시 (Process.Kill, Environment.Exit 등) JobObject가 닫히면서
///   // KILL_ON_JOB_CLOSE 플래그로 워커 자동 종료 → orphan 방지
/// </summary>
internal sealed class JobObjectHelper : IDisposable
{
    private IntPtr _handle;
    private bool _disposed;

    public JobObjectHelper()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle == IntPtr.Zero)
            throw new InvalidOperationException(
                $"CreateJobObject failed (LastError={Marshal.GetLastWin32Error()})");

        // KILL_ON_JOB_CLOSE: 마지막 핸들이 닫힐 때 모든 할당된 프로세스 종료
        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        int infoSize = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr infoPtr = Marshal.AllocHGlobal(infoSize);
        try
        {
            Marshal.StructureToPtr(info, infoPtr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, infoPtr, (uint)infoSize))
            {
                int err = Marshal.GetLastWin32Error();
                CloseHandle(_handle);
                _handle = IntPtr.Zero;
                throw new InvalidOperationException(
                    $"SetInformationJobObject failed (LastError={err})");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(infoPtr);
        }
    }

    /// <summary>
    /// 프로세스를 JobObject에 할당. 이후 JobObject가 닫히면 함께 종료됨.
    /// 실패 시 false 반환 (호출자가 수동 cleanup 결정).
    /// </summary>
    public bool AssignProcess(Process process)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(JobObjectHelper));
        if (process == null) throw new ArgumentNullException(nameof(process));
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return AssignProcessToJobObject(_handle, process.Handle);
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[JobObject] AssignProcess failed: {ex.Message}");
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }

    ~JobObjectHelper() => Dispose();

    // ════════════════════════════════════════════════════════════════
    //  P/Invoke
    // ════════════════════════════════════════════════════════════════

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(
        IntPtr hJob, int infoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);
}
