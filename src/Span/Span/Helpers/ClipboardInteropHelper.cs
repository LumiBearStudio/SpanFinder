using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Span.Helpers;

/// <summary><see cref="ClipboardInteropHelper.SetFilesAsync"/>의 결과.</summary>
internal enum ClipboardWriteResult
{
    /// <summary>클립보드에 썼다.</summary>
    Written,
    /// <summary>유효한 항목이 없거나 쓰기에 실패했다. 클립보드는 그대로다.</summary>
    Failed,
    /// <summary>기다리는 동안 더 나중에 시작한 복사가 먼저 클립보드에 썼다. 그것을 덮어쓰지 않았다.
    /// 호출자는 상태를 건드리지 않아야 한다(내부 클립보드 상태는 이미 나중 복사의 것이다).</summary>
    Superseded,
}

/// <summary>
/// Issue #62: 클립보드 쓰기(복사/잘라내기)를 Windows 탐색기가 확실히 읽을 수 있게 만든다.
///
/// 기존에는 <c>SetDataProvider</c>(지연 렌더링)만 등록하고 <c>Clipboard.Flush()</c>를
/// 호출하지 않았다. 탐색기가 붙여넣는 시점에 SPAN으로 콜백이 들어와야 하는데,
/// 경로 해석 실패 시 빈 목록을 그대로 넘기거나 콜백이 늦으면 탐색기에
/// CLIPBRD_E_BAD_DATA(0x800401D3)가 발생했다. 또 SPAN을 종료하면 클립보드 내용이
/// 사라졌다.
///
/// 이 헬퍼는 StorageItem을 미리 만들어 즉시 넣고(Flush로 시스템에 소유권 이전),
/// 유효한 항목이 하나도 없으면 아예 클립보드를 건드리지 않는다.
///
/// Issue #71: 경로 텍스트(CF_UNICODETEXT)는 넣지 않는다 — 탐색기의 복사와 같은 형식.
/// 파일 대화상자의 "파일 이름" 칸은 셸 자동 완성이 붙여넣기를 가로채 텍스트와
/// 파일 목록의 따옴표 경로를 함께 넣어 <c>T"T"</c>가 되고, Enter 시
/// "파일 이름이 올바르지 않습니다"로 실패했다. 텍스트가 없으면 <c>"T"</c>만 들어간다.
/// </summary>
internal static class ClipboardInteropHelper
{
    // 요청 순번. 큰 압축 항목 추출처럼 await가 길어지는 동안 나중 복사가 먼저 끝났다면,
    // 늦게 끝난 앞 요청이 그 결과를 덮어쓰지 않게 한다. 클립보드는 하나라 모든 창이 공유한다.
    private static int s_lastTicket;
    private static int s_lastWrittenTicket;

    /// <summary>
    /// 지정 경로들을 클립보드에 올린다. 실제 파일시스템 경로만 대상이며
    /// (압축 내부·원격 경로는 제외) 유효 항목이 없으면 <see cref="ClipboardWriteResult.Failed"/>를
    /// 반환하고 클립보드를 변경하지 않는다.
    /// </summary>
    public static async Task<ClipboardWriteResult> SetFilesAsync(IReadOnlyList<string> paths, bool isCut)
    {
        int ticket = Interlocked.Increment(ref s_lastTicket);

        // 호출자는 보통 내부 클립보드 목록(_clipboardPaths)을 그대로 넘긴다. 아래 await 사이에
        // 다른 복사·Esc·붙여넣기가 그 목록을 바꾸면 열거가 InvalidOperationException으로 끊기므로
        // 시작 시점의 복사본으로 작업한다.
        var requested = paths.ToList();
        var items = new List<IStorageItem>();

        // Issue #64: 압축 내부 항목은 실파일이 아니라 그대로는 StorageItem을 만들 수 없다
        // (빈 CF_HDROP가 되어 탐색기에서 오류). 임시 폴더에 꺼내 실제 경로로 바꾼다.
        // 원격(ftp/sftp) 경로는 여전히 대상이 아니며 아래에서 걸러진다.
        var resolved = Services.Archive.ArchiveEntryStaging.ContainsArchiveEntry(requested)
            ? await Services.Archive.ArchiveEntryStaging.MaterializeAsync(requested)
            : requested;

        foreach (var p in resolved)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            if (ArchivePathHelper.IsArchivePath(p) || Services.FileSystemRouter.IsRemotePath(p)) continue;

            try
            {
                if (Directory.Exists(p))
                {
                    items.Add(await StorageFolder.GetFolderFromPathAsync(p));
                }
                else if (File.Exists(p))
                {
                    items.Add(await StorageFile.GetFileFromPathAsync(p));
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Clipboard] 경로 해석 실패 '{p}': {ex.Message}");
            }
        }

        // 기다리는 사이 더 나중 복사가 이미 썼다면 성공·실패와 관계없이 손대지 않는다.
        // (실패로 돌려주면 호출자가 나중 복사의 내부 상태를 지운다)
        if (Volatile.Read(ref s_lastWrittenTicket) > ticket)
        {
            DebugLogger.Log("[Clipboard] 더 나중에 시작한 복사가 이미 썼으므로 이 요청은 쓰지 않음");
            return ClipboardWriteResult.Superseded;
        }

        if (items.Count == 0)
        {
            DebugLogger.Log("[Clipboard] 유효한 항목이 없어 클립보드를 변경하지 않음");
            return ClipboardWriteResult.Failed;
        }

        try
        {
            var dp = new DataPackage
            {
                RequestedOperation = isCut ? DataPackageOperation.Move : DataPackageOperation.Copy
            };
            // 즉시 렌더링 — 지연 콜백에 의존하지 않으므로 탐색기가 언제 붙여넣어도 안전
            dp.SetStorageItems(items, /*readOnly*/ false);

            Clipboard.SetContent(dp);
            Volatile.Write(ref s_lastWrittenTicket, ticket);

            // 앱이 종료된 뒤에도 클립보드 내용이 유지되도록 시스템에 소유권 이전
            try { Clipboard.Flush(); }
            catch (Exception ex) { DebugLogger.Log($"[Clipboard] Flush 실패: {ex.Message}"); }

            return ClipboardWriteResult.Written;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Clipboard] SetContent 실패: {ex.Message}");
            return ClipboardWriteResult.Failed;
        }
    }

    /// <summary>
    /// 텍스트만 클립보드에 쓴다(경로 복사). 파일 복사와 같은 순번을 써서, 이보다 먼저 시작해
    /// 아직 끝나지 않은 <see cref="SetFilesAsync"/>가 나중에 이 텍스트를 덮어쓰지 않게 한다.
    /// </summary>
    public static bool SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        int ticket = Interlocked.Increment(ref s_lastTicket);
        try
        {
            var dp = new DataPackage();
            dp.SetText(text);
            Clipboard.SetContent(dp);
            Volatile.Write(ref s_lastWrittenTicket, ticket);

            try { Clipboard.Flush(); }
            catch (Exception ex) { DebugLogger.Log($"[Clipboard] Flush 실패: {ex.Message}"); }

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Clipboard] SetText 실패: {ex.Message}");
            return false;
        }
    }
}
