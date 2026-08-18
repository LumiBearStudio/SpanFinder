using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace Span.Helpers;

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
/// </summary>
internal static class ClipboardInteropHelper
{
    /// <summary>
    /// 지정 경로들을 클립보드에 올린다. 실제 파일시스템 경로만 대상이며
    /// (압축 내부·원격 경로는 제외) 유효 항목이 없으면 false를 반환하고
    /// 클립보드를 변경하지 않는다.
    /// </summary>
    public static async Task<bool> SetFilesAsync(IReadOnlyList<string> paths, bool isCut)
    {
        var items = new List<IStorageItem>();
        var validPaths = new List<string>();

        foreach (var p in paths)
        {
            if (string.IsNullOrWhiteSpace(p)) continue;
            // 압축 내부(archive://)·원격(ftp/sftp) 경로는 실파일이 아니라 StorageItem을
            // 만들 수 없다 → 빈 CF_HDROP가 되어 탐색기에서 오류가 난다.
            if (ArchivePathHelper.IsArchivePath(p) || Services.FileSystemRouter.IsRemotePath(p)) continue;

            try
            {
                if (Directory.Exists(p))
                {
                    items.Add(await StorageFolder.GetFolderFromPathAsync(p));
                    validPaths.Add(p);
                }
                else if (File.Exists(p))
                {
                    items.Add(await StorageFile.GetFileFromPathAsync(p));
                    validPaths.Add(p);
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[Clipboard] 경로 해석 실패 '{p}': {ex.Message}");
            }
        }

        if (items.Count == 0)
        {
            DebugLogger.Log("[Clipboard] 유효한 항목이 없어 클립보드를 변경하지 않음");
            return false;
        }

        try
        {
            var dp = new DataPackage
            {
                RequestedOperation = isCut ? DataPackageOperation.Move : DataPackageOperation.Copy
            };
            // 즉시 렌더링 — 지연 콜백에 의존하지 않으므로 탐색기가 언제 붙여넣어도 안전
            dp.SetStorageItems(items, /*readOnly*/ false);
            dp.SetText(string.Join(Environment.NewLine, validPaths));

            Clipboard.SetContent(dp);

            // 앱이 종료된 뒤에도 클립보드 내용이 유지되도록 시스템에 소유권 이전
            try { Clipboard.Flush(); }
            catch (Exception ex) { DebugLogger.Log($"[Clipboard] Flush 실패: {ex.Message}"); }

            return true;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Clipboard] SetContent 실패: {ex.Message}");
            return false;
        }
    }
}
