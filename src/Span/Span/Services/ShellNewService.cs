using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace Span.Services;

/// <summary>
/// ShellNew 레지스트리 항목의 유형.
/// </summary>
public enum ShellNewType
{
    NullFile,
    FileName,
    Data,
    Command
}

/// <summary>
/// HKCR에서 읽은 ShellNew 항목 하나를 나타내는 모델.
/// </summary>
public class ShellNewItem
{
    /// <summary>확장자 (예: ".txt")</summary>
    public string Extension { get; set; } = "";

    /// <summary>표시 이름 (예: "텍스트 문서")</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>아이콘 경로 (nullable)</summary>
    public string? IconPath { get; set; }

    /// <summary>ShellNew 유형</summary>
    public ShellNewType Type { get; set; }

    /// <summary>FileName 타입: 템플릿 파일 경로</summary>
    public string? TemplatePath { get; set; }

    /// <summary>Data 타입: 바이너리 템플릿 데이터</summary>
    public byte[]? TemplateData { get; set; }

    /// <summary>Command 타입: 실행 명령</summary>
    public string? Command { get; set; }
}

/// <summary>
/// Windows ShellNew 레지스트리를 스캔하여 "새 파일" 메뉴 항목을 동적으로 제공하는 서비스.
/// HKCR\.{ext}\ShellNew 키를 읽어 NullFile, FileName, Data, Command 유형을 구분한다.
/// </summary>
public class ShellNewService
{
    private List<ShellNewItem>? _cache;

    /// <summary>
    /// HKCR에서 ShellNew 항목을 스캔하여 캐시된 목록 반환.
    /// 첫 호출 시 레지스트리를 스캔하며, 이후에는 캐시를 반환한다.
    /// </summary>
    public List<ShellNewItem> GetShellNewItems()
    {
        if (_cache != null) return _cache;
        _cache = ScanRegistry();
        return _cache;
    }

    /// <summary>
    /// 캐시 무효화 (설정 변경 시 호출).
    /// </summary>
    public void InvalidateCache() => _cache = null;

    /// <summary>
    /// ShellNew 항목에 따라 새 파일을 생성한다.
    /// 중복 파일명은 "baseName (2).ext" 패턴으로 자동 증가.
    /// </summary>
    /// <param name="item">ShellNew 항목</param>
    /// <param name="directoryPath">생성 대상 폴더 경로</param>
    /// <param name="customName">사용자 지정 기본 파일명 (null이면 DisplayName 사용)</param>
    /// <returns>생성된 파일 경로. Command 타입이면 null.</returns>
    public async Task<string?> CreateNewFileAsync(ShellNewItem item, string directoryPath, string? customName = null)
    {
        var baseName = customName ?? item.DisplayName;
        var fileName = $"{baseName}{item.Extension}";
        var filePath = Path.Combine(directoryPath, fileName);

        // 중복 방지: baseName (2).ext 패턴
        int counter = 2;
        while (File.Exists(filePath))
        {
            fileName = $"{baseName} ({counter}){item.Extension}";
            filePath = Path.Combine(directoryPath, fileName);
            counter++;
        }

        switch (item.Type)
        {
            case ShellNewType.NullFile:
                await Task.Run(() => File.WriteAllBytes(filePath, Array.Empty<byte>()));
                break;

            case ShellNewType.FileName:
                if (item.TemplatePath != null && File.Exists(item.TemplatePath))
                    await Task.Run(() => File.Copy(item.TemplatePath, filePath));
                else
                    await Task.Run(() => File.WriteAllBytes(filePath, Array.Empty<byte>()));
                break;

            case ShellNewType.Data:
                if (item.TemplateData != null)
                    await Task.Run(() => File.WriteAllBytes(filePath, item.TemplateData));
                else
                    await Task.Run(() => File.WriteAllBytes(filePath, Array.Empty<byte>()));
                break;

            case ShellNewType.Command:
                // Command 타입: 프로세스 실행 (OneNote 등)
                if (item.Command != null)
                {
                    var cmd = item.Command.Replace("%1", filePath);
                    try
                    {
                        Process.Start(new ProcessStartInfo("cmd.exe", $"/c {cmd}")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false
                        });
                    }
                    catch (Exception ex)
                    {
                        Helpers.DebugLogger.Log($"[ShellNew] Command execution error: {ex.Message}");
                    }
                }
                return null; // Command 타입은 파일 경로 반환 없음
        }

        return filePath;
    }

    private List<ShellNewItem> ScanRegistry()
    {
        var items = new List<ShellNewItem>();

        try
        {
            using var classesRoot = Registry.ClassesRoot;

            foreach (var extName in classesRoot.GetSubKeyNames())
            {
                if (!extName.StartsWith('.')) continue;

                try
                {
                    using var extKey = classesRoot.OpenSubKey(extName);
                    if (extKey == null) continue;

                    // ShellNew를 2단계로 탐색:
                    // 1차: HKCR\.ext\ShellNew (직접)
                    // 2차: HKCR\.ext\ProgID\ShellNew (ProgID 서브키 하위)
                    var shellNewKey = extKey.OpenSubKey("ShellNew");
                    if (shellNewKey == null)
                    {
                        // ProgID 서브키 탐색 (예: .docx\Word.Document.12\ShellNew)
                        foreach (var subName in extKey.GetSubKeyNames())
                        {
                            if (subName.StartsWith('.')) continue; // 다른 확장자 서브키 스킵
                            shellNewKey = extKey.OpenSubKey($"{subName}\\ShellNew");
                            if (shellNewKey != null) break;
                        }
                    }
                    if (shellNewKey == null) continue;

                    var item = new ShellNewItem { Extension = extName };

                    // 타입 결정 (우선순위: NullFile > FileName > Data > Command)
                    if (shellNewKey.GetValue("NullFile") != null)
                    {
                        item.Type = ShellNewType.NullFile;
                    }
                    else if (shellNewKey.GetValue("FileName") is string templateFile)
                    {
                        item.Type = ShellNewType.FileName;
                        item.TemplatePath = ResolveTemplatePath(templateFile);
                    }
                    else if (shellNewKey.GetValue("Data") is byte[] data)
                    {
                        item.Type = ShellNewType.Data;
                        item.TemplateData = data;
                    }
                    else if (shellNewKey.GetValue("Command") is string command)
                    {
                        item.Type = ShellNewType.Command;
                        item.Command = command;
                    }
                    else
                    {
                        continue; // 유효한 ShellNew 값 없음 — 건너뜀
                    }

                    // 표시 이름 가져오기
                    item.DisplayName = GetFileTypeDisplayName(extName, classesRoot);
                    if (string.IsNullOrEmpty(item.DisplayName))
                        item.DisplayName = $"{extName.TrimStart('.')} file";

                    // 아이콘 경로 (있으면)
                    item.IconPath = shellNewKey.GetValue("IconPath") as string;

                    items.Add(item);
                }
                catch
                {
                    // 깨진 레지스트리 항목 무시
                }
            }
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ShellNew] Registry scan error: {ex.Message}");
        }

        // .txt가 레지스트리에 없으면 기본 추가 (Windows 11에서 누락 가능)
        if (!items.Exists(i => i.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)))
        {
            items.Add(new ShellNewItem
            {
                Extension = ".txt",
                DisplayName = "Text Document",
                Type = ShellNewType.NullFile
            });
        }

        // 알파벳 순 정렬 (표시 이름 기준)
        items.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

        return items;
    }

    private static string GetFileTypeDisplayName(string extension, RegistryKey classesRoot)
    {
        // 1. HKCR\.ext 의 (Default) 값 → ProgID
        using var extKey = classesRoot.OpenSubKey(extension);
        var progId = extKey?.GetValue("") as string;

        if (!string.IsNullOrEmpty(progId))
        {
            // 2. HKCR\ProgID 의 (Default) 값 → 표시 이름
            using var progIdKey = classesRoot.OpenSubKey(progId);
            var displayName = progIdKey?.GetValue("") as string;
            if (!string.IsNullOrEmpty(displayName))
                return displayName;
        }

        return "";
    }

    private static string? ResolveTemplatePath(string templateFile)
    {
        // 절대 경로면 그대로
        if (Path.IsPathRooted(templateFile) && File.Exists(templateFile))
            return templateFile;

        // ShellNew 폴더에서 검색
        var shellNewDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "ShellNew");
        var resolved = Path.Combine(shellNewDir, templateFile);
        if (File.Exists(resolved)) return resolved;

        // Templates 폴더에서 검색
        var templatesDir = Environment.GetFolderPath(Environment.SpecialFolder.Templates);
        resolved = Path.Combine(templatesDir, templateFile);
        if (File.Exists(resolved)) return resolved;

        return null;
    }
}
