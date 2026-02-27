using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 파일 시스템 프로바이더 추상화 인터페이스.
    /// 로컬(LocalFileSystemProvider), FTP(FtpProvider), SFTP(SftpProvider)가 구현하여
    /// FileSystemRouter가 URI 스킴에 따라 적절한 프로바이더로 라우팅한다.
    /// </summary>
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
