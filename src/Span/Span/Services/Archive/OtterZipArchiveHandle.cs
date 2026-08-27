using System.Runtime.InteropServices;

namespace Span.Services.Archive;

/// <summary>
/// Owns a native <c>OtterzipArchive*</c>.
///
/// A raw IntPtr here would make double-close and use-after-free ordinary bugs
/// rather than impossible ones, and both corrupt the process rather than throwing.
/// SafeHandle gives us refcounted access (<c>DangerousAddRef</c>/<c>Release</c>) so a
/// call in flight cannot be closed out from under it, plus finalizer-backed release
/// if a caller forgets to dispose.
/// </summary>
internal sealed class OtterZipArchiveHandle : SafeHandle
{
    internal OtterZipArchiveHandle() : base(IntPtr.Zero, ownsHandle: true) { }

    public override bool IsInvalid => handle == IntPtr.Zero;

    internal void SetRawHandle(IntPtr raw) => SetHandle(raw);

    protected override bool ReleaseHandle()
    {
        OtterZipNative.ArchiveClose(handle);
        return true;
    }
}
