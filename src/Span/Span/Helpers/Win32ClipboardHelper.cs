using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Span.Helpers;

/// <summary>
/// Issue #62: Windows 탐색기와 클립보드로 파일을 주고받기 위한 Win32(OLE) 경로.
///
/// WinRT <c>DataPackageView.GetStorageItemsAsync()</c>는 클립보드의 CF_HDROP를
/// StorageFile로 변환하는데, 압축 폴더 내부·클라우드 placeholder·경로 없는 셸 아이템이
/// 하나라도 섞이면 호출 전체가 실패한다. 탐색기는 항상 CF_HDROP를 넣으므로
/// 원본 형식을 직접 읽는 폴백이 필요하다.
///
/// 또한 잘라내기/복사 구분은 탐색기가 "Preferred DropEffect" 형식으로 전달하므로
/// 이를 읽어야 탐색기에서 Ctrl+X 한 항목이 SPAN에서도 이동으로 처리된다.
/// </summary>
internal static class Win32ClipboardHelper
{
    private const int S_OK = 0;
    private const short CF_HDROP = 15;

    private const uint DROPEFFECT_COPY = 1;
    private const uint DROPEFFECT_MOVE = 2;

    private static ushort _cfPreferredDropEffect;
    private static ushort CfPreferredDropEffect => _cfPreferredDropEffect != 0
        ? _cfPreferredDropEffect
        : (_cfPreferredDropEffect = NativeMethods.RegisterClipboardFormatW("Preferred DropEffect"));

    /// <summary>클립보드에 파일 목록(CF_HDROP)이 있는지 — 붙여넣기 버튼 활성화 판정용(저비용).</summary>
    public static bool HasFileDrop()
    {
        try
        {
            int hr = NativeMethods.OleGetClipboard(out IDataObject dataObj);
            if (hr != S_OK || dataObj == null) return false;
            try
            {
                var fmt = MakeFormat(CF_HDROP);
                return dataObj.QueryGetData(ref fmt) == S_OK;
            }
            finally { Marshal.ReleaseComObject(dataObj); }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Win32Clipboard] HasFileDrop 예외: {ex.Message}");
            return false;
        }
    }

    /// <summary>클립보드의 CF_HDROP에서 파일 경로 목록을 읽는다. 없으면 빈 목록.</summary>
    public static List<string> GetFileDropList()
    {
        var result = new List<string>();
        try
        {
            int hr = NativeMethods.OleGetClipboard(out IDataObject dataObj);
            if (hr != S_OK || dataObj == null) return result;

            try
            {
                var fmt = MakeFormat(CF_HDROP);
                if (dataObj.QueryGetData(ref fmt) != S_OK) return result;

                dataObj.GetData(ref fmt, out STGMEDIUM medium);
                try
                {
                    if (medium.unionmember == IntPtr.Zero) return result;

                    IntPtr hDrop = medium.unionmember;
                    // 첫 호출(index=0xFFFFFFFF)로 파일 개수를 얻는다
                    uint count = DragQueryFileW(hDrop, 0xFFFFFFFF, null, 0);
                    for (uint i = 0; i < count; i++)
                    {
                        uint len = DragQueryFileW(hDrop, i, null, 0);
                        if (len == 0) continue;
                        var sb = new System.Text.StringBuilder((int)len + 1);
                        if (DragQueryFileW(hDrop, i, sb, (uint)sb.Capacity) > 0)
                        {
                            var path = sb.ToString();
                            if (!string.IsNullOrWhiteSpace(path)) result.Add(path);
                        }
                    }
                }
                finally { NativeMethods.ReleaseStgMedium(ref medium); }
            }
            finally { Marshal.ReleaseComObject(dataObj); }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Win32Clipboard] GetFileDropList 예외: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// "Preferred DropEffect"를 읽어 잘라내기 여부를 판정. 형식이 없으면 null.
    /// true = 이동(잘라내기), false = 복사.
    /// </summary>
    public static bool? IsCutOperation()
    {
        try
        {
            int hr = NativeMethods.OleGetClipboard(out IDataObject dataObj);
            if (hr != S_OK || dataObj == null) return null;

            try
            {
                var fmt = MakeFormat((short)CfPreferredDropEffect);
                if (dataObj.QueryGetData(ref fmt) != S_OK) return null;

                dataObj.GetData(ref fmt, out STGMEDIUM medium);
                try
                {
                    if (medium.unionmember == IntPtr.Zero) return null;
                    IntPtr ptr = NativeMethods.GlobalLock(medium.unionmember);
                    if (ptr == IntPtr.Zero) return null;
                    try
                    {
                        uint effect = (uint)Marshal.ReadInt32(ptr);
                        if ((effect & DROPEFFECT_MOVE) != 0) return true;
                        if ((effect & DROPEFFECT_COPY) != 0) return false;
                        return null;
                    }
                    finally { NativeMethods.GlobalUnlock(medium.unionmember); }
                }
                finally { NativeMethods.ReleaseStgMedium(ref medium); }
            }
            finally { Marshal.ReleaseComObject(dataObj); }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[Win32Clipboard] IsCutOperation 예외: {ex.Message}");
            return null;
        }
    }

    private static FORMATETC MakeFormat(short cfFormat) => new()
    {
        cfFormat = cfFormat,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = -1,
        ptd = IntPtr.Zero,
        tymed = TYMED.TYMED_HGLOBAL
    };

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "DragQueryFileW")]
    private static extern uint DragQueryFileW(IntPtr hDrop, uint iFile, System.Text.StringBuilder? lpszFile, uint cch);
}
