using Span.Models;

namespace Span.Helpers;

/// <summary>
/// Decides whether an <c>archive://</c> entry should be extracted to a temp file so the
/// preview loaders — which all take a real path — can open it (Issue #64).
///
/// Preview runs on every selection change, so the policy here exists to keep that cheap.
/// It lives in one place because both the preview panel and QuickLook need the same
/// answer, and a disagreement between them would show up as "the panel previews it but
/// spacebar doesn't".
/// </summary>
internal static class ArchivePreviewResolver
{
    /// <summary>
    /// Upper bound on what is worth extracting for a preview. Text previews read the first
    /// 30k characters and images/PDFs shown in a side panel are small, so anything past
    /// this is being extracted for nothing.
    /// </summary>
    internal const long MaxArchivePreviewBytes = 16 * 1024 * 1024;

    /// <summary>
    /// Preview kinds whose loader opens a file by path. Media is deliberately absent:
    /// video and audio stream from disk, so staging one would copy hundreds of megabytes
    /// out of the archive just because the user moved the selection onto it.
    /// Archive/Folder/Generic/None need no file either.
    /// </summary>
    private static bool NeedsRealFile(PreviewType previewType) =>
        previewType is PreviewType.Text or PreviewType.Markdown or PreviewType.Csv
            or PreviewType.HexBinary or PreviewType.Image or PreviewType.Pdf or PreviewType.Font;

    /// <summary>
    /// Returns a real path to preview, or null when the entry should not be staged — in
    /// which case the caller should fall back to the generic (metadata-only) preview
    /// rather than showing an error.
    ///
    /// <paramref name="entrySize"/> is the uncompressed size already known from the
    /// archive listing, so an oversized entry is rejected without opening the archive.
    /// </summary>
    internal static async Task<string?> ResolveAsync(
        PreviewType previewType,
        string archivePath,
        long entrySize,
        CancellationToken ct)
    {
        if (!ArchivePathHelper.IsArchivePath(archivePath)) return null;
        if (!NeedsRealFile(previewType)) return null;

        if (entrySize <= 0 || entrySize > MaxArchivePreviewBytes)
        {
            DebugLogger.Log(
                $"[Preview] archive entry not staged (size={entrySize}, cap={MaxArchivePreviewBytes}): {archivePath}");
            return null;
        }

        return await Services.Archive.ArchiveEntryStaging.StageForPreviewAsync(
            archivePath, entrySize, MaxArchivePreviewBytes, ct);
    }
}
