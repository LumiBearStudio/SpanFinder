using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// File Shelf 비즈니스 로직: 항목 생성, 검증, 경로 추출.
    /// </summary>
    public class ShelfService
    {
        public const int MaxShelfItems = 50;

        private readonly IconService _iconService;

        public ShelfService(IconService iconService)
        {
            _iconService = iconService;
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

                var icon = isDir
                    ? _iconService.FolderGlyph
                    : _iconService.GetIcon(System.IO.Path.GetExtension(path));

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
    }
}
