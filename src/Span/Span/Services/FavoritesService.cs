using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 즐겨찾기 서비스 구현. Span 자체 목록(favorites.json)을 source of truth로 사용한다.
    /// 첫 실행 시 Windows Quick Access에 고정된 폴더를 한 번만 가져오며(마이그레이션),
    /// 이후 추가/제거는 Quick Access(탐색기)에 영향을 주지 않는다. 폴더와 파일 모두 지원.
    /// </summary>
    public class FavoritesService : IFavoritesService
    {
        // Windows Quick Access shell namespace CLSID (one-time migration only)
        private const string QuickAccessCLSID = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";
        // Legacy Quick Access order (migration only)
        private const string FavoritesOrderKey = "FavoritesOrderJson";
        private const string SettingsKey = "FavoritesJson";
        private readonly string _storePath;

        public FavoritesService()
        {
            _storePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Span",
                "favorites.json");
        }

        public List<FavoriteItem> LoadFavorites()
        {
            var entries = LoadEntries();
            if (entries == null)
            {
                entries = MigrateFromQuickAccess();
                SaveEntries(entries);
                Helpers.DebugLogger.Log($"[FavoritesService] Migrated {entries.Count} items from Quick Access");
            }

            var favorites = new List<FavoriteItem>(entries.Count);
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.Path)) continue;
                if (favorites.Any(f => IsSamePath(f.Path, entry.Path))) continue;
                favorites.Add(CreateItem(entry, favorites.Count));
            }
            Helpers.DebugLogger.Log($"[FavoritesService] Loaded {favorites.Count} favorites");
            return favorites;
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? ""),
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? ""),
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? ""),
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? ""),
                    IconColor = "#93C47D",
                    Order = order++
                });
            }

            return favorites;
        }

        /// <summary>
        /// 즐겨찾기 목록(순서, 사용자 이름/색상 포함)을 favorites.json + LocalSettings에 저장.
        /// </summary>
        public void SaveFavorites(List<FavoriteItem> favorites)
        {
            SaveEntries(favorites.Select(f => new FavoriteEntry
            {
                Path = f.Path,
                Name = f.CustomName,
                Color = f.CustomColor,
                IsFile = f.IsFile
            }).ToList());
        }

        public List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing)
        {
            var canonical = CanonicalizePath(path);
            if (existing.Any(f => IsSamePath(f.Path, canonical)))
                return existing.ToList();

            bool isFile = !Directory.Exists(canonical) && File.Exists(canonical);
            var result = existing.ToList();
            result.Add(CreateItem(new FavoriteEntry { Path = canonical, IsFile = isFile }, result.Count));
            SaveFavorites(result);
            Helpers.DebugLogger.Log($"[FavoritesService] Added favorite: {canonical} (file={isFile})");
            return result;
        }

        public List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing)
        {
            var result = existing.Where(f => !IsSamePath(f.Path, path)).ToList();
            SaveFavorites(result);
            Helpers.DebugLogger.Log($"[FavoritesService] Removed favorite: {path}");
            return result;
        }

        /// <summary>Default icon color for a favorite (used when a custom color is reset).</summary>
        public string GetDefaultIconColor(FavoriteItem item)
            => (item.IsFile ? GetIconForFile(item.Path) : GetIconForPath(item.Path)).Color;

        /// <summary>Display name derived from the path (folder/file name, or the root itself).</summary>
        public static string GetDefaultName(string path)
        {
            var name = Path.GetFileName(path.TrimEnd('\\', '/'));
            return string.IsNullOrEmpty(name) ? path : name;
        }

        /// <summary>Full path without trailing separators (drive roots keep theirs).</summary>
        public static string CanonicalizePath(string path)
        {
            try
            {
                var full = Path.GetFullPath(path.Trim());
                var root = Path.GetPathRoot(full) ?? string.Empty;
                return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
            }
            catch
            {
                return path.Trim();
            }
        }

        public static bool IsSamePath(string a, string b)
            => string.Equals(CanonicalizePath(a), CanonicalizePath(b), StringComparison.OrdinalIgnoreCase);

        private FavoriteItem CreateItem(FavoriteEntry entry, int order)
        {
            var (glyph, color) = entry.IsFile ? GetIconForFile(entry.Path) : GetIconForPath(entry.Path);
            var customName = string.IsNullOrWhiteSpace(entry.Name) ? null : entry.Name;
            var customColor = string.IsNullOrWhiteSpace(entry.Color) ? null : entry.Color;
            return new FavoriteItem
            {
                Name = customName ?? GetDefaultName(entry.Path),
                Path = entry.Path,
                IconGlyph = glyph,
                IconColor = customColor ?? color,
                Order = order,
                IsFile = entry.IsFile,
                CustomName = customName,
                CustomColor = customColor
            };
        }

        // =================================================================
        //  Persistence
        // =================================================================

        /// <summary>Returns null when nothing was ever saved (→ migrate from Quick Access).</summary>
        private List<FavoriteEntry>? LoadEntries()
        {
            // Prefer on-disk file so dev, personal install, and reboots share the same list.
            try
            {
                if (File.Exists(_storePath))
                {
                    var fromFile = JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(_storePath));
                    if (fromFile != null)
                        return fromFile;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] File load failed: {ex.Message}");
            }

            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SettingsKey, out var raw) && raw is string json)
                    return JsonSerializer.Deserialize<List<FavoriteEntry>>(json);
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] LocalSettings load failed: {ex.Message}");
            }

            return null;
        }

        private void SaveEntries(List<FavoriteEntry> entries)
        {
            var json = JsonSerializer.Serialize(entries);
            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingsKey] = json; }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FavoritesService] LocalSettings save failed: {ex.Message}"); }
            try
            {
                var dir = Path.GetDirectoryName(_storePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_storePath, json);
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FavoritesService] File save failed: {ex.Message}"); }
        }

        // =================================================================
        //  One-time migration from Windows Quick Access
        // =================================================================

        private List<FavoriteEntry> MigrateFromQuickAccess()
        {
            List<FavoriteItem> items;
            try
            {
                items = ApplySavedOrder(ReadQuickAccess());
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Quick Access read failed: {ex.Message}");
                items = new List<FavoriteItem>();
            }

            if (items.Count == 0)
                items = GetDefaultFavorites();

            return items.Select(f => new FavoriteEntry { Path = f.Path }).ToList();
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

                            favorites.Add(new FavoriteItem
                            {
                                Name = item.Name ?? Path.GetFileName(path),
                                Path = path,
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
        /// 이전 버전이 저장한 Quick Access 순서가 있으면 마이그레이션 시 그대로 적용.
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

                    var ordered = new List<FavoriteItem>();
                    foreach (var savedPath in savedPaths)
                    {
                        var match = favorites.FirstOrDefault(f => IsSamePath(f.Path, savedPath));
                        if (match != null && !ordered.Contains(match))
                            ordered.Add(match);
                    }
                    ordered.AddRange(favorites.Where(f => !ordered.Contains(f)));
                    return ordered;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] ApplySavedOrder failed: {ex.Message}");
            }
            return favorites;
        }

        // =================================================================
        //  Icon / Color mapping
        // =================================================================

        private static (string Glyph, string Color) GetIconForFile(string path)
        {
            var ext = Path.GetExtension(path);
            var icons = IconService.Current;
            var glyph = icons?.GetIcon(ext) ?? icons?.FileDefaultGlyph ?? "";
            var color = "#C8C8C8";
            try
            {
                if (icons?.GetBrush(ext) is Microsoft.UI.Xaml.Media.SolidColorBrush brush)
                    color = $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}";
            }
            catch { }
            return (glyph, color);
        }

        private static (string Glyph, string Color) GetIconForPath(string path)
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picturesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            if (path.Equals(desktopPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#6FA8DC");
            if (path.Equals(downloadsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#FFA066");
            if (path.Equals(documentsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#6FA8DC");
            if (path.Equals(picturesPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#93C47D");
            if (path.Equals(musicPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#B07CD8");
            if (path.Equals(videosPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? ""), "#E06666");

            return ((IconService.Current?.FolderGlyph ?? ""), "#FFC857");
        }
    }

    public interface IFavoritesLayoutService
    {
        FavoritesLayout Load();
        void Save(FavoritesLayout layout);
    }

    public class FavoritesLayoutService : IFavoritesLayoutService
    {
        private const string SettingsKey = "FavoritesLayoutJson";
        private readonly string _fallbackPath;

        public FavoritesLayoutService()
        {
            _fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Span",
                "favorites-layout.json");
        }

        public FavoritesLayout Load()
        {
            // Prefer on-disk file so dev, personal install, and reboots share the same layout.
            try
            {
                if (File.Exists(_fallbackPath))
                {
                    var json = File.ReadAllText(_fallbackPath);
                    var fromFile = JsonSerializer.Deserialize<FavoritesLayout>(json);
                    if (fromFile != null)
                        return fromFile;
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesLayout] File load failed: {ex.Message}");
            }

            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values.TryGetValue(SettingsKey, out var raw) && raw is string json)
                {
                    var layout = JsonSerializer.Deserialize<FavoritesLayout>(json);
                    if (layout != null)
                    {
                        Save(layout);
                        return layout;
                    }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesLayout] LocalSettings load failed: {ex.Message}");
            }

            return new FavoritesLayout();
        }

        public void Save(FavoritesLayout layout)
        {
            var json = JsonSerializer.Serialize(layout);
            try { Windows.Storage.ApplicationData.Current.LocalSettings.Values[SettingsKey] = json; }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FavoritesLayout] LocalSettings save failed: {ex.Message}"); }
            try
            {
                var dir = Path.GetDirectoryName(_fallbackPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(_fallbackPath, json);
            }
            catch (Exception ex) { Helpers.DebugLogger.Log($"[FavoritesLayout] File save failed: {ex.Message}"); }
        }
    }
}
