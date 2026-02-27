using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Span.Services
{
    /// <summary>
    /// 파일 확장자 → 아이콘/색상 매핑 정의. icons.json에서 역직렬화된다.
    /// </summary>
    public class IconMapping
    {
        public List<string> Extensions { get; set; } = new();
        public string Icon { get; set; } = "\\uECE0";
        public string Color { get; set; } = "#ABABAB";
    }

    /// <summary>
    /// 아이콘 팩 설정 파일(icons.json/icons-phosphor.json/icons-tabler.json) 구조.
    /// </summary>
    public class IconConfig
    {
        public string DefaultIcon { get; set; } = "file-text-line";
        public string DefaultColor { get; set; } = "#9E9E9E";
        public string FolderIcon { get; set; } = "folder-3-fill"; // \uED53 - Corrected unicode
        public string FolderColor { get; set; } = "#FFD54F";
        public List<IconMapping> Mappings { get; set; } = new();
    }

    /// <summary>
    /// 아이콘 서비스 구현. Remix/Phosphor/Tabler 아이콘 팩을 JSON에서 로드하고,
    /// 파일 확장자별 글리프/브러시를 캐싱하여 제공한다. 싱글턴(IconService.Current)으로 접근.
    /// </summary>
    public class IconService : IIconService
    {
        private IconConfig _config = new();
        private Dictionary<string, (string Icon, Brush Brush)> _cache = new();
        private Brush _defaultBrush;
        private Brush _folderBrush;

        public string FolderIcon => _config.FolderIcon;
        public Brush FolderBrush => _folderBrush;

        /// <summary>
        /// Font family path for the current icon pack.
        /// </summary>
        public string FontFamilyPath { get; private set; } = "/Assets/Fonts/remixicon.ttf#remixicon";

        // Structural icon glyphs (resolved per pack at startup)
        public string FolderGlyph { get; private set; } = "\uED53";
        public string FolderOpenGlyph { get; private set; } = "\uED6F";
        public string FileDefaultGlyph { get; private set; } = "\uECE0";
        public string DriveGlyph { get; private set; } = "\uEC65";
        public string RemovableGlyph { get; private set; } = "\uF285"; // ri-usb-fill
        public string CdRomGlyph { get; private set; } = "\uECA4"; // ri-disc-fill
        public string NetworkGlyph { get; private set; } = "\uEDD4";
        public string ServerGlyph { get; private set; } = "\uEE71";
        public string ChevronRightGlyph { get; private set; } = "\uEA6E";
        public string NewFolderGlyph { get; private set; } = "\uED59";
        public string SplitViewGlyph { get; private set; } = "\uEE8C";
        public string CloudGlyph { get; private set; } = "\uEB9C"; // ri-cloud-fill

        public static IconService Current { get; private set; }

        public IconService()
        {
            Current = this;
        }

        public async Task LoadAsync()
        {
            var settings = App.Current.Services.GetRequiredService<SettingsService>();
            var pack = settings.IconPack;

            // Select JSON file per icon pack
            var jsonFile = pack switch
            {
                "phosphor" => "icons-phosphor.json",
                "tabler" => "icons-tabler.json",
                _ => "icons.json"
            };

            // Select glyph resolver per icon pack
            Func<string, string> resolver = pack switch
            {
                "phosphor" => Helpers.PhosphorIconHelper.GetGlyph,
                "tabler" => Helpers.TablerIconHelper.GetGlyph,
                _ => Helpers.RemixIconHelper.GetGlyph
            };

            // Set font family path per icon pack
            FontFamilyPath = pack switch
            {
                "phosphor" => "/Assets/Fonts/Phosphor.ttf#Phosphor-Fill",
                "tabler" => "/Assets/Fonts/tabler-icons.ttf#tabler-icons",
                _ => "/Assets/Fonts/remixicon.ttf#remixicon"
            };

            // Resolve structural icon glyphs per pack
            // These are UI-level icons (folder, drive, chevron, etc.) with verified codepoints
            (FolderGlyph, FolderOpenGlyph, FileDefaultGlyph, DriveGlyph, NetworkGlyph, ServerGlyph, ChevronRightGlyph, NewFolderGlyph, SplitViewGlyph) = pack switch
            {
                "phosphor" => ("\ue24a", "\ue256", "\ue23a", "\ue2a0", "\ue28e", "\ue2a0", "\ue0a4", "\ue258", "\ue1b0"),
                "tabler" => ("\uf749", "\ufaf7", "\ueaa2", "\ueb1f", "\ueb54", "\ueb1f", "\uea6e", "\ueaae", "\ueebc"),
                _ => ("\uED53", "\uED6F", "\uECE0", "\uEC65", "\uEDD4", "\uEE71", "\uEA6E", "\uED59", "\uEE8C")
            };

            // Additional structural glyphs per pack (Removable/USB, CD-ROM, Cloud)
            (RemovableGlyph, CdRomGlyph, CloudGlyph) = pack switch
            {
                "phosphor" => ("\ue2a0", "\ue0e0", "\ue288"),  // hard-drives, disc, globe
                "tabler" => ("\ueb1f", "\ueb3d", "\uf673"),  // device-floppy, disc, cloud-filled
                _ => ("\uEDFA", "\uECA4", "\uEB9C")   // ri-hard-drive-fill, ri-disc-fill, ri-cloud-fill
            };

            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, jsonFile);
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
                System.Diagnostics.Debug.WriteLine($"Failed to load {jsonFile}: {ex.Message}");
            }

            // Pre-calculate colors & Resolving Icons
            _defaultBrush = GetBrushFromHex(_config.DefaultColor);
            _folderBrush = GetBrushFromHex(_config.FolderColor);

            // Resolve default and folder icons using selected resolver
            _config.DefaultIcon = resolver(_config.DefaultIcon);
            _config.FolderIcon = resolver(_config.FolderIcon);

            _cache.Clear();

            foreach (var mapping in _config.Mappings)
            {
                var brush = GetBrushFromHex(mapping.Color);
                var glyph = resolver(mapping.Icon);

                foreach (var ext in mapping.Extensions)
                {
                    _cache[ext.ToLowerInvariant()] = (glyph, brush);
                }
            }
        }

        /// <summary>
        /// DriveType별 적절한 아이콘 글리프 반환.
        /// Fixed=HDD, Removable=USB, Network=Globe, CDRom=Disc
        /// </summary>
        public string GetDriveGlyph(string driveType)
        {
            return driveType switch
            {
                "Fixed" => DriveGlyph,
                "Removable" => RemovableGlyph,
                "Network" => NetworkGlyph,
                "CDRom" => CdRomGlyph,
                "CloudStorage" => CloudGlyph,
                _ => DriveGlyph
            };
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
