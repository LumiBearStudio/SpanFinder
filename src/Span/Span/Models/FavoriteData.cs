using System;
using System.Collections.Generic;

namespace Span.Models
{
    /// <summary>Persisted favorite (Span-owned list, independent of Windows Quick Access).</summary>
    public class FavoriteEntry
    {
        public string Path { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Color { get; set; }
        public bool IsFile { get; set; }
    }

    /// <summary>User-defined sidebar favorites group.</summary>
    public class FavoriteGroup
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<string> Paths { get; set; } = new();
    }

    /// <summary>Persisted sidebar favorites layout.</summary>
    public class FavoritesLayout
    {
        public List<FavoriteGroup> Groups { get; set; } = new();
        public List<string> UngroupedPaths { get; set; } = new();
    }
}
