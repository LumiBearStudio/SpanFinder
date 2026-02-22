using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Renci.SshNet;
using Renci.SshNet.Sftp;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    public class SftpProvider : IFileSystemProvider, IDisposable
    {
        private SftpClient? _client;
        private Models.ConnectionInfo? _connectionInfo;
        private string? _password;
        private readonly object _connectLock = new();

        public string Scheme => "sftp";
        public string DisplayName => "SFTP";
        public bool IsConnected => _client?.IsConnected == true;

        public async Task ConnectAsync(Models.ConnectionInfo connInfo, string? password = null)
        {
            _connectionInfo = connInfo;
            _password = password;

            await Task.Run(() =>
            {
                _client = CreateSftpClient(connInfo, password);
                _client.Connect();
                DebugLogger.Log($"[SftpProvider] 연결 성공: {connInfo.Host}:{connInfo.Port}");
            });
        }

        public void Disconnect()
        {
            if (_client?.IsConnected == true)
            {
                _client.Disconnect();
                DebugLogger.Log("[SftpProvider] 연결 해제");
            }
        }

        /// <summary>
        /// 연결 상태를 확인하고, 끊어졌으면 저장된 자격증명으로 재연결.
        /// SSH keepalive가 없으면 서버 측 유휴 타임아웃으로 끊길 수 있음.
        /// </summary>
        private void EnsureConnected()
        {
            lock (_connectLock)
            {
                if (_client == null || _connectionInfo == null)
                    throw new InvalidOperationException("SFTP 연결이 초기화되지 않았습니다.");

                if (!_client.IsConnected)
                {
                    DebugLogger.Log("[SftpProvider] 연결 끊김 감지, 재연결 시도...");
                    Reconnect();
                    return;
                }

                try
                {
                    _client.GetAttributes("/");
                }
                catch
                {
                    DebugLogger.Log("[SftpProvider] stale 연결 감지, 재연결 시도...");
                    Reconnect();
                }
            }
        }

        private void Reconnect()
        {
            if (_client != null)
            {
                try { _client.Dispose(); } catch { }
            }

            _client = CreateSftpClient(_connectionInfo!, _password);
            _client.Connect();
            DebugLogger.Log($"[SftpProvider] 재연결 성공: {_connectionInfo!.Host}:{_connectionInfo.Port}");
        }

        private static SftpClient CreateSftpClient(Models.ConnectionInfo connInfo, string? password)
        {
            Renci.SshNet.ConnectionInfo sshConnInfo;

            if (connInfo.AuthMethod == AuthMethod.SshKey && !string.IsNullOrEmpty(connInfo.SshKeyPath))
            {
                var keyFile = string.IsNullOrEmpty(password)
                    ? new PrivateKeyFile(connInfo.SshKeyPath)
                    : new PrivateKeyFile(connInfo.SshKeyPath, password);

                sshConnInfo = new Renci.SshNet.ConnectionInfo(
                    connInfo.Host, connInfo.Port, connInfo.Username,
                    new PrivateKeyAuthenticationMethod(connInfo.Username, keyFile));
            }
            else
            {
                sshConnInfo = new Renci.SshNet.ConnectionInfo(
                    connInfo.Host, connInfo.Port, connInfo.Username,
                    new PasswordAuthenticationMethod(connInfo.Username, password ?? string.Empty));
            }

            sshConnInfo.Timeout = TimeSpan.FromSeconds(10);
            return new SftpClient(sshConnInfo);
        }

        public Task<IReadOnlyList<IFileSystemItem>> GetItemsAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                var items = new List<IFileSystemItem>();
                if (_client == null) return (IReadOnlyList<IFileSystemItem>)items;

                try
                {
                    EnsureConnected();
                    var listing = _client.ListDirectory(path);
                    foreach (var entry in listing)
                    {
                        ct.ThrowIfCancellationRequested();

                        if (entry.Name == "." || entry.Name == "..") continue;

                        if (entry.IsDirectory)
                        {
                            items.Add(new FolderItem
                            {
                                Name = entry.Name,
                                Path = entry.FullName,
                                DateModified = entry.LastWriteTime
                            });
                        }
                        else if (entry.IsRegularFile)
                        {
                            items.Add(new FileItem
                            {
                                Name = entry.Name,
                                Path = entry.FullName,
                                Size = entry.Length,
                                DateModified = entry.LastWriteTime,
                                FileType = System.IO.Path.GetExtension(entry.Name)
                            });
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SftpProvider] GetItemsAsync 오류 ({path}): {ex.Message}");
                }

                return (IReadOnlyList<IFileSystemItem>)items;
            }, ct);
        }

        public Task<bool> ExistsAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return false;
                try
                {
                    EnsureConnected();
                    return _client.Exists(path);
                }
                catch { return false; }
            }, ct);
        }

        public Task<bool> IsDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return false;
                try
                {
                    EnsureConnected();
                    var attrs = _client.GetAttributes(path);
                    return attrs.IsDirectory;
                }
                catch { return false; }
            }, ct);
        }

        public Task CreateDirectoryAsync(string path, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return;
                EnsureConnected();
                _client.CreateDirectory(path);
            }, ct);
        }

        public Task DeleteAsync(string path, bool recursive, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return;
                EnsureConnected();

                var attrs = _client.GetAttributes(path);
                if (attrs.IsDirectory)
                {
                    if (recursive)
                        DeleteDirectoryRecursive(path, ct);
                    else
                        _client.DeleteDirectory(path);
                }
                else
                {
                    _client.DeleteFile(path);
                }
            }, ct);
        }

        public Task RenameAsync(string oldPath, string newPath, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return;
                EnsureConnected();
                _client.RenameFile(oldPath, newPath);
            }, ct);
        }

        public Task CopyAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            return Task.Run(async () =>
            {
                if (_client == null) return;
                EnsureConnected();

                using var stream = new MemoryStream();
                _client.DownloadFile(sourcePath, stream);
                stream.Position = 0;
                _client.UploadFile(stream, destPath);
            }, ct);
        }

        public Task MoveAsync(string sourcePath, string destPath, CancellationToken ct = default)
        {
            return RenameAsync(sourcePath, destPath, ct);
        }

        public Task<Stream> OpenReadAsync(string path, CancellationToken ct = default)
        {
            return Task.Run<Stream>(() =>
            {
                if (_client == null)
                    throw new InvalidOperationException("SFTP 연결이 없습니다.");

                EnsureConnected();
                var stream = new MemoryStream();
                _client.DownloadFile(path, stream);
                stream.Position = 0;
                return stream;
            }, ct);
        }

        public Task WriteAsync(string path, Stream content, CancellationToken ct = default)
        {
            return Task.Run(() =>
            {
                if (_client == null) return;
                EnsureConnected();
                _client.UploadFile(content, path);
            }, ct);
        }

        private void DeleteDirectoryRecursive(string path, CancellationToken ct)
        {
            if (_client == null) return;

            foreach (var entry in _client.ListDirectory(path))
            {
                ct.ThrowIfCancellationRequested();
                if (entry.Name == "." || entry.Name == "..") continue;

                if (entry.IsDirectory)
                    DeleteDirectoryRecursive(entry.FullName, ct);
                else
                    _client.DeleteFile(entry.FullName);
            }
            _client.DeleteDirectory(path);
        }

        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected) _client.Disconnect();
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[SftpProvider] Disconnect 중 오류 (무시): {ex.Message}");
                }
                _client.Dispose();
                _client = null;
            }
        }
    }
}
