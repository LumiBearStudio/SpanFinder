using System;
using System.IO;
using Microsoft.Win32;

namespace Span.Helpers;

/// <summary>
/// Windows Error Reporting (WER) LocalDumps 등록 헬퍼.
/// 네이티브 크래시(.NET 핸들러 미캡처)를 미니덤프로 자동 수집한다.
///
/// 등록 위치: HKCU\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\Span.exe
/// 덤프 저장: %LocalAppData%\Span\CrashDumps\Span.exe.{pid}.dmp
///
/// 외부 라이브러리 0 — 레지스트리 + 파일 IO만 사용.
/// 시작 시 1회 등록하면 OS가 다음 크래시부터 자동 덤프 생성.
/// </summary>
internal static class WerHelper
{
    private const string LocalDumpsKeyBase = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";

    // 우리 메인 실행 파일명 + 향후 워커도 같은 정책 적용 가능하도록 배열로 관리
    private static readonly string[] ExeNames = { "Span.exe", "Span.Thumbs.exe" };

    /// <summary>
    /// 미니덤프 저장 폴더 (외부에서도 참조 — CrashReportingService에서 dump 검색).
    /// </summary>
    public static string DumpFolder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Span", "CrashDumps");

    /// <summary>
    /// WER LocalDumps 레지스트리 등록.
    /// 이미 등록되어 있어도 idempotent — 매번 호출해도 안전.
    /// 실패해도 앱 동작에 영향 없도록 모든 예외 흡수.
    /// </summary>
    public static void EnsureRegistered()
    {
        try
        {
            var dumpFolder = DumpFolder;
            try { Directory.CreateDirectory(dumpFolder); } catch { }

            foreach (var exeName in ExeNames)
            {
                try
                {
                    using var key = Registry.CurrentUser.CreateSubKey(
                        $@"{LocalDumpsKeyBase}\{exeName}");
                    if (key == null) continue;

                    // ExpandString — %LocalAppData% 등 확장 가능. 우리는 절대경로지만 표준 따름
                    key.SetValue("DumpFolder", dumpFolder, RegistryValueKind.ExpandString);
                    key.SetValue("DumpCount", 10, RegistryValueKind.DWord);
                    // DumpType: 0=Custom, 1=Mini, 2=Full
                    // 2(Full)는 디스크 사용량 큼 — Mini로 시작, 필요 시 Full로 승격
                    key.SetValue("DumpType", 1, RegistryValueKind.DWord);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[WER] Register {exeName} failed: {ex.Message}");
                }
            }

            DebugLogger.Log($"[WER] LocalDumps registered (folder={dumpFolder})");
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[WER] EnsureRegistered failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 덤프 폴더의 모든 .dmp 파일 목록 반환 (오래된 순).
    /// 업로드 후 호출자가 삭제 책임.
    /// </summary>
    public static string[] EnumerateDumps()
    {
        try
        {
            if (!Directory.Exists(DumpFolder)) return Array.Empty<string>();
            var files = Directory.GetFiles(DumpFolder, "*.dmp");
            Array.Sort(files, (a, b) =>
                File.GetLastWriteTimeUtc(a).CompareTo(File.GetLastWriteTimeUtc(b)));
            return files;
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
