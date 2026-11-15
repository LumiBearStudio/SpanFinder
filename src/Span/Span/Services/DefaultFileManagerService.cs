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

        // ── Win+E (Issue #78) ──
        // Win+E는 폴더 열기 동사가 아니라 이 CLSID(파일 탐색기)의 OpenNewWindow 동사를 부른다(HKLM에 등록됨).
        // HKCU에 같은 동사의 command를 두면 그쪽이 우선한다 — Files 앱과 같은 방식.
        // HKCU에는 원래 이 CLSID 키가 없으므로, 끌 때는 우리가 만든 키를 지워 HKLM 동작으로 되돌린다.
        private const string WinEClsid = "{52205fd8-5dfb-447d-801a-d0b52f2e83e1}";
        private const string WinEClsidKey = @"Software\Classes\CLSID\" + WinEClsid;
        private const string WinECommandKey = WinEClsidKey + @"\shell\opennewwindow\command";

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

        /// <summary>Win+E가 SPAN을 열도록 등록돼 있는지 (HKCU의 OpenNewWindow command가 spanfinder.exe인지).</summary>
        public bool IsWinEEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(WinECommandKey);
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
        /// Win+E로 SPAN을 열지 켜고 끈다. 등록·해제와 같은 방식(regedit /s + runas, UAC)이다 —
        /// 패키지 앱의 HKCU\Software\Classes 쓰기는 가상화되어 셸이 보지 못한다.
        /// </summary>
        public async Task<bool> SetWinEAsync(bool enable)
        {
            if (IsWinEEnabled() == enable) return true;
            try
            {
                var regContent = "Windows Registry Editor Version 5.00\r\n\r\n"
                    + (enable ? GenerateSetWinELines() : GenerateRemoveWinELines());
                var tempPath = Path.Combine(Path.GetTempPath(), "SpanWinE.reg");
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

                NotifyShellAssocChanged();

                var now = IsWinEEnabled();
                Helpers.DebugLogger.Log($"[DefaultFM] Win+E {(enable ? "enable" : "disable")} → enabled={now}");
                return now == enable;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 사용자가 UAC를 취소한 경우
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] Win+E toggle failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Win+E 등록 줄. 탐색기의 Win+E처럼 항상 새 창을 연다(--new-window).
        /// DelegateExecute=""는 폴더 열기와 같은 이유 — HKLM의 COM 핸들러가 우리 명령줄을 가리지 않게 한다.
        /// </summary>
        private static string GenerateSetWinELines()
        {
            var exePath = AliasPath.Replace("\\", "\\\\");
            return $"[HKEY_CURRENT_USER\\{WinECommandKey}]\r\n"
                 + $"@=\"\\\"{exePath}\\\" --new-window\"\r\n"
                 + "\"DelegateExecute\"=\"\"\r\n";
        }

        /// <summary>
        /// Win+E 해제 줄. SPAN이 등록한 게 아니면 건드리지 않는다(다른 앱의 설정일 수 있음).
        /// 빈 opennewwindow 키가 남으면 HKLM 동사를 가려 Win+E가 깨질 수 있으므로(폴더 열기에서 겪은 문제),
        /// command가 아니라 키째 지운다. CLSID 키에 우리가 만든 것만 있으면 CLSID 키를 통째로 지운다.
        /// </summary>
        private string GenerateRemoveWinELines()
        {
            if (!IsWinEEnabled()) return string.Empty;
            return WinEKeyHoldsOnlySpan()
                ? $"[-HKEY_CURRENT_USER\\{WinEClsidKey}]\r\n"
                : $"[-HKEY_CURRENT_USER\\{WinEClsidKey}\\shell\\opennewwindow]\r\n";
        }

        /// <summary>HKCU CLSID 키가 shell\opennewwindow\command 한 줄기만 담고 있는지(값·다른 하위 키 없음).</summary>
        private static bool WinEKeyHoldsOnlySpan()
        {
            try
            {
                using var clsid = Registry.CurrentUser.OpenSubKey(WinEClsidKey);
                if (clsid == null) return true;
                if (!HoldsOnly(clsid, "shell")) return false;
                using var shell = clsid.OpenSubKey("shell");
                if (shell == null || !HoldsOnly(shell, "opennewwindow")) return false;
                using var verb = shell.OpenSubKey("opennewwindow");
                return verb != null && HoldsOnly(verb, "command");
            }
            catch
            {
                return false; // 판단할 수 없으면 보수적으로 동사 키만 지운다
            }

            static bool HoldsOnly(RegistryKey key, string child)
            {
                if (key.ValueCount != 0) return false;
                var subKeys = key.GetSubKeyNames();
                return subKeys.Length == 1 && subKeys[0].Equals(child, StringComparison.OrdinalIgnoreCase);
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

        /// <summary>
        /// 복원용 .reg 내용 생성.
        /// v2.0.7까지는 \command만 지워 빈 shell\open 키가 남았고, 그 빈 키가 HKLM의 open 동사를 가려
        /// ShellExecute(폴더, "open")이 ERROR_NO_ASSOCIATION(1155)으로 실패했다(실측: 기본 동사(NULL)는 성공,
        /// 명시적 "open"만 실패). 그래서 shell\open에 SPAN이 쓴 command 말고 아무것도 없으면 shell\open 키째
        /// 지운다. 다른 값이나 하위 키가 있으면(다른 앱·사용자가 둔 것) 예전처럼 command만 지워 그것들을 보존한다.
        /// shell 키는 다른 앱의 동사가 있을 수 있어 건드리지 않는다.
        /// </summary>
        private string GenerateRestoreReg()
        {
            var sb = new System.Text.StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
            foreach (var cls in ShellOpenClasses)
            {
                var openKey = $@"HKEY_CURRENT_USER\Software\Classes\{cls}\shell\open";
                sb.Append(OpenKeyHoldsOnlySpanCommand(cls)
                    ? $"[-{openKey}]\r\n"
                    : $"[-{openKey}\\command]\r\n");
            }
            // 기본 파일 관리자를 끄면 Win+E도 같이 되돌린다 — 앱을 지우기 전에 이 설정만 끄면 되도록.
            sb.Append(GenerateRemoveWinELines());
            return sb.ToString();
        }

        /// <summary>HKCU {cls}\shell\open이 없거나, 값이 없고 하위 키가 command 하나(또는 없음)뿐인지.</summary>
        private static bool OpenKeyHoldsOnlySpanCommand(string cls)
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{cls}\shell\open");
                if (key == null) return true;
                if (key.ValueCount != 0) return false;
                var subKeys = key.GetSubKeyNames();
                return subKeys.Length == 0
                    || (subKeys.Length == 1 && subKeys[0].Equals("command", StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false; // 판단할 수 없으면 보수적으로 command만 지운다
            }
        }

        private static readonly string[] ShellOpenClasses = { "Folder", "Directory", "Drive" };

        /// <summary>
        /// 이전 버전의 해제가 남긴 빈 shell\open 키(값도 하위 키도 없음)가 있는 클래스 목록.
        /// 값이나 하위 키가 있는 키는 다른 앱이 쓴 것일 수 있어 대상에서 뺀다.
        /// </summary>
        public System.Collections.Generic.List<string> FindLeftoverOpenKeys()
        {
            var found = new System.Collections.Generic.List<string>();
            foreach (var cls in ShellOpenClasses)
            {
                try
                {
                    using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{cls}\shell\open");
                    if (key != null && key.ValueCount == 0 && key.SubKeyCount == 0)
                        found.Add(cls);
                }
                catch
                {
                    // 읽기 실패는 '없음'으로 본다
                }
            }
            return found;
        }

        /// <summary>기본 파일 관리자가 꺼져 있는데 빈 shell\open 키가 남아 있는지.</summary>
        public bool HasLeftoverOpenKeys()
        {
            try { return !IsDefault() && FindLeftoverOpenKeys().Count > 0; }
            catch { return false; }
        }

        /// <summary>
        /// 남은 빈 shell\open 키를 지운다. 등록·해제와 같은 방식(regedit /s + runas, UAC)이다 —
        /// 패키지 앱의 HKCU 쓰기는 가상화되므로 앱이 직접 지울 수 없다.
        /// </summary>
        public async Task<bool> RemoveLeftoverOpenKeysAsync()
        {
            var classes = FindLeftoverOpenKeys();
            if (classes.Count == 0) return true;

            try
            {
                var lines = new System.Text.StringBuilder("Windows Registry Editor Version 5.00\r\n\r\n");
                foreach (var cls in classes)
                    lines.Append($"[-HKEY_CURRENT_USER\\Software\\Classes\\{cls}\\shell\\open]\r\n");

                var tempPath = Path.Combine(Path.GetTempPath(), "SpanCleanupOpenKeys.reg");
                await File.WriteAllTextAsync(tempPath, lines.ToString());

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

                NotifyShellAssocChanged();

                var remaining = FindLeftoverOpenKeys();
                Helpers.DebugLogger.Log($"[DefaultFM] Leftover open keys cleanup: {string.Join(",", classes)} → remaining {remaining.Count}");
                return remaining.Count == 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 사용자가 UAC를 취소한 경우
                return false;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[DefaultFM] Leftover open keys cleanup failed: {ex.Message}");
                return false;
            }
        }
    }
}
