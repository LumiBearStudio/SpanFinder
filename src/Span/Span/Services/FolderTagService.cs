using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using Span.Helpers;
using Span.Models;

namespace Span.Services;

/// <summary>
/// Issue #58: 폴더 컬러 태그를 각 폴더의 desktop.ini에 저장/조회한다.
///
/// 저장 위치를 desktop.ini로 택한 이유: 파일이 폴더 안에 있으므로 폴더를 이동하거나
/// 이름을 바꿔도 — 심지어 SPAN 밖(탐색기, 스크립트)에서 옮겨도 — 태그가 따라간다.
/// 앱 DB(경로 키) 방식의 고아 문제도, NTFS 대체 스트림 방식의 USB/네트워크 소실 문제도
/// 발생하지 않는다.
///
/// 중요한 설계 결정 — 폴더 속성을 절대 건드리지 않는다:
/// Windows가 desktop.ini를 해석하려면 폴더에 System/ReadOnly 속성이 필요하지만,
///  - System 속성을 붙이면 숨김 표시가 꺼진 상태에서 그 폴더가 SPAN 목록에서 사라지고
///    (FolderViewModel의 Hidden|System 필터),
///  - ReadOnly 속성을 붙이면 Directory.Delete(recursive)가 실패해 폴더 이동/삭제가 깨진다.
/// 태그 점은 SPAN이 이 파일을 직접 읽어 그리므로 셸의 해석이 필요 없다.
/// 대신 탐색기에서는 태그가 보이지 않는다 (의도된 트레이드오프).
/// </summary>
public sealed class FolderTagService
{
    private const string SectionName = "SpanFinder";
    private const string KeyName = "Tag";
    private const string IniFileName = "desktop.ini";

    /// <summary>경로별 태그 캐시. 목록 스크롤 중 반복 I/O를 막는다.</summary>
    private readonly ConcurrentDictionary<string, FolderTagColor> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 폴더의 태그를 읽는다. 캐시에 있으면 I/O 없음.
    /// 반드시 백그라운드에서 호출할 것 (첫 조회는 파일 읽기).
    /// </summary>
    public FolderTagColor GetTag(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return FolderTagColor.None;
        if (_cache.TryGetValue(folderPath, out var cached)) return cached;

        var tag = ReadTagFromDisk(folderPath);
        _cache[folderPath] = tag;
        return tag;
    }

    /// <summary>캐시된 값만 반환 (I/O 없음). 없으면 null — 호출자가 백그라운드 조회를 걸 수 있다.</summary>
    public FolderTagColor? GetCachedTag(string folderPath)
        => _cache.TryGetValue(folderPath, out var v) ? v : null;

    private static FolderTagColor ReadTagFromDisk(string folderPath)
    {
        try
        {
            string iniPath = Path.Combine(folderPath, IniFileName);
            // 대부분의 폴더에는 desktop.ini가 없다 — 존재 확인만으로 빠르게 탈출
            if (!File.Exists(iniPath)) return FolderTagColor.None;

            var sb = new System.Text.StringBuilder(32);
            uint len = NativeMethods.GetPrivateProfileStringW(
                SectionName, KeyName, string.Empty, sb, (uint)sb.Capacity, iniPath);
            if (len == 0) return FolderTagColor.None;

            return Enum.TryParse<FolderTagColor>(sb.ToString(), ignoreCase: true, out var parsed)
                ? parsed
                : FolderTagColor.None;
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[FolderTag] 읽기 실패 '{folderPath}': {ex.Message}");
            return FolderTagColor.None;
        }
    }

    /// <summary>
    /// 폴더에 태그를 기록한다. FolderTagColor.None이면 태그를 제거한다.
    /// 성공 시 null, 실패 시 사용자에게 보여줄 오류 메시지를 반환한다.
    /// </summary>
    public string? SetTag(string folderPath, FolderTagColor color)
    {
        if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            return LocalizationService.L("Op_PathNotFoundShort");

        string iniPath = Path.Combine(folderPath, IniFileName);
        bool readOnlyCleared = false;

        try
        {
            // 기존 desktop.ini가 ReadOnly면 쓰기가 거부되므로 잠시 해제 후 원복
            if (File.Exists(iniPath))
            {
                var attrs = File.GetAttributes(iniPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(iniPath, attrs & ~FileAttributes.ReadOnly);
                    readOnlyCleared = true;
                }
                NormalizeEncodingIfNeeded(iniPath);
            }

            // WritePrivateProfileStringW는 Hidden+System 파일에도 안전하게 쓴다.
            // (File.WriteAllText 계열은 그런 파일에서 UnauthorizedAccessException을 던진다)
            bool ok = color == FolderTagColor.None
                ? NativeMethods.WritePrivateProfileStringW(SectionName, null, null, iniPath) // 섹션 통째 삭제
                : NativeMethods.WritePrivateProfileStringW(SectionName, KeyName, color.ToString(), iniPath);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                DebugLogger.Log($"[FolderTag] 쓰기 실패 '{folderPath}' err={err}");
                return err == 5 // ERROR_ACCESS_DENIED
                    ? LocalizationService.L("Error_AccessDenied")
                    : LocalizationService.L("Toast_OperationFailed");
            }

            // 새로 만들어진 desktop.ini는 사용자 목록에 보이지 않도록 숨김+시스템 속성 부여
            // (폴더 속성은 건드리지 않는다 — 클래스 주석 참조)
            if (File.Exists(iniPath))
            {
                try
                {
                    var attrs = File.GetAttributes(iniPath);
                    if ((attrs & FileAttributes.Hidden) == 0 || (attrs & FileAttributes.System) == 0)
                        File.SetAttributes(iniPath, attrs | FileAttributes.Hidden | FileAttributes.System);
                }
                catch { /* 속성 설정 실패는 치명적이지 않음 */ }

                // 태그를 지웠고 남은 내용이 없으면 파일 자체를 정리
                if (color == FolderTagColor.None && IsEffectivelyEmpty(iniPath))
                {
                    try { File.Delete(iniPath); } catch { /* 삭제 실패 무시 */ }
                }
            }

            _cache[folderPath] = color;
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return LocalizationService.L("Error_AccessDenied");
        }
        catch (PathTooLongException)
        {
            return LocalizationService.L("Error_PathTooLong");
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[FolderTag] 쓰기 예외 '{folderPath}': {ex.Message}");
            return LocalizationService.L("Toast_OperationFailed");
        }
        finally
        {
            if (readOnlyCleared && File.Exists(iniPath))
            {
                try { File.SetAttributes(iniPath, File.GetAttributes(iniPath) | FileAttributes.ReadOnly); }
                catch { }
            }
        }
    }

    /// <summary>
    /// UTF-8 BOM으로 저장된 desktop.ini는 WritePrivateProfileString이 섹션을 중복 생성해
    /// 기존 [.ShellClassInfo](커스텀 아이콘 등)를 무력화한다. BOM을 제거해 정규화한다.
    /// </summary>
    private static void NormalizeEncodingIfNeeded(string iniPath)
    {
        try
        {
            var bytes = File.ReadAllBytes(iniPath);
            if (bytes.Length < 3) return;
            if (bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF) return; // UTF-8 BOM 아님

            var attrs = File.GetAttributes(iniPath);
            File.SetAttributes(iniPath, FileAttributes.Normal);
            File.WriteAllBytes(iniPath, bytes[3..]);
            File.SetAttributes(iniPath, attrs);
            DebugLogger.Log($"[FolderTag] UTF-8 BOM 제거: {iniPath}");
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[FolderTag] BOM 정규화 실패: {ex.Message}");
        }
    }

    /// <summary>desktop.ini에 의미 있는 내용(섹션/키)이 남아 있는지 확인.</summary>
    private static bool IsEffectivelyEmpty(string iniPath)
    {
        try
        {
            foreach (var line in File.ReadAllLines(iniPath))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith(';')) continue;
                return false; // 섹션이든 키든 남아 있음
            }
            return true;
        }
        catch { return false; } // 확인 불가하면 삭제하지 않는다 (보수적)
    }

    /// <summary>경로(및 하위)의 캐시를 무효화한다. 폴더 이동/삭제/외부 변경 시 호출.</summary>
    public void InvalidateCache(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath)) return;
        _cache.TryRemove(folderPath, out _);
    }

    /// <summary>태그 지정이 가능한 경로인지 — 로컬 고정 디스크의 실제 폴더만 허용.</summary>
    public static bool IsTaggable(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (ArchivePathHelper.IsArchivePath(path)) return false;   // 압축 내부는 쓰기 불가
        if (FileSystemRouter.IsRemotePath(path)) return false;     // FTP/SFTP는 서버에 쓰레기를 남김
        if (path.StartsWith(@"\\", StringComparison.Ordinal)) return false; // UNC
        try
        {
            if (!Directory.Exists(path)) return false;
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return false;
            var drive = new DriveInfo(root);
            return drive.DriveType == DriveType.Fixed;
        }
        catch { return false; }
    }
}
