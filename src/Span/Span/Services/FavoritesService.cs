using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 즐겨찾기 서비스 구현. Windows Quick Access Shell Namespace를 통해
    /// 고정된 폴더 목록을 읽고, Shell COM verb(pintohome/unpinfromhome)로 추가/제거한다.
    /// </summary>
    public class FavoritesService : IFavoritesService
    {
        // Windows Quick Access shell namespace CLSID
        private const string QuickAccessCLSID = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
        private const string FavoritesOrderKey = "FavoritesOrderJson";

        public List<FavoriteItem> LoadFavorites()
        {
            try
            {
                var favorites = ReadQuickAccess();
                if (favorites.Count > 0)
                {
                    favorites = ApplySavedOrder(favorites);
                    Helpers.DebugLogger.Log($"[FavoritesService] Loaded {favorites.Count} items from Quick Access");
                    return favorites;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Quick Access read failed: {ex.Message}");
            }

            return GetDefaultFavorites();
        }

        public List<FavoriteItem> GetDefaultFavorites()
        {
            var favorites = new List<FavoriteItem>();
            int order = 0;

            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (Directory.Exists(desktopPath))
            {
                favorites.Add(new FavoriteItem
                {
                    Name = LocalizationService.L("Favorites_Desktop"),
                    Path = desktopPath,
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),
                    IconColor = "#6FA8DC",
                    Order = order++
                });
            }

            var downloadsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            if (Directory.Exists(downloadsPath))
            {
                favorites.Add(new FavoriteItem
                {
                    Name = LocalizationService.L("Favorites_Downloads"),
                    Path = downloadsPath,
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),
                    IconColor = "#FFA066",
                    Order = order++
                });
            }

            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            if (Directory.Exists(documentsPath))
            {
                favorites.Add(new FavoriteItem
                {
                    Name = LocalizationService.L("Favorites_Documents"),
                    Path = documentsPath,
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),
                    IconColor = "#6FA8DC",
                    Order = order++
                });
            }

            var picturesPath = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (Directory.Exists(picturesPath))
            {
                favorites.Add(new FavoriteItem
                {
                    Name = LocalizationService.L("Favorites_Pictures"),
                    Path = picturesPath,
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),
                    IconColor = "#93C47D",
                    Order = order++
                });
            }

            return favorites;
        }

        /// <summary>
        /// Read pinned folders from Windows Quick Access via Shell COM.
        /// On Windows 11, the Quick Access namespace contains only pinned items.
        /// </summary>
        private List<FavoriteItem> ReadQuickAccess()
        {
            var favorites = new List<FavoriteItem>();
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return favorites;

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic? folder = shell?.NameSpace(QuickAccessCLSID);
                if (folder == null) return favorites;

                dynamic? items = null;
                try
                {
                    items = folder.Items();
                    int count = (int)items.Count;
                    int order = 0;

                    for (int i = 0; i < count; i++)
                    {
                        dynamic? item = null;
                        try
                        {
                            item = items.Item(i);
                            string? path = item.Path;
                            if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                                continue;

                            string name = item.Name ?? Path.GetFileName(path);
                            var (glyph, color) = GetIconForPath(path);

                            favorites.Add(new FavoriteItem
                            {
                                Name = name,
                                Path = path,
                                IconGlyph = glyph,
                                IconColor = color,
                                Order = order++
                            });
                        }
                        catch
                        {
                            // Skip problematic items (network folders offline, etc.)
                        }
                        finally
                        {
                            if (item != null) try { Marshal.ReleaseComObject(item); } catch { }
                        }
                    }
                }
                finally
                {
                    if (items != null) try { Marshal.ReleaseComObject(items); } catch { }
                    try { Marshal.ReleaseComObject(folder); } catch { }
                }
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.ReleaseComObject(shell); } catch { }
                }
            }

            return favorites;
        }

        /// <summary>
        /// 즐겨찾기 경로 순서를 LocalSettings에 JSON으로 저장.
        /// Quick Access는 항목의 source of truth, 순서는 앱 로컬에서 관리.
        /// </summary>
        public void SaveFavorites(List<FavoriteItem> favorites)
        {
            try
            {
                var paths = favorites.Select(f => f.Path).ToList();
                var json = JsonSerializer.Serialize(paths);
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values[FavoritesOrderKey] = json;
                Helpers.DebugLogger.Log($"[FavoritesService] Saved favorites order ({paths.Count} items)");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] SaveFavorites failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 저장된 순서가 있으면 Quick Access에서 로드한 목록을 재정렬.
        /// 새로 추가된 항목은 끝에 배치, 삭제된 항목은 무시.
        /// </summary>
        private static List<FavoriteItem> ApplySavedOrder(List<FavoriteItem> favorites)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(FavoritesOrderKey, out var raw) && raw is string json)
                {
                    var savedPaths = JsonSerializer.Deserialize<List<string>>(json);
                    if (savedPaths == null || savedPaths.Count == 0) return favorites;

                    // 경로 → FavoriteItem 매핑 (정규화된 경로로 비교)
                    var lookup = new Dictionary<string, FavoriteItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var fav in favorites)
                    {
                        var key = Path.GetFullPath(fav.Path).TrimEnd('\\');
                        lookup.TryAdd(key, fav);
                    }

                    var ordered = new List<FavoriteItem>();
                    var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    // 저장된 순서대로 배치
                    foreach (var savedPath in savedPaths)
                    {
                        var key = Path.GetFullPath(savedPath).TrimEnd('\\');
                        if (lookup.TryGetValue(key, out var item) && used.Add(key))
                        {
                            ordered.Add(item);
                        }
                    }

                    // Quick Access에 있지만 저장 순서에 없는 새 항목은 끝에 추가
                    foreach (var fav in favorites)
                    {
                        var key = Path.GetFullPath(fav.Path).TrimEnd('\\');
                        if (used.Add(key))
                        {
                            ordered.Add(fav);
                        }
                    }

                    if (ordered.Count > 0)
                    {
                        Helpers.DebugLogger.Log($"[FavoritesService] Applied saved order ({ordered.Count} items)");
                        return ordered;
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] ApplySavedOrder failed: {ex.Message}");
            }
            return favorites;
        }

        public List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing)
        {
            if (existing.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return existing.ToList();

            PinToQuickAccess(path);
            var result = LoadFavorites();
            SaveFavorites(result);
            return result;
        }

        public List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing)
        {
            UnpinFromQuickAccess(path);

            // Shell Quick Access 캐시 업데이트는 비동기일 수 있으므로,
            // LoadFavorites()가 stale 데이터를 반환할 경우 로컬에서 직접 제거
            var reloaded = LoadFavorites();
            var normalizedPath = Path.GetFullPath(path).TrimEnd('\\');
            bool stillPresent = reloaded.Any(f =>
                Path.GetFullPath(f.Path).TrimEnd('\\').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));

            if (stillPresent)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Quick Access stale — removing locally: {path}");
                reloaded.RemoveAll(f =>
                    Path.GetFullPath(f.Path).TrimEnd('\\').Equals(normalizedPath, StringComparison.OrdinalIgnoreCase));
            }

            SaveFavorites(reloaded);
            return reloaded;
        }

        // =================================================================
        //  Shell COM: Pin / Unpin
        // =================================================================

        private static void PinToQuickAccess(string path)
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic? folder = shell?.NameSpace(path);
                if (folder == null) return;

                dynamic? self = null;
                try
                {
                    self = folder.Self;
                    // Windows 11: "pintohome", Windows 10: "pintoquickaccess"
                    try { self.InvokeVerb("pintohome"); }
                    catch
                    {
                        try { self.InvokeVerb("pintoquickaccess"); } catch { }
                    }

                    Helpers.DebugLogger.Log($"[FavoritesService] Pinned to Quick Access: {path}");
                }
                finally
                {
                    if (self != null) try { Marshal.ReleaseComObject(self); } catch { }
                    try { Marshal.ReleaseComObject(folder); } catch { }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Pin failed: {ex.Message}");
                try { App.Current.Services.GetService<CrashReportingService>()?.CaptureException(ex, "FavoritesService.PinToQuickAccess"); } catch { }
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.ReleaseComObject(shell); } catch { }
                }
            }
        }

        private static void UnpinFromQuickAccess(string path)
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            // 경로 정규화: trailing backslash 제거 + full path 변환
            var normalizedPath = Path.GetFullPath(path).TrimEnd('\\');

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic? qa = shell?.NameSpace(QuickAccessCLSID);
                if (qa == null) return;

                dynamic? items = null;
                bool found = false;
                try
                {
                    items = qa.Items();
                    int count = (int)items.Count;

                    for (int i = 0; i < count; i++)
                    {
                        dynamic? item = null;
                        try
                        {
                            item = items.Item(i);
                            string? itemPath = item.Path;
                            if (string.IsNullOrEmpty(itemPath)) continue;

                            var normalizedItemPath = Path.GetFullPath(itemPath).TrimEnd('\\');
                            if (string.Equals(normalizedItemPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                // Verb cascading: unpinfromhome → removefromhome → unpinfromquickaccess
                                string[] verbs = ["unpinfromhome", "removefromhome", "unpinfromquickaccess"];
                                foreach (var verb in verbs)
                                {
                                    try
                                    {
                                        item.InvokeVerb(verb);
                                        Helpers.DebugLogger.Log($"[FavoritesService] Unpinned from Quick Access via '{verb}': {path}");
                                        break;
                                    }
                                    catch { }
                                }
                                break;
                            }
                        }
                        catch { }
                        finally
                        {
                            if (item != null) try { Marshal.ReleaseComObject(item); } catch { }
                        }
                    }

                    if (!found)
                        Helpers.DebugLogger.Log($"[FavoritesService] Item not found in Quick Access for unpin: {path}");
                }
                finally
                {
                    if (items != null) try { Marshal.ReleaseComObject(items); } catch { }
                    try { Marshal.ReleaseComObject(qa); } catch { }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Unpin failed: {ex.Message}");
                try { App.Current.Services.GetService<CrashReportingService>()?.CaptureException(ex, "FavoritesService.UnpinFromQuickAccess"); } catch { }
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.ReleaseComObject(shell); } catch { }
                }
            }
        }

        // =================================================================
        //  Icon / Color mapping for special folders
        // =================================================================

        private static (string Glyph, string Color) GetIconForPath(string path)
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picturesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            if (path.Equals(desktopPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#6FA8DC");
            if (path.Equals(downloadsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#FFA066");
            if (path.Equals(documentsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#6FA8DC");
            if (path.Equals(picturesPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#93C47D");
            if (path.Equals(musicPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#B07CD8");
            if (path.Equals(videosPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#E06666");

            return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#FFC857");
        }
    }
}
