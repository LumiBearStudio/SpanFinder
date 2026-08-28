// -----------------------------------------------------------------------------
// otterzip_ffi.dll statically links the `unrar` / `unrar_sys` crates, which embed
// UnRAR sources (C) Alexander Roshal. SPAN Finder uses UnRAR to EXTRACT RAR
// archives only and never creates them. The UnRAR license requires the following
// paragraph to appear in the license, the documentation, and in source code
// comments of the resulting package — this is that source comment. The same text
// is in LICENSE.md (UnRAR Exception) and OpenSourceLicenses.md.
//
//   UnRAR source code may be used in any software to handle
//   RAR archives without limitations free of charge, but cannot be
//   used to develop RAR (WinRAR) compatible archiver and to
//   re-create RAR compression algorithm, which is proprietary.
//   Distribution of modified UnRAR source code in separate form
//   or as a part of other software is permitted, provided that
//   full text of this paragraph, starting from "UnRAR source code"
//   words, is included in license, or in documentation if license
//   is not available, and in source code comments of resulting package.
//
// Because that restriction is one GPL v3 does not allow to be imposed on
// recipients, LICENSE.md carries an UnRAR Exception (GPL v3 section 7).
// -----------------------------------------------------------------------------

using System.Runtime.InteropServices;

namespace Span.Services.Archive;

/// <summary>
/// Raw P/Invoke surface for otterzip_ffi.dll (Issue #66).
///
/// This mirrors <c>crates/otterzip-ffi/include/otterzip.h</c> in the OtterZip
/// repository, which cbindgen generates from the Rust source. That header is the
/// contract — do not infer layouts from OtterZip's own C# bindings
/// (<c>app/OtterZip.Interop</c>), which target net9.0, carry a Sentry
/// PackageReference with OtterZip's own DSN hardcoded, and are GPL-scoped to that
/// project. Everything here is written against the header.
///
/// The vendored binary and its provenance live in <c>third_party/otterzip/</c>.
/// Nothing in this file may be called unless <see cref="OtterZipEngine.IsAvailable"/>
/// says the engine loaded — see that class for why.
/// </summary>
internal static class OtterZipNative
{
    internal const string LibraryName = "otterzip_ffi";

    /// <summary>
    /// ABI the bindings in this file were written against. Checked at load time and
    /// treated as fatal-for-the-engine (not fatal-for-the-app) on mismatch: a struct
    /// layout drift would corrupt memory silently, so we refuse to call in at all.
    /// </summary>
    internal const uint ExpectedAbiVersion = 9;

    // --- error codes (crates/otterzip-ffi/src/error.rs) ----------------------
    internal static class Err
    {
        internal const int Ok = 0;

        // Our own bugs — never surface the raw text to users, always report.
        internal const int Generic = -1;
        internal const int InvalidArgument = -2;
        internal const int InvalidHandle = -3;
        internal const int OutOfMemory = -4;

        internal const int Io = -10;
        internal const int FileNotFound = -11;
        internal const int PermissionDenied = -12;
        internal const int DiskFull = -13;

        internal const int UnsupportedFormat = -20;
        internal const int CorruptedArchive = -21;
        internal const int WrongPassword = -22;
        internal const int MissingVolume = -23;
        internal const int EntryNotFound = -24;
        internal const int IteratorEnd = -25;

        internal const int OperationCanceled = -30;

        internal const int FeatureDisabled = -40;
        internal const int PathTraversal = -41;
        internal const int ZipBomb = -42;
        internal const int BackendError = -50;
    }

    /// <summary>Value for the <c>mode</c> parameter of <see cref="ArchiveOpen"/> (archive.rs:21).</summary>
    internal const uint OpenModeRead = 0;

    // --- structs -------------------------------------------------------------

    /// <summary>Mirrors <c>OtterzipProgressView</c>.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ProgressView
    {
        internal ulong BytesProcessed;
        internal ulong BytesTotal;
        internal uint EntriesProcessed;
        internal uint EntriesTotal;
        internal IntPtr CurrentEntryUtf8;
        internal nuint CurrentEntryLen;
        internal uint Phase;
        internal ulong ElapsedMs;

        /// <summary>ABI v9. Populated on the large-file streaming path only; 0 elsewhere.</summary>
        internal ulong CurrentEntryBytesProcessed;

        /// <summary>ABI v9. Populated on the large-file streaming path only; 0 elsewhere.</summary>
        internal ulong CurrentEntryBytesTotal;
    }

    /// <summary>
    /// Mirrors <c>OtterzipExtractOptions</c>. Field order and types must match the
    /// header exactly — this struct crosses the ABI boundary by layout, not by name.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtractOptions
    {
        internal IntPtr DestinationUtf8;
        internal nuint DestinationLen;
        internal uint OverwritePolicy;

        internal byte FlattenPaths;
        internal byte PreservePermissions;
        internal byte PreserveTimestamps;
        internal byte FollowSymlinks;
        internal byte BlockPathTraversal;
        internal byte PreserveZoneIdentifier;
        internal byte Reserved0;
        internal byte Reserved1;

        internal uint MaxCompressionRatio;
        internal uint MaxTotalCompressionRatio;
        internal ulong MaxTotalOutputBytes;

        /// <summary>
        /// DO NOT USE. The native extract path never reads this field — only the
        /// archive-creation path does. A password supplied here is silently ignored
        /// and the extract fails with <see cref="Err.WrongPassword"/> for no visible
        /// reason. Pass the password to <see cref="ArchiveOpen"/> instead.
        /// Always left null/0 by <see cref="OtterZipEngine"/>.
        /// </summary>
        internal IntPtr PasswordUtf8;
        internal nuint PasswordLen;

        /// <summary>
        /// DO NOT USE. Declared in the ABI but consumed nowhere in the native core
        /// (verified: the only references are the option declaration, its default, the
        /// FFI parse, and a round-trip test). Passing a filter here does not select a
        /// subset — the whole archive is extracted, with no warning. Selective
        /// extraction must not be built on this field.
        /// Always left null/0 by <see cref="OtterZipEngine"/>.
        /// </summary>
        internal IntPtr EntryFilterUtf8;
        internal nuint EntryFilterLen;
    }

    /// <summary>
    /// Mirrors <c>OtterzipExtractReport</c>. Note <c>bytes_written</c> is a 64-bit
    /// field sitting between two 32-bit ones — getting this order wrong does not
    /// fail loudly, it just reports byte counts as warning counts.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ExtractReport
    {
        internal uint EntriesExtracted;
        internal uint EntriesSkipped;
        internal ulong BytesWritten;
        internal uint WarningsCount;
        internal ulong ElapsedMs;
    }

    /// <summary>
    /// Progress/cancel callback. Return 0 to continue, non-zero to request cancel.
    ///
    /// This is invoked from native code, and on the ZIP parallel path it can be
    /// invoked from a rayon worker thread. It must never let a managed exception
    /// escape (undefined behaviour across the ABI boundary) and must never block —
    /// blocking inside it stalls one worker while the others keep writing, and it
    /// also blocks the only channel through which cancellation can be signalled.
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int ProgressCallback(ref ProgressView progress, IntPtr userData);

    // --- entry points --------------------------------------------------------
    // AssemblyDirectory only: the DLL ships next to the app inside the MSIX, and a
    // wider search order would let a DLL elsewhere on the path be loaded instead.

    [DllImport(LibraryName, EntryPoint = "otterzip_abi_version", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern uint AbiVersion();

    [DllImport(LibraryName, EntryPoint = "otterzip_archive_open", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern int ArchiveOpen(
        byte[] pathUtf8, nuint pathLen,
        uint mode,
        byte[] passwordUtf8, nuint passwordLen,
        out IntPtr outHandle);

    [DllImport(LibraryName, EntryPoint = "otterzip_archive_close", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern void ArchiveClose(IntPtr handle);

    [DllImport(LibraryName, EntryPoint = "otterzip_archive_entry_count", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern int ArchiveEntryCount(IntPtr handle, out int outCount);

    [DllImport(LibraryName, EntryPoint = "otterzip_archive_is_encrypted", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern int ArchiveIsEncrypted(IntPtr handle, out int outIsEncrypted);

    [DllImport(LibraryName, EntryPoint = "otterzip_archive_extract_all", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern int ArchiveExtractAll(
        IntPtr handle,
        ref ExtractOptions options,
        ProgressCallback? progressCallback,
        IntPtr userData,
        ref ExtractReport outReport);

    /// <summary>
    /// Last error message for the CURRENT THREAD. The native store is thread_local
    /// (error.rs:40-42), so this must be read on the same thread that made the failing
    /// call, before anything else runs on it — an <c>await</c> in between can resume
    /// elsewhere and return null or a stale message from an unrelated call.
    /// The text is English and intended for logs and crash reports, never for a toast.
    /// </summary>
    [DllImport(LibraryName, EntryPoint = "otterzip_last_error_message", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern IntPtr LastErrorMessage();

    [DllImport(LibraryName, EntryPoint = "otterzip_clear_last_error", CallingConvention = CallingConvention.Cdecl)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    internal static extern void ClearLastError();
}
