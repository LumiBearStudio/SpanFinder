using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    public class FtpProvider : IFileSystemProvider, IAsyncDisposable, IDisposable
    {
        private AsyncFtpClient? _client;
        private Models.ConnectionInfo? _connectionInfo;
        private string? _password;
        private readonly SemaphoreSlim _connectLock = new(1, 1);

        public string Scheme => "ftp";
        public string DisplayName => "FTP";
        public bool IsConnected => _client?.IsConnected == true;

        public async Task ConnectAsync(Models.ConnectionInfo connInfo, string? password = null)
        {
            _connectionInfo = connInfo;
            _password = password ?? string.Empty;

            _client = new AsyncFtpClient(
                connInfo.Host,
                connInfo.Username,
                _password,
                connInfo.Port);

            _client.Config.ConnectTimeout = 10000;
            _client.Config.DataConnectionConnectTimeout = 10000;
            _client.Config.ReadTimeout = 15000;

            // FTPS 설정
            if (connInfo.Protocol == RemoteProtocol.FTPS)
            {
                _client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                _client.Config.ValidateAnyCertificate = true; // TODO: 인증서 검증 UI 추가
            }

            await _client.Connect();
            DebugLogger.Log($"[FtpProvider] 연결 성공: {connInfo.Host}:{connInfo.Port} ({connInfo.Protocol})");
        }

        /// <summary>
        /// NOOP으로 연결 상태를 확인하고, 끊어졌으면 저장된 자격증명으로 재연결.
        /// 서버 측 유휴 타임아웃으로 끊긴 경우를 처리.
        /// </summary>
        private async Task EnsureConnectedAsync()
        {
            await _connectLock.WaitAsync();
            try
            {
                if (_client == null || _connectionInfo == null)
                    throw new InvalidOperationException("FTP 연결이 초기화되지 않았습니다.");

                if (!_client.IsConnected)
                {
                    DebugLogger.Log("[FtpProvider] 연결 끊김 감지, 재연결 시도...");
                    await ReconnectAsync();
                    return;
                }

                try
                {
                    await _client.Execute("NOOP");
                }
                catch
                {
                    DebugLogger.Log("[FtpProvider] NOOP 실패 (stale 연결), 재연결 시도...");
                    await ReconnectAsync();
                }
            }
            finally
            {
                _connectLock.Release();
            }
        }

        private async Task ReconnectAsync()
        {
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
            }

            _client = new AsyncFtpClient(
                _connectionInfo!.Host,
                _connectionInfo.Username,
                _password ?? string.Empty,
                _connectionInfo.Port);

            _client.Config.ConnectTimeout = 10000;
            _client.Config.DataConnectionConnectTimeout = 10000;
            _client.Config.ReadTimeout = 15000;

            if (_connectionInfo.Protocol == RemoteProtocol.FTPS)
            {
                _client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                _client.Config.ValidateAnyCertificate = true;
            }

            await _client.Connect();
            DebugLogger.Log($"[FtpProvider] 재연결 성공: {_connectionInfo.Host}:{_connectionInfo.Port}");
        }

        public async Task DisconnectAsync()
        {
            if (_client?.IsConnected == true)
            {
                await _client.Disconnect();
                DebugLogger.Log("[FtpProvider] 연결 해제");
            }
        }

        public async Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
        {
            var items = new List<IFileSystemItem>();
            if (_client == null) return items;

            try
            {
                await EnsureConnectedAsync();
                var listing = await _client.GetListing(path, FtpListOption.Auto, ct);
                foreach (var entry in listing)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.Type == FtpObjectType.Directory)
                    {
                        items.Add(new FolderItem
                        {
                            Name = entry.Name,
                            Path = entry.FullName,
                            DateModified = entry.Modified
                        });
                    }
                    else if (entry.Type == FtpObjectType.File)
                    {
                        items.Add(new FileItem
                        {
                            Name = entry.Name,
                            Path = entry.FullName,
                            Size = entry.Size,
                            DateModified = entry.Modified,
                            FileType = System.IO.Path.GetExtension(entry.Name)
                        });
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FtpProvider] GetItemsAsync 오류 ({path}): {ex.Message}");
            }

            return items;
        }

        public async Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            if (_client == null) return false;
            try
            {
                await EnsureConnectedAsync();
                return await _client.DirectoryExists(path, ct) || await _client.FileExists(path, ct);
            }
            catch { return false; }
        }

        public async Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        {
            if (_client == null) return false;
            try
            {
                await EnsureConnectedAsync();
                return await _client.DirectoryExists(path, ct);
            }
            catch { return false; }
        }

        public async Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();
            await _client.CreateDirectory(path, ct);
        }

        public async Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();

            if (await _client.DirectoryExists(path, ct))
            {
                if (recursive)
                    await _client.DeleteDirectory(path, FtpListOption.Recursive, ct);
                else
                    await _client.DeleteDirectory(path, ct);
            }
            else
            {
                await _client.DeleteFile(path, ct);
            }
        }

        public async Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();
            await _client.Rename(oldPath, newPath, ct);
        }

        public async Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();

            using var stream = new MemoryStream();
            await _client.DownloadStream(stream, sourcePath, token: ct);
            stream.Position = 0;
            await _client.UploadStream(stream, destPath, token: ct);
        }

        public async Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            await RenameAsync(sourcePath, destPath, ct);
        }

        public async Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        {
            if (_client == null)
                throw new InvalidOperationException("FTP 연결이 없습니다.");

            await EnsureConnectedAsync();
            var stream = new MemoryStream();
            await _client.DownloadStream(stream, path, token: ct);
            stream.Position = 0;
            return stream;
        }

        public async Task WriteAsync(string path, Stream content, CancellationToken ct = default)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();
            await _client.UploadStream(content, path, token: ct);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected)
                        _client.Disconnect().GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[FtpProvider] Disconnect 중 오류 (무시): {ex.Message}");
                }
                _client.Dispose();
                _client = null;
            }
            _connectLock.Dispose();
        }

        public async ValueTask DisposeAsync()
        {
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected)
                        await _client.Disconnect();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[FtpProvider] Disconnect 중 오류 (무시): {ex.Message}");
                }
                _client.Dispose();
                _client = null;
            }
            _connectLock.Dispose();
        }
    }
}
