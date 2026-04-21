using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Span.Services
{
    public class DefaultFileManagerService
    {
        private const string FolderOpenCommandKey = @"Software\Classes\Folder\shell\open\command";
        private const string DriveOpenCommandKey = @"Software\Classes\Drive\shell\open\command";

        // ── SHChangeNotify: 셸에게 파일 association 변경됨을 알림 (explorer 재시작 없이 즉시 반영) ──
        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const uint SHCNF_IDLIST = 0x0000;
        private const uint SHCNF_FLUSH = 0x1000;

        private static void NotifyShellAssocChanged()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST | SHCNF_FLUSH, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] SHChangeNotify failed: {ex.Message}");
            }
        }

        /// <summary>
        /// [주의: 현재 호출 측에서 비활성 처리됨]
        /// Win11 explorer.exe 셸 association 캐시 무효화 — 강제 재시작.
        /// SHChangeNotify만으로는 explorer가 갱신 안 하는 경우가 있음.
        /// 사용자가 열어둔 탐색기 창이 모두 닫히는 위험 있음 → 명시 동의 후에만 호출할 것.
        /// </summary>
        public static void RestartExplorer()
        {
            try
            {
                Helpers.DebugLogger.Log("[DefaultFM] Restarting explorer.exe to refresh association cache");
                var kill = Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = "/F /IM explorer.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                kill?.WaitForExit(3000);

                // OS가 자동 재시작 안 한 경우 fallback (보통은 자동 시작됨)
                System.Threading.Thread.Sleep(400);
                var running = Process.GetProcessesByName("explorer");
                if (running.Length == 0)
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        UseShellExecute = true
                    });
                    Helpers.DebugLogger.Log("[DefaultFM] explorer.exe manually restarted");
                }
                else
                {
                    Helpers.DebugLogger.Log($"[DefaultFM] explorer.exe auto-restarted ({running.Length} instance)");
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] RestartExplorer failed: {ex.Message}");
            }
        }

        // AppExecutionAlias 경로 (WindowsApps에 등록됨)
        private static readonly string AliasPath =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Microsoft", "WindowsApps", "spanfinder.exe");

        /// <summary>
        /// regedit /s + runas로 기본 파일 관리자 등록.
        /// UAC 다이얼로그가 표시됨.
        /// </summary>
        public async Task<bool> SetAsDefaultAsync()
        {
            try
            {
                var regContent = GenerateSetDefaultReg();
                var tempPath = Path.Combine(Path.GetTempPath(), "SpanSetDefault.reg");
                await File.WriteAllTextAsync(tempPath, regContent);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    Arguments = $"/s \"{tempPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"  // UAC 승격
                });

                if (process != null)
                    await process.WaitForExitAsync();

                // temp 파일 정리
                try { File.Delete(tempPath); } catch { }

                // 셸에 association 변경 알림 — 안 하면 explorer 재시작 전까지 옛 핸들러 유지
                NotifyShellAssocChanged();

                // 검증
                return IsDefault();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 사용자가 UAC를 취소한 경우
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] SetAsDefault failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 기본 파일 관리자 해제.
        /// </summary>
        public async Task<bool> UnsetDefaultAsync()
        {
            try
            {
                var regContent = GenerateRestoreReg();
                var tempPath = Path.Combine(Path.GetTempPath(), "SpanRestoreDefault.reg");
                await File.WriteAllTextAsync(tempPath, regContent);

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "regedit.exe",
                    Arguments = $"/s \"{tempPath}\"",
                    UseShellExecute = true,
                    Verb = "runas"
                });

                if (process != null)
                    await process.WaitForExitAsync();

                try { File.Delete(tempPath); } catch { }

                // 셸에 association 변경 알림
                NotifyShellAssocChanged();

                return !IsDefault();
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] UnsetDefault failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 현재 SPAN이 기본 파일 관리자인지 확인.
        /// HKCU에서 Folder\shell\open\command를 읽어 spanfinder.exe 포함 여부 확인.
        /// </summary>
        public bool IsDefault()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(FolderOpenCommandKey);
                var command = key?.GetValue("")?.ToString();
                return !string.IsNullOrEmpty(command)
                    && command.Contains("spanfinder.exe", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 등록용 .reg 파일을 사용자 지정 경로에 내보내기 (fallback용).
        /// </summary>
        public async Task ExportSetDefaultRegAsync(string filePath)
        {
            await File.WriteAllTextAsync(filePath, GenerateSetDefaultReg());
        }

        /// <summary>
        /// 복원용 .reg 파일을 사용자 지정 경로에 내보내기.
        /// </summary>
        public async Task ExportRestoreRegAsync(string filePath)
        {
            await File.WriteAllTextAsync(filePath, GenerateRestoreReg());
        }

        /// <summary>등록용 .reg 내용 생성</summary>
        private string GenerateSetDefaultReg()
        {
            // %LOCALAPPDATA%\Microsoft\WindowsApps\spanfinder.exe 전체 경로 사용 (안정성)
            var exePath = AliasPath.Replace("\\", "\\\\");
            //
            // ⚠ DelegateExecute는 반드시 "" (빈 문자열). "-" (삭제) 아님.
            //
            //   HKLM\Folder\shell\open\command 에 DelegateExecute={11dbb47c-...} (Windows.FileExplorer COM)이 기본 설정됨.
            //   HKCR(merged) = HKCU overlay on HKLM. HKCU에 키가 없으면 HKLM값이 상속되어
            //   explorer가 CoCreateInstance로 COM 핸들러를 우선 호출 → 우리 명령줄 우회됨.
            //   → HKCU에 빈 문자열로 명시 설정하여 CLSID 파싱 실패 유도 → (기본값) 명령줄 fallback.
            //
            // Folder(meta) + Directory(파일시스템 폴더) + Drive(루트 볼륨) 모두 커버.
            return $"""
Windows Registry Editor Version 5.00

[HKEY_CURRENT_USER\Software\Classes\Folder\shell\open\command]
@="\"{exePath}\" \"%1\""
"DelegateExecute"=""

[HKEY_CURRENT_USER\Software\Classes\Directory\shell\open\command]
@="\"{exePath}\" \"%1\""
"DelegateExecute"=""

[HKEY_CURRENT_USER\Software\Classes\Drive\shell\open\command]
@="\"{exePath}\" \"%1\""
"DelegateExecute"=""
""";
        }

        /// <summary>복원용 .reg 내용 생성</summary>
        private string GenerateRestoreReg()
        {
            return """
Windows Registry Editor Version 5.00

[-HKEY_CURRENT_USER\Software\Classes\Folder\shell\open\command]
[-HKEY_CURRENT_USER\Software\Classes\Directory\shell\open\command]
[-HKEY_CURRENT_USER\Software\Classes\Drive\shell\open\command]
""";
        }
    }
}
