using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using FluentFTP;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// FluentFTP 기반 FTP/FTPS 파일 시스템 프로바이더.
    /// 자동 재연결(NOOP heartbeat), FTPS TOFU 인증서 검증, raw SIZE/MDTM 커맨드 폴백을 지원.
    /// </summary>
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

            ConfigureClient(_client, connInfo);

            await _client.Connect();
            DebugLogger.Log($"[FtpProvider] 연결 성공: {connInfo.Host}:{connInfo.Port} ({connInfo.Protocol})");
            LogServerCapabilities();
        }

        private void ConfigureClient(AsyncFtpClient client, Models.ConnectionInfo connInfo)
        {
            client.Config.ConnectTimeout = 10000;
            client.Config.DataConnectionConnectTimeout = 10000;
            client.Config.ReadTimeout = 15000;

            // 날짜/시간 처리: 서버 시간 그대로 사용 (변환 없이)
            client.Config.TimeConversion = FtpDate.ServerTime;

            // FTPS 설정
            if (connInfo.Protocol == RemoteProtocol.FTPS)
            {
                client.Config.EncryptionMode = FtpEncryptionMode.Explicit;
                ConfigureCertificateValidation(client, connInfo);
            }
        }

        private void LogServerCapabilities()
        {
            if (_client == null) return;
            try
            {
                var hasSize = _client.HasFeature(FtpCapability.SIZE);
                var hasMdtm = _client.HasFeature(FtpCapability.MDTM);
                DebugLogger.Log($"[FtpProvider] 서버 기능 - SIZE:{hasSize}, MDTM:{hasMdtm}, ServerType:{_client.ServerType}");
            }
            catch { /* connect 직후 기능 조회 실패 무시 */ }
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

            ConfigureClient(_client, _connectionInfo);

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

                // ForceList: MLSD 우회하여 LIST 커맨드 강제 사용
                // (MLSD가 size=0을 반환하는 서버 대응)
                var listing = await _client.GetListing(path, FtpListOption.ForceList, ct);

                // 디버그: 첫 번째 파일 항목의 파싱 결과 출력
                foreach (var sample in listing)
                {
                    if (sample.Type == FtpObjectType.File)
                    {
                        DebugLogger.Log($"[FtpProvider] LIST Sample: Name=\"{sample.Name}\", Size={sample.Size}, Modified={sample.Modified}, FullName=\"{sample.FullName}\", Input=\"{sample.Input}\"");
                        break;
                    }
                }

                // Binary 모드 전환 (SIZE 커맨드 선행 조건)
                await _client.Execute("TYPE I", ct);

                foreach (var entry in listing)
                {
                    ct.ThrowIfCancellationRequested();

                    if (entry.Type == FtpObjectType.Directory)
                    {
                        var folderDate = entry.Modified;
                        if (folderDate.Year < 1980) folderDate = DateTime.MinValue;

                        // 폴더 날짜 누락 시 MDTM 시도
                        if (folderDate == DateTime.MinValue)
                            folderDate = await TryGetModifiedTimeRaw(entry.FullName, ct);

                        items.Add(new FolderItem
                        {
                            Name = entry.Name,
                            Path = entry.FullName,
                            DateModified = folderDate
                        });
                    }
                    else if (entry.Type == FtpObjectType.File)
                    {
                        // ── 파일 크기: LIST 파싱 → raw SIZE 커맨드 ──
                        long fileSize = entry.Size;
                        if (fileSize <= 0)
                        {
                            fileSize = await TryGetFileSizeRaw(entry.FullName, ct);
                        }

                        // ── 날짜: LIST 파싱 → raw MDTM 커맨드 ──
                        var fileDate = entry.Modified;
                        if (fileDate.Year < 1980)
                        {
                            fileDate = await TryGetModifiedTimeRaw(entry.FullName, ct);
                        }

                        items.Add(new FileItem
                        {
                            Name = entry.Name,
                            Path = entry.FullName,
                            Size = Math.Max(0, fileSize),
                            DateModified = fileDate,
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

        /// <summary>
        /// FluentFTP 우회: raw SIZE 커맨드로 파일 크기 직접 조회.
        /// 서버 응답 "213 &lt;bytes&gt;" 파싱.
        /// </summary>
        private async Task<long> TryGetFileSizeRaw(string remotePath, CancellationToken ct)
        {
            try
            {
                var reply = await _client!.Execute($"SIZE {remotePath}", ct);
                DebugLogger.Log($"[FtpProvider] SIZE \"{remotePath}\": Code={reply.Code}, Msg=\"{reply.Message}\"");
                if (reply.Code == "213" && long.TryParse(reply.Message.Trim(), out var size))
                    return size;

                // FluentFTP GetFileSize도 시도 (내부에서 TYPE I + SIZE 처리)
                var fluentSize = await _client.GetFileSize(remotePath, -1, ct);
                DebugLogger.Log($"[FtpProvider] GetFileSize \"{remotePath}\": {fluentSize}");
                return fluentSize;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[FtpProvider] SIZE 실패 \"{remotePath}\": {ex.Message}");
                return -1;
            }
        }

        /// <summary>
        /// FluentFTP 우회: raw MDTM 커맨드로 수정 시간 직접 조회.
        /// 서버 응답 "213 YYYYMMDDHHmmss" 파싱.
        /// </summary>
        private async Task<DateTime> TryGetModifiedTimeRaw(string remotePath, CancellationToken ct)
        {
            try
            {
                var reply = await _client!.Execute($"MDTM {remotePath}", ct);
                if (reply.Code == "213")
                {
                    var raw = reply.Message.Trim();
                    // "YYYYMMDDHHmmss" 또는 "YYYYMMDDHHmmss.sss" 포맷
                    var datePart = raw.Contains('.') ? raw[..raw.IndexOf('.')] : raw;
                    if (DateTime.TryParseExact(datePart, "yyyyMMddHHmmss",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var parsed))
                    {
                        return parsed;
                    }
                }
            }
            catch { /* MDTM 미지원 */ }
            return DateTime.MinValue;
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

        /// <summary>
        /// FTP SIZE 커맨드로 원격 파일 크기를 조회.
        /// </summary>
        public async Task<long> GetFileSizeAsync(string path, CancellationToken ct = default)
        {
            if (_client == null) return -1;
            await EnsureConnectedAsync();
            return await _client.GetFileSize(path, -1, ct);
        }

        /// <summary>
        /// 진행률 콜백을 지원하는 FTP 다운로드.
        /// </summary>
        public async Task DownloadWithProgressAsync(string remotePath, Stream destStream, IProgress<long>? progress, CancellationToken ct)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();

            IProgress<FtpProgress>? ftpProgress = progress != null
                ? new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes))
                : null;

            await _client.DownloadStream(destStream, remotePath, 0, ftpProgress, ct);
        }

        /// <summary>
        /// 진행률 콜백을 지원하는 FTP 업로드.
        /// </summary>
        public async Task UploadWithProgressAsync(string remotePath, Stream sourceStream, IProgress<long>? progress, CancellationToken ct)
        {
            if (_client == null) return;
            await EnsureConnectedAsync();

            IProgress<FtpProgress>? ftpProgress = progress != null
                ? new Progress<FtpProgress>(p => progress.Report(p.TransferredBytes))
                : null;

            await _client.UploadStream(sourceStream, remotePath, FtpRemoteExists.Overwrite, false, ftpProgress, ct);
        }

        private void ConfigureCertificateValidation(AsyncFtpClient client, Models.ConnectionInfo connInfo)
        {
            client.ValidateCertificate += (control, e) =>
            {
                var cert = new X509Certificate2(e.Certificate);
                var thumbprint = cert.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256);

                if (!string.IsNullOrEmpty(connInfo.TrustedCertThumbprint))
                {
                    // Compare against previously trusted thumbprint
                    e.Accept = string.Equals(thumbprint, connInfo.TrustedCertThumbprint, StringComparison.OrdinalIgnoreCase);
                    if (!e.Accept)
                        DebugLogger.Log($"[FtpProvider] 인증서 불일치! 예상: {connInfo.TrustedCertThumbprint}, 실제: {thumbprint}");
                }
                else
                {
                    // First connection: trust on first use (TOFU) and save thumbprint
                    connInfo.TrustedCertThumbprint = thumbprint;
                    e.Accept = true;
                    DebugLogger.Log($"[FtpProvider] 인증서 TOFU 등록: {thumbprint}");
                }
            };
        }

        public void Dispose()
        {
            if (_client != null)
            {
                try
                {
                    if (_client.IsConnected)
                    {
                        // UI 스레드 데드락 방지: 스레드풀에서 Disconnect 실행
                        var task = Task.Run(async () => await _client.Disconnect());
                        task.Wait(TimeSpan.FromSeconds(3));
                    }
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
                        await _client.Disconnect().ConfigureAwait(false);
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
