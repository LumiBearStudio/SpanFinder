using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using Span.Models;

namespace Span.Services
{
    public class FavoritesService : IFavoritesService
    {
        // Windows Quick Access shell namespace CLSID
        private const string QuickAccessCLSID = "shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}";

        public List<FavoriteItem> LoadFavorites()
        {
            try
            {
                var favorites = ReadQuickAccess();
                if (favorites.Count > 0)
                {
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
                    Name = "Desktop",
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
                    Name = "Downloads",
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
                    Name = "Documents",
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
                    Name = "Pictures",
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

                dynamic items = folder.Items();
                int count = (int)items.Count;
                int order = 0;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
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
                }
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }

            return favorites;
        }

        /// <summary>
        /// No-op: Windows Quick Access is the single source of truth.
        /// </summary>
        public void SaveFavorites(List<FavoriteItem> favorites)
        {
            // Quick Access is managed by Windows Shell — nothing to persist.
        }

        public List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing)
        {
            if (existing.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase)))
                return existing.ToList();

            PinToQuickAccess(path);
            return LoadFavorites();
        }

        public List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing)
        {
            UnpinFromQuickAccess(path);
            return LoadFavorites();
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

                dynamic self = folder.Self;
                // Windows 11: "pintohome", Windows 10: "pintoquickaccess"
                try { self.InvokeVerb("pintohome"); }
                catch
                {
                    try { self.InvokeVerb("pintoquickaccess"); } catch { }
                }

                Helpers.DebugLogger.Log($"[FavoritesService] Pinned to Quick Access: {path}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Pin failed: {ex.Message}");
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
                }
            }
        }

        private static void UnpinFromQuickAccess(string path)
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType == null) return;

            dynamic? shell = null;
            try
            {
                shell = Activator.CreateInstance(shellType);
                dynamic? qa = shell?.NameSpace(QuickAccessCLSID);
                if (qa == null) return;

                dynamic items = qa.Items();
                int count = (int)items.Count;

                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        dynamic item = items.Item(i);
                        string? itemPath = item.Path;
                        if (string.Equals(itemPath, path, StringComparison.OrdinalIgnoreCase))
                        {
                            // Windows 11: "unpinfromhome", Windows 10: "unpinfromquickaccess"
                            try { item.InvokeVerb("unpinfromhome"); }
                            catch
                            {
                                try { item.InvokeVerb("unpinfromquickaccess"); } catch { }
                            }

                            Helpers.DebugLogger.Log($"[FavoritesService] Unpinned from Quick Access: {path}");
                            break;
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoritesService] Unpin failed: {ex.Message}");
            }
            finally
            {
                if (shell != null)
                {
                    try { Marshal.FinalReleaseComObject(shell); } catch { }
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
