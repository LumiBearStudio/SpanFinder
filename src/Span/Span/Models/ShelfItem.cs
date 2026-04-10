using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// File Shelf(임시 수집함)에 담긴 개별 항목.
    /// IsPinned 변경 시 UI 바인딩 갱신을 위해 ObservableObject 상속.
    /// </summary>
    public partial class ShelfItem : ObservableObject
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Path { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = string.Empty;
        public Brush IconBrush { get; set; } = new SolidColorBrush(Microsoft.UI.Colors.Gray);
        public string SourceFolder { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public long FileSize { get; set; }
        public DateTime AddedTime { get; set; } = DateTime.Now;

        /// <summary>핀(잠금) 상태 — Move/Clear 시에도 유지됨.</summary>
        [ObservableProperty]
        private bool _isPinned;
    }
}
