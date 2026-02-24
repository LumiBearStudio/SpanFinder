using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// Windows Shell에 등록된 클라우드 스토리지 프로바이더(iCloud, OneDrive, Dropbox 등)를
    /// 감지하여 사이드바에 표시할 DriveItem 목록을 반환.
    /// DriveInfo.GetDrives()는 물리/네트워크 드라이브만 반환하므로,
    /// Shell Namespace Extension으로 등록된 가상 폴더는 별도 감지 필요.
    /// </summary>
    public class CloudStorageProviderService
    {
        /// <summary>
        /// 모든 클라우드 스토리지 프로바이더를 DriveItem 목록으로 반환.
        /// </summary>
        public List<DriveItem> GetCloudStorageDrives()
        {
            var drives = new List<DriveItem>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var cloudGlyph = IconService.Current?.CloudGlyph ?? "\uEB9C";

            // Layer 1: Registry SyncRootManager (모든 Cloud Files API 프로바이더)
            try
            {
                EnumerateFromSyncRootManager(drives, seenPaths, cloudGlyph);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[CloudStorage] SyncRootManager error: {ex.Message}");
            }

            // Layer 2: Navigation Pane CLSIDs (파일 탐색기 사이드바에 고정된 항목)
            try
            {
                EnumerateFromNavigationPane(drives, seenPaths, cloudGlyph);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[CloudStorage] NavigationPane error: {ex.Message}");
            }

            // Layer 3: 프로바이더별 직접 감지 (위에서 누락된 경우 보완)
            DetectOneDrive(drives, seenPaths, cloudGlyph);
            DetectDropbox(drives, seenPaths, cloudGlyph);
            DetectICloud(drives, seenPaths, cloudGlyph);

            return drives;
        }

        /// <summary>
        /// HKLM\...\SyncRootManager 레지스트리에서 등록된 클라우드 프로바이더 열거.
        /// Windows Cloud Files API를 사용하는 모든 프로바이더가 여기 등록됨.
        /// </summary>
        private static void EnumerateFromSyncRootManager(
            List<DriveItem> drives, HashSet<string> seenPaths, string cloudGlyph)
        {
            const string syncRootKey =
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\SyncRootManager";

            using var rootKey = Registry.LocalMachine.OpenSubKey(syncRootKey);
            if (rootKey == null) return;

            foreach (var subKeyName in rootKey.GetSubKeyNames())
            {
                try
                {
                    using var subKey = rootKey.OpenSubKey(subKeyName);
                    if (subKey == null) continue;

                    var displayName = subKey.GetValue("DisplayNameResource") as string;

                    using var syncRootsKey = subKey.OpenSubKey("UserSyncRoots");
                    if (syncRootsKey == null) continue;

                    foreach (var sidName in syncRootsKey.GetValueNames())
                    {
                        var path = syncRootsKey.GetValue(sidName) as string;
                        if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                            continue;
                        if (!seenPaths.Add(path))
                            continue;

                        drives.Add(CreateCloudDriveItem(
                            ResolveDisplayName(displayName, subKeyName),
                            path, cloudGlyph));
                    }
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[CloudStorage] SyncRoot '{subKeyName}' error: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// HKCU\...\Desktop\NameSpace에서 Navigation Pane에 고정된 CLSID 열거.
        /// 각 CLSID의 TargetFolderPath에서 실제 폴더 경로를 가져옴.
        /// </summary>
        private static void EnumerateFromNavigationPane(
            List<DriveItem> drives, HashSet<string> seenPaths, string cloudGlyph)
        {
            const string nameSpaceKey =
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\Desktop\NameSpace";

            using var nsKey = Registry.CurrentUser.OpenSubKey(nameSpaceKey);
            if (nsKey == null) return;

            foreach (var clsidStr in nsKey.GetSubKeyNames())
            {
                try
                {
                    if (!clsidStr.StartsWith("{") || !clsidStr.EndsWith("}"))
                        continue;

                    // IsPinnedToNameSpaceTree 확인
                    using var mainKey = Registry.CurrentUser.OpenSubKey(
                        $@"Software\Classes\CLSID\{clsidStr}");
                    if (mainKey == null) continue;

                    var isPinned = mainKey.GetValue("System.IsPinnedToNameSpaceTree");
                    if (isPinned is int pinVal && pinVal == 0) continue;

                    // TargetFolderPath에서 실제 경로 가져오기
                    using var propBag = Registry.CurrentUser.OpenSubKey(
                        $@"Software\Classes\CLSID\{clsidStr}\Instance\InitPropertyBag");
                    if (propBag == null) continue;

                    var targetPath = propBag.GetValue("TargetFolderPath") as string;
                    if (string.IsNullOrEmpty(targetPath)) continue;

                    targetPath = Environment.ExpandEnvironmentVariables(targetPath);
                    if (!Directory.Exists(targetPath) || !seenPaths.Add(targetPath))
                        continue;

                    // Display name
                    using var nsSubKey = nsKey.OpenSubKey(clsidStr);
                    var displayName = nsSubKey?.GetValue(null) as string
                                   ?? mainKey.GetValue(null) as string
                                   ?? Path.GetFileName(targetPath);

                    drives.Add(CreateCloudDriveItem(displayName, targetPath, cloudGlyph));
                }
                catch { /* 개별 CLSID 오류 무시 */ }
            }
        }

        private static void DetectOneDrive(
            List<DriveItem> drives, HashSet<string> seenPaths, string cloudGlyph)
        {
            foreach (var envVar in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
            {
                var path = Environment.GetEnvironmentVariable(envVar);
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path) && seenPaths.Add(path))
                {
                    var label = envVar == "OneDriveCommercial" ? "OneDrive - Business" : "OneDrive";
                    drives.Add(CreateCloudDriveItem(label, path, cloudGlyph));
                }
            }
        }

        private static void DetectDropbox(
            List<DriveItem> drives, HashSet<string> seenPaths, string cloudGlyph)
        {
            var infoJson = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Dropbox", "info.json");

            if (File.Exists(infoJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(infoJson));
                    foreach (var account in new[] { "personal", "business" })
                    {
                        if (doc.RootElement.TryGetProperty(account, out var acc)
                            && acc.TryGetProperty("path", out var pathProp))
                        {
                            var path = pathProp.GetString();
                            if (!string.IsNullOrEmpty(path) && Directory.Exists(path)
                                && seenPaths.Add(path))
                            {
                                var label = account == "business" ? "Dropbox Business" : "Dropbox";
                                drives.Add(CreateCloudDriveItem(label, path, cloudGlyph));
                            }
                        }
                    }
                }
                catch { /* JSON 파싱 오류 무시 */ }
            }

            // 기본 경로 폴백
            var defaultPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox");
            if (Directory.Exists(defaultPath) && seenPaths.Add(defaultPath))
                drives.Add(CreateCloudDriveItem("Dropbox", defaultPath, cloudGlyph));
        }

        private static void DetectICloud(
            List<DriveItem> drives, HashSet<string> seenPaths, string cloudGlyph)
        {
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var (folder, label) in new[]
            {
                ("iCloudDrive", "iCloud Drive"),
                ("iCloud Drive", "iCloud Drive"),
            })
            {
                var path = Path.Combine(userProfile, folder);
                if (Directory.Exists(path) && seenPaths.Add(path))
                {
                    drives.Add(CreateCloudDriveItem(label, path, cloudGlyph));
                    break;
                }
            }
        }

        private static DriveItem CreateCloudDriveItem(string name, string path, string iconGlyph)
        {
            return new DriveItem
            {
                Name = name,
                Path = path,
                DriveType = "CloudStorage",
                IconGlyph = iconGlyph,
                IsCloudStorage = true,
            };
        }

        /// <summary>
        /// DisplayNameResource가 리소스 경로(@dll,-123)인 경우 ProviderId에서 추출
        /// </summary>
        private static string ResolveDisplayName(string? displayNameResource, string providerId)
        {
            if (string.IsNullOrEmpty(displayNameResource) || displayNameResource.StartsWith("@"))
            {
                var bangIdx = providerId.IndexOf('!');
                return bangIdx > 0 ? providerId[..bangIdx] : providerId;
            }
            return displayNameResource;
        }
    }
}
