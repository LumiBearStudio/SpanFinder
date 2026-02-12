using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Span.Services
{
    public class IconMapping
    {
        public List<string> Extensions { get; set; } = new();
        public string Icon { get; set; } = "\\uECE0";
        public string Color { get; set; } = "#ABABAB";
    }

    public class IconConfig
    {
        public string DefaultIcon { get; set; } = "file-text-line";
        public string DefaultColor { get; set; } = "#9E9E9E";
        public string FolderIcon { get; set; } = "folder-3-fill"; // \uED53 - Corrected unicode
        public string FolderColor { get; set; } = "#FFD54F";
        public List<IconMapping> Mappings { get; set; } = new();
    }

    public class IconService
    {
        private IconConfig _config = new();
        private Dictionary<string, (string Icon, Brush Brush)> _cache = new();
        private Brush _defaultBrush;
        private Brush _folderBrush;

        public string FolderIcon => _config.FolderIcon;
        public Brush FolderBrush => _folderBrush;

        public static IconService Current { get; private set; }

        public IconService()
        {
            Current = this;
        }

        public async Task LoadAsync()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "icons.json");
                if (File.Exists(path))
                {
                    string json = await File.ReadAllTextAsync(path);

                    var options = new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true,
                        ReadCommentHandling = JsonCommentHandling.Skip,
                        AllowTrailingCommas = true
                    };
                    _config = JsonSerializer.Deserialize<IconConfig>(json, options) ?? new IconConfig();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load icons.json: {ex.Message}");
            }

            // Pre-calculate colors & Resolving Icons
            _defaultBrush = GetBrushFromHex(_config.DefaultColor);
            _folderBrush = GetBrushFromHex(_config.FolderColor);

            // Resolve default and folder icons
            _config.DefaultIcon = Helpers.RemixIconHelper.GetGlyph(_config.DefaultIcon);
            _config.FolderIcon = Helpers.RemixIconHelper.GetGlyph(_config.FolderIcon);

            _cache.Clear();

            foreach (var mapping in _config.Mappings)
            {
                var brush = GetBrushFromHex(mapping.Color);
                var glyph = Helpers.RemixIconHelper.GetGlyph(mapping.Icon);

                foreach (var ext in mapping.Extensions)
                {
                    _cache[ext.ToLowerInvariant()] = (glyph, brush);
                }
            }
        }

        public string GetIcon(string extension)
        {
            var ext = (extension ?? "").ToLowerInvariant().TrimStart('.');
            if (_cache.TryGetValue(ext, out var val))
            {
                return val.Icon;
            }
            return _config.DefaultIcon;
        }

        public Brush GetBrush(string extension)
        {
            var ext = (extension ?? "").ToLowerInvariant().TrimStart('.');
            if (_cache.TryGetValue(ext, out var val))
            {
                return val.Brush;
            }
            return _defaultBrush;
        }

        private static Brush GetBrushFromHex(string hex)
        {
            try
            {
                hex = hex.Replace("#", "");
                if (hex.Length == 6)
                {
                    byte r = Convert.ToByte(hex.Substring(0, 2), 16);
                    byte g = Convert.ToByte(hex.Substring(2, 2), 16);
                    byte b = Convert.ToByte(hex.Substring(4, 2), 16);
                    return new SolidColorBrush(Color.FromArgb(255, r, g, b));
                }
            }
            catch { }
            return new SolidColorBrush(Colors.Gray);
        }
    }
}
