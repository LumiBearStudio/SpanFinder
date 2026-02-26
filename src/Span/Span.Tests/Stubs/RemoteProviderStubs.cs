using Span.Models;
using Span.Services;

namespace Span.Services;

/// <summary>
/// Minimal stub for FtpProvider to satisfy CopyFileOperation compilation.
/// Tests never exercise remote paths, so these methods are unreachable.
/// </summary>
public class FtpProvider : IFileSystemProvider
{
    public string Scheme => "ftp";
    public string DisplayName => "FTP Stub";

    public Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IFileSystemItem>>(Array.Empty<IFileSystemItem>());

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MoveAsync(string source, string dest, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task WriteAsync(string path, Stream data, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DownloadWithProgressAsync(string remotePath, Stream destStream, IProgress<long>? progress, CancellationToken ct)
        => Task.CompletedTask;

    public Task UploadWithProgressAsync(string remotePath, Stream sourceStream, IProgress<long>? progress, CancellationToken ct)
        => Task.CompletedTask;

    public Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct)
        => Task.FromResult(0L);
}

/// <summary>
/// Minimal stub for SftpProvider to satisfy CopyFileOperation compilation.
/// </summary>
public class SftpProvider : IFileSystemProvider
{
    public string Scheme => "sftp";
    public string DisplayName => "SFTP Stub";

    public Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<IFileSystemItem>>(Array.Empty<IFileSystemItem>());

    public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task MoveAsync(string source, string dest, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        => Task.FromResult<Stream>(new MemoryStream());

    public Task WriteAsync(string path, Stream data, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DownloadWithProgressAsync(string remotePath, Stream destStream, IProgress<long>? progress, CancellationToken ct)
        => Task.CompletedTask;

    public Task UploadWithProgressAsync(string remotePath, Stream sourceStream, IProgress<long>? progress, CancellationToken ct)
        => Task.CompletedTask;

    public Task<long> GetFileSizeAsync(string remotePath, CancellationToken ct)
        => Task.FromResult(0L);
}
