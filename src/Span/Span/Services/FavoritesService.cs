using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Span.Models;

namespace Span.Services
{
    public class FavoritesService : IFavoritesService
    {
        private const string FavoritesKey = "FavoritesData";

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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),  // RemixIcon: FolderFill
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),  // RemixIcon: FolderFill
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),  // RemixIcon: FolderFill
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
                    IconGlyph = (IconService.Current?.FolderGlyph ?? "\uED53"),  // RemixIcon: FolderFill
                    IconColor = "#93C47D",
                    Order = order++
                });
            }

            return favorites;
        }

        public List<FavoriteItem> LoadFavorites()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                if (settings.Values[FavoritesKey] is Windows.Storage.ApplicationDataCompositeValue composite)
                {
                    int count = (int)(composite["Count"] ?? 0);
                    var favorites = new List<FavoriteItem>(count);

                    for (int i = 0; i < count; i++)
                    {
                        favorites.Add(new FavoriteItem
                        {
                            Name = composite[$"N{i}"] as string ?? "",
                            Path = composite[$"P{i}"] as string ?? "",
                            IconGlyph = composite[$"G{i}"] as string ?? "",
                            IconColor = composite[$"C{i}"] as string ?? "#FFFFFF",
                            Order = i
                        });
                    }

                    // Always apply correct RemixIcons glyphs (migrates from any old font glyphs)
                    foreach (var fav in favorites)
                    {
                        var (glyph, color) = GetIconForPath(fav.Path);
                        fav.IconGlyph = glyph;
                        fav.IconColor = color;
                    }
                    SaveFavorites(favorites);

                    return favorites;
                }
            }
            catch (Exception ex)
            {
                Span.Helpers.DebugLogger.Log($"[FavoritesService] Error loading favorites: {ex.Message}");
            }

            return GetDefaultFavorites();
        }

        /// <summary>
        /// Determine the correct Segoe Fluent Icons glyph and color for a given path.
        /// Special folders get unique icons; all others get the standard folder icon.
        /// </summary>
        private static (string Glyph, string Color) GetIconForPath(string path)
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            var downloadsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var picturesPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Pictures");
            var musicPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var videosPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            // Use current icon pack's folder glyph
            // Special folders get unique colors
            if (path.Equals(desktopPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#6FA8DC");   // FolderFill - blue
            if (path.Equals(downloadsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#FFA066");   // FolderFill - orange
            if (path.Equals(documentsPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#6FA8DC");   // FolderFill - blue
            if (path.Equals(picturesPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#93C47D");   // FolderFill - green
            if (path.Equals(musicPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#B07CD8");   // FolderFill - purple
            if (path.Equals(videosPath, StringComparison.OrdinalIgnoreCase))
                return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#E06666");   // FolderFill - red

            // Default: same folder icon as miller columns (FolderItem.IconGlyph)
            return ((IconService.Current?.FolderGlyph ?? "\uED53"), "#FFC857");       // FolderFill - yellow
        }

        public void SaveFavorites(List<FavoriteItem> favorites)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                var composite = new Windows.Storage.ApplicationDataCompositeValue
                {
                    ["Count"] = favorites.Count
                };

                for (int i = 0; i < favorites.Count; i++)
                {
                    var item = favorites[i];
                    composite[$"N{i}"] = item.Name;
                    composite[$"P{i}"] = item.Path;
                    composite[$"G{i}"] = item.IconGlyph;
                    composite[$"C{i}"] = item.IconColor;
                }

                settings.Values[FavoritesKey] = composite;
            }
            catch (Exception ex)
            {
                Span.Helpers.DebugLogger.Log($"[FavoritesService] Error saving favorites: {ex.Message}");
            }
        }

        public List<FavoriteItem> AddFavorite(string path, List<FavoriteItem> existing)
        {
            var updated = new List<FavoriteItem>(existing);
            int maxOrder = updated.Count > 0 ? updated.Max(f => f.Order) : -1;

            var (glyph, color) = GetIconForPath(path);
            updated.Add(new FavoriteItem
            {
                Name = Path.GetFileName(path),
                Path = path,
                IconGlyph = glyph,
                IconColor = color,
                Order = maxOrder + 1
            });

            return updated;
        }

        public List<FavoriteItem> RemoveFavorite(string path, List<FavoriteItem> existing)
        {
            return existing
                .Where(f => !f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }
}
