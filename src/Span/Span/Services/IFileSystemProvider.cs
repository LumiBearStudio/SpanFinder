using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    public interface IFileSystemProvider
    {
        string Scheme { get; }       // "file", "sftp", "ftp"
        string DisplayName { get; }

        Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default);
        Task<bool> ExistsAsync(string path, CancellationToken ct = default);
        Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default);
        Task CreateDirectoryAsync(string path, CancellationToken ct = default);
        Task DeleteAsync(string path, bool recursive, CancellationToken ct = default);
        Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default);
        Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default);
        Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default);
        Task<Stream> OpenReadAsync(string path, CancellationToken ct = default);
        Task WriteAsync(string path, Stream content, CancellationToken ct = default);
    }
}
