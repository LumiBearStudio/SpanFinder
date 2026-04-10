using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// File Shelf 비즈니스 로직: 항목 생성, 검증, 경로 추출, 영속성.
    /// </summary>
    public class ShelfService
    {
        public const int MaxShelfItems = 50;
        private const string ShelfItemsSettingKey = "ShelfItemsJson";

        private readonly IconService _iconService;
        private readonly ISettingsService _settings;

        public ShelfService(IconService iconService, ISettingsService settings)
        {
            _iconService = iconService;
            _settings = settings;
        }

        /// <summary>
        /// 경로 목록 → ShelfItem 생성. 기존 항목과 중복되는 경로는 제외.
        /// </summary>
        public List<ShelfItem> CreateShelfItems(List<string> paths, ObservableCollection<ShelfItem> existingItems)
        {
            var existingPaths = new HashSet<string>(existingItems.Select(i => i.Path), StringComparer.OrdinalIgnoreCase);
            var result = new List<ShelfItem>();

            foreach (var path in paths)
            {
                if (existingPaths.Contains(path)) continue;

                var isDir = Directory.Exists(path);
                var isFile = !isDir && File.Exists(path);
                if (!isDir && !isFile) continue;

                var name = System.IO.Path.GetFileName(path);
                if (string.IsNullOrEmpty(name)) name = path;

                var ext = System.IO.Path.GetExtension(path);
                var icon = isDir ? _iconService.FolderGlyph : _iconService.GetIcon(ext);
                var brush = isDir ? _iconService.FolderBrush : _iconService.GetBrush(ext);

                long size = 0;
                if (isFile)
                {
                    try { size = new FileInfo(path).Length; } catch { }
                }

                result.Add(new ShelfItem
                {
                    Path = path,
                    Name = name,
                    IconGlyph = icon,
                    IconBrush = brush,
                    SourceFolder = System.IO.Path.GetDirectoryName(path) ?? string.Empty,
                    IsDirectory = isDir,
                    FileSize = size,
                });

                existingPaths.Add(path);
            }

            return result;
        }

        /// <summary>
        /// Shelf 항목 중 실제 파일/폴더가 사라진 것 검출.
        /// </summary>
        public static List<ShelfItem> ValidateItems(ObservableCollection<ShelfItem> items)
        {
            return items
                .Where(i => !File.Exists(i.Path) && !Directory.Exists(i.Path))
                .ToList();
        }

        /// <summary>
        /// ShelfItem 컬렉션에서 경로 목록만 추출 (FileOperationManager용).
        /// </summary>
        public static List<string> GetPaths(ObservableCollection<ShelfItem> items)
        {
            return items.Select(i => i.Path).ToList();
        }

        // ── 영속성 (Persistence) ────────────────────────────────

        /// <summary>영속화용 DTO (경로 + 핀 상태).</summary>
        private record ShelfItemDto(string Path, bool IsPinned = false);

        /// <summary>
        /// Shelf 항목을 설정에 저장. 앱 종료 시 호출.
        /// </summary>
        public void SaveShelfItems(ObservableCollection<ShelfItem> items)
        {
            try
            {
                var dtos = items.Select(i => new ShelfItemDto(i.Path, i.IsPinned)).ToList();
                var json = JsonSerializer.Serialize(dtos);
                _settings.Set(ShelfItemsSettingKey, json);
                Helpers.DebugLogger.Log($"[ShelfService] Saved {dtos.Count} shelf items");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShelfService] Save failed: {ex.Message}");
            }
        }

        /// <summary>
        /// 설정에서 Shelf 항목을 복원. 실제 존재하는 경로만 ShelfItem으로 재생성.
        /// 기존 List&lt;string&gt; 포맷도 하위 호환 지원.
        /// </summary>
        public List<ShelfItem> LoadShelfItems()
        {
            try
            {
                var json = _settings.Get(ShelfItemsSettingKey, string.Empty);
                if (string.IsNullOrEmpty(json)) return new List<ShelfItem>();

                // 새 포맷(DTO 리스트) 시도
                List<ShelfItemDto>? dtos = null;
                try
                {
                    dtos = JsonSerializer.Deserialize<List<ShelfItemDto>>(json);
                }
                catch
                {
                    // 하위 호환: 기존 List<string> 포맷
                    var paths = JsonSerializer.Deserialize<List<string>>(json);
                    if (paths != null)
                        dtos = paths.Select(p => new ShelfItemDto(p, false)).ToList();
                }

                if (dtos == null || dtos.Count == 0) return new List<ShelfItem>();

                var pathList = dtos.Select(d => d.Path).ToList();
                var empty = new ObservableCollection<ShelfItem>();
                var items = CreateShelfItems(pathList, empty);

                // 핀 상태 복원
                var pinMap = dtos.Where(d => d.IsPinned).Select(d => d.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var item in items)
                {
                    if (pinMap.Contains(item.Path))
                        item.IsPinned = true;
                }

                Helpers.DebugLogger.Log($"[ShelfService] Loaded {items.Count} shelf items (from {dtos.Count} saved)");
                return items;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShelfService] Load failed: {ex.Message}");
                return new List<ShelfItem>();
            }
        }
    }
}
