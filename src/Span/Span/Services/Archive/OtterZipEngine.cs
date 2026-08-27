using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Span.Helpers;

namespace Span.Services.Archive;

/// <summary>Outcome of one <see cref="OtterZipEngine.ExtractAll"/> call.</summary>
internal sealed class OtterZipExtractResult
{
    internal int Code { get; init; }
    internal bool Success => Code == OtterZipNative.Err.Ok;
    internal bool Canceled => Code == OtterZipNative.Err.OperationCanceled;

    internal uint EntriesExtracted { get; init; }
    internal uint EntriesSkipped { get; init; }
    internal uint WarningsCount { get; init; }
    internal ulong BytesWritten { get; init; }

    /// <summary>Localization key describing <see cref="Code"/> to the user.</summary>
    internal string MessageKey { get; init; } = "Op_ExtractFailed";

    /// <summary>English native diagnostic. For logs and Sentry only — never a toast.</summary>
    internal string? NativeMessage { get; init; }

    /// <summary>True when the code indicates a defect on our side rather than bad input.</summary>
    internal bool IsOurBug =>
        Code is OtterZipNative.Err.Generic
             or OtterZipNative.Err.InvalidArgument
             or OtterZipNative.Err.InvalidHandle
             or OtterZipNative.Err.BackendError;
}

/// <summary>Progress snapshot handed to the caller during extraction.</summary>
internal readonly record struct OtterZipProgress(
    ulong BytesProcessed,
    ulong BytesTotal,
    uint EntriesProcessed,
    uint EntriesTotal,
    string? CurrentEntry);

/// <summary>
/// Managed wrapper over otterzip_ffi.dll (Issue #66) — the archive formats
/// System.IO.Compression cannot open.
///
/// Availability is a normal condition, not an error. The DLL is x64-only and is not
/// packaged for other platforms, so every caller must check <see cref="IsAvailable"/>
/// and fall back to the existing System.IO.Compression path. A missing or unloadable
/// DLL must never reach the user as an exception.
/// </summary>
internal static class OtterZipEngine
{
    private static readonly Lazy<bool> _available = new(Probe, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>
    /// True when the native engine loaded and reports the ABI these bindings target.
    /// Probed once per process; the result is cached including the failure case, so a
    /// missing DLL costs one load attempt rather than one per extraction.
    /// </summary>
    internal static bool IsAvailable => _available.Value;

    private static bool Probe()
    {
        // The Rust core has no x86 / ARM64 target, and Span.csproj only packages the
        // DLL for x64. Checking here keeps the failure out of the exception path
        // entirely on other architectures.
        if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
        {
            DebugLogger.Log($"[OtterZip] disabled: process architecture is {RuntimeInformation.ProcessArchitecture}, engine is x64-only");
            return false;
        }

        try
        {
            uint abi = OtterZipNative.AbiVersion();
            if (abi != OtterZipNative.ExpectedAbiVersion)
            {
                // Refuse rather than call in: the structs in OtterZipNative are laid
                // out for one ABI, and a drifted layout corrupts memory silently
                // instead of failing.
                DebugLogger.Log($"[OtterZip] disabled: ABI mismatch — dll reports {abi}, bindings expect {OtterZipNative.ExpectedAbiVersion}");
                return false;
            }

            DebugLogger.Log($"[OtterZip] engine available (ABI {abi})");
            return true;
        }
        catch (DllNotFoundException)
        {
            DebugLogger.Log("[OtterZip] disabled: otterzip_ffi.dll not found next to the app");
            return false;
        }
        catch (BadImageFormatException)
        {
            DebugLogger.Log("[OtterZip] disabled: otterzip_ffi.dll is not loadable in this process (architecture mismatch?)");
            return false;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[OtterZip] disabled: unexpected load failure {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Opens an archive for reading. Returns null on failure; the caller should fall
    /// back rather than treat this as fatal.
    ///
    /// A password for an encrypted archive belongs here, NOT in the extract options —
    /// the native extract path never reads the options' password field.
    /// </summary>
    internal static OtterZipArchiveHandle? Open(string archivePath, string? password = null)
    {
        if (!IsAvailable) return null;

        var pathBytes = Encoding.UTF8.GetBytes(archivePath);
        byte[] pwBytes = password is null ? [] : Encoding.UTF8.GetBytes(password);

        var handle = new OtterZipArchiveHandle();
        int rc = OtterZipNative.ArchiveOpen(
            pathBytes, (nuint)pathBytes.Length,
            OtterZipNative.OpenModeRead,
            pwBytes, (nuint)pwBytes.Length,
            out IntPtr raw);

        if (rc != OtterZipNative.Err.Ok)
        {
            // Read the native message on this thread, immediately — the native store
            // is thread_local and does not survive a thread switch.
            DebugLogger.Log($"[OtterZip] open failed ({rc}) for {archivePath}: {ReadLastError() ?? "(no message)"}");
            handle.Dispose();
            return null;
        }

        handle.SetRawHandle(raw);
        return handle;
    }

    /// <summary>Number of entries, or -1 if it could not be determined.</summary>
    internal static int EntryCount(OtterZipArchiveHandle handle)
    {
        if (handle is null || handle.IsInvalid) return -1;

        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            return OtterZipNative.ArchiveEntryCount(handle.DangerousGetHandle(), out int count) == OtterZipNative.Err.Ok
                ? count
                : -1;
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    /// <summary>True if the archive is encrypted, false if not, null if unknown.</summary>
    internal static bool? IsEncrypted(OtterZipArchiveHandle handle)
    {
        if (handle is null || handle.IsInvalid) return null;

        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            return OtterZipNative.ArchiveIsEncrypted(handle.DangerousGetHandle(), out int flag) == OtterZipNative.Err.Ok
                ? flag != 0
                : null;
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    /// <summary>
    /// Extracts every entry into <paramref name="destination"/>.
    ///
    /// Blocking call — run it on a background thread. <paramref name="onProgress"/> is
    /// invoked from native code (possibly a worker thread) and must be cheap and
    /// non-blocking; anything expensive belongs behind a throttle on the caller's side.
    ///
    /// Cancellation is observed at entry boundaries, so a single very large entry keeps
    /// running until it finishes. Callers must reflect that in the UI rather than
    /// implying an immediate stop.
    /// </summary>
    internal static OtterZipExtractResult ExtractAll(
        OtterZipArchiveHandle handle,
        string destination,
        Action<OtterZipProgress>? onProgress = null,
        CancellationToken cancellationToken = default)
    {
        if (handle is null || handle.IsInvalid)
            return new OtterZipExtractResult { Code = OtterZipNative.Err.InvalidHandle };

        var destBytes = Encoding.UTF8.GetBytes(destination);
        var destPin = GCHandle.Alloc(destBytes, GCHandleType.Pinned);

        // Kept in a local so the delegate is not collected while native code holds it.
        OtterZipNative.ProgressCallback callback = (ref OtterZipNative.ProgressView view, IntPtr _) =>
        {
            // Nothing may escape into native code. An exception crossing the ABI
            // boundary is undefined behaviour, so the entire body is guarded and a
            // failure here degrades to "keep going" rather than tearing down the process.
            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return 1; // non-zero requests cancel

                if (onProgress is not null)
                {
                    string? entry = view.CurrentEntryUtf8 != IntPtr.Zero && view.CurrentEntryLen > 0
                        ? Marshal.PtrToStringUTF8(view.CurrentEntryUtf8, (int)view.CurrentEntryLen)
                        : null;

                    onProgress(new OtterZipProgress(
                        view.BytesProcessed, view.BytesTotal,
                        view.EntriesProcessed, view.EntriesTotal,
                        entry));
                }
            }
            catch
            {
                // Deliberately swallowed — see above.
            }
            return 0;
        };

        var options = new OtterZipNative.ExtractOptions
        {
            DestinationUtf8 = destPin.AddrOfPinnedObject(),
            DestinationLen = (nuint)destBytes.Length,
            OverwritePolicy = 0,
            PreserveTimestamps = 1,
            FollowSymlinks = 0,
            BlockPathTraversal = 1,

            // Ratio gates off, absolute cap on. The native defaults (1000:1 per entry
            // and cumulative) reject ordinary files: a 1.5 GiB zero-filled disk image
            // or preallocated database compresses at about 1028:1, just past the limit,
            // and DEFLATE's own ceiling is around 1032:1 — so the gate mostly fires on
            // legitimate content. For a file manager a false rejection of the user's own
            // backup is worse than the thing the gate prevents. The real harm is filling
            // the disk, so we bound that directly instead.
            MaxCompressionRatio = 0,
            MaxTotalCompressionRatio = 0,
            MaxTotalOutputBytes = FreeSpaceCap(destination),

            // Both of these are traps — see the field docs in OtterZipNative.
            // The password belongs on Open(); the entry filter is not consumed at all.
            PasswordUtf8 = IntPtr.Zero,
            PasswordLen = 0,
            EntryFilterUtf8 = IntPtr.Zero,
            EntryFilterLen = 0,
        };

        var report = default(OtterZipNative.ExtractReport);
        int rc;
        string? nativeMessage = null;
        bool added = false;

        try
        {
            handle.DangerousAddRef(ref added);
            rc = OtterZipNative.ArchiveExtractAll(
                handle.DangerousGetHandle(), ref options, callback, IntPtr.Zero, ref report);

            // Same thread as the failing call, before anything else — thread_local store.
            if (rc != OtterZipNative.Err.Ok)
                nativeMessage = ReadLastError();
        }
        finally
        {
            if (added) handle.DangerousRelease();
            destPin.Free();
            GC.KeepAlive(callback);
        }

        if (rc != OtterZipNative.Err.Ok)
        {
            DebugLogger.Log(
                $"[OtterZip] extract_all failed ({rc}) into {destination}: {nativeMessage ?? "(no message)"}");
        }

        return new OtterZipExtractResult
        {
            Code = rc,
            EntriesExtracted = report.EntriesExtracted,
            EntriesSkipped = report.EntriesSkipped,
            WarningsCount = report.WarningsCount,
            BytesWritten = report.BytesWritten,
            MessageKey = MessageKeyFor(rc),
            NativeMessage = nativeMessage,
        };
    }

    /// <summary>
    /// Absolute output cap for one extraction: the destination drive's free space less
    /// a small margin. Returns 0 (meaning "no cap" to the native side) when free space
    /// cannot be determined, since guessing low would reject valid work.
    /// </summary>
    private static ulong FreeSpaceCap(string destination)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(destination));
            if (string.IsNullOrEmpty(root)) return 0;

            long free = new DriveInfo(root).AvailableFreeSpace;
            if (free <= 0) return 0;

            // Leave 64 MiB so a runaway extraction fails cleanly instead of leaving the
            // volume with no room to report the failure or clean up.
            const long Margin = 64L * 1024 * 1024;
            return free > Margin ? (ulong)(free - Margin) : 0;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[OtterZip] free space unavailable for {destination}: {ex.GetType().Name}");
            return 0;
        }
    }

    private static string? ReadLastError()
    {
        try
        {
            IntPtr p = OtterZipNative.LastErrorMessage();
            return p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Maps a native error code to a localization key.
    ///
    /// Codes that mean "we called the API wrong" deliberately collapse into the generic
    /// message: they are our defects, and the user cannot act on them. They are also the
    /// ones worth reporting — see <see cref="OtterZipExtractResult.IsOurBug"/>.
    /// </summary>
    private static string MessageKeyFor(int code) => code switch
    {
        OtterZipNative.Err.Ok => "",
        OtterZipNative.Err.OperationCanceled => "Toast_OperationCancelled",

        OtterZipNative.Err.WrongPassword => "Op_ArchivePasswordRequired",
        OtterZipNative.Err.CorruptedArchive => "Op_ArchiveCorrupted",
        OtterZipNative.Err.UnsupportedFormat => "Op_ArchiveUnsupported",
        OtterZipNative.Err.MissingVolume => "Op_ArchiveMissingVolume",
        OtterZipNative.Err.EntryNotFound => "Op_ArchiveEntryNotFound",

        OtterZipNative.Err.PathTraversal => "Op_ArchiveUnsafePath",
        OtterZipNative.Err.ZipBomb => "Op_ArchiveTooLarge",
        OtterZipNative.Err.DiskFull => "Op_ArchiveDiskFull",
        OtterZipNative.Err.PermissionDenied => "Op_ArchivePermissionDenied",
        OtterZipNative.Err.FileNotFound => "Op_ArchiveNotFound",

        _ => "Op_ExtractFailed",
    };
}
