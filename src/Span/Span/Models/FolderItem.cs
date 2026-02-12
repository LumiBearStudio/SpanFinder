using System;
using System.Collections.Generic;

namespace Span.Models
{
    public class FolderItem : IFileSystemItem
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public DateTime DateModified { get; set; }
        public List<FileItem> Files { get; set; } = new List<FileItem>();
        public List<FolderItem> SubFolders { get; set; } = new List<FolderItem>();

        // Unified collection for UI
        public System.Collections.ObjectModel.ObservableCollection<IFileSystemItem> Children { get; set; } = new();

        // UI Helper
        public string IconGlyph => "\uEEA7"; // RemixIcon: FolderFill
    }
}
