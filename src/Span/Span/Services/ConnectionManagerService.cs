using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 원격 연결(SFTP/FTP/SMB) 관리 서비스. 연결 정보를 JSON으로, 자격 증명을 DPAPI로 암호화하여 저장.
    /// ObservableCollection으로 사이드바 바인딩을 지원한다.
    ///
    /// 데이터 안전성 보장:
    /// - SemaphoreSlim으로 동시 쓰기 직렬화
    /// - 원자적 쓰기 (tmp → rename)
    /// - 자동 백업 (.bak) + 손상 시 자동 복원
    /// - 앱 종료 시 FlushAsync()로 미완료 저장 보장
    /// </summary>
    public class ConnectionManagerService
    {
        private const string ConnectionsFileName = "connections.json";
        private const string ConnectionsBackupFileName = "connections.json.bak";
        private const string ConnectionsTempFileName = "connections.json.tmp";
        private const string CredentialsFileName = "credentials.dat";

        private readonly string _storagePath;
        private readonly SemaphoreSlim _saveLock = new(1, 1);
        private bool _isDirty;

        public ObservableCollection<ConnectionInfo> SavedConnections { get; } = new();

        public ConnectionManagerService()
        {
            _storagePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Span");
            Directory.CreateDirectory(_storagePath);
        }

        public async Task LoadConnectionsAsync()
        {
            MigrateFromPackageFolder();

            var filePath = Path.Combine(_storagePath, ConnectionsFileName);
            var bakPath = Path.Combine(_storagePath, ConnectionsBackupFileName);

            List<ConnectionInfo>? connections = null;

            // 1차: 메인 파일에서 로드 시도
            connections = await TryLoadFromFileAsync(filePath);

            // 2차: 메인 파일 손상 시 백업에서 복원
            if (connections == null && File.Exists(bakPath))
            {
                DebugLogger.Log("[ConnectionManager] Main file missing or corrupted, trying backup...");
                connections = await TryLoadFromFileAsync(bakPath);

                if (connections != null)
                {
                    // 백업에서 복원 성공 → 메인 파일 복구
                    try
                    {
                        File.Copy(bakPath, filePath, overwrite: true);
                        DebugLogger.Log("[ConnectionManager] Restored connections from backup");
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.Log($"[ConnectionManager] Failed to restore backup to main: {ex.Message}");
                    }
                }
            }

            if (connections == null)
            {
                if (!File.Exists(filePath) && !File.Exists(bakPath))
                    DebugLogger.Log("[ConnectionManager] No connections file found, starting empty");
                else
                    DebugLogger.Log("[ConnectionManager] WARNING: Both main and backup files corrupted, starting empty");
                return;
            }

            SavedConnections.Clear();
            foreach (var conn in connections)
                SavedConnections.Add(conn);

            DebugLogger.Log($"[ConnectionManager] Loaded {SavedConnections.Count} connections from {filePath}");
        }

        /// <summary>
        /// 파일에서 연결 목록 로드 시도. 실패 시 null 반환.
        /// </summary>
        private async Task<List<ConnectionInfo>?> TryLoadFromFileAsync(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;

                var json = await Task.Run(() => File.ReadAllText(path, Encoding.UTF8));
                if (string.IsNullOrWhiteSpace(json)) return null;

                var connections = JsonSerializer.Deserialize<List<ConnectionInfo>>(json, _jsonOptions);
                return connections;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Failed to load from {Path.GetFileName(path)}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 연결 정보를 디스크에 저장. SemaphoreSlim으로 동시 쓰기 직렬화.
        /// 원자적 쓰기: tmp 파일에 쓴 후 rename. 기존 파일은 .bak으로 백업.
        /// </summary>
        public async Task SaveConnectionsAsync()
        {
            await _saveLock.WaitAsync();
            try
            {
                var filePath = Path.Combine(_storagePath, ConnectionsFileName);
                var tmpPath = Path.Combine(_storagePath, ConnectionsTempFileName);
                var bakPath = Path.Combine(_storagePath, ConnectionsBackupFileName);

                // 스냅샷을 lock 안에서 생성하여 race condition 방지
                var snapshot = SavedConnections.ToList();
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

                await Task.Run(() =>
                {
                    // Step 1: tmp 파일에 쓰기
                    File.WriteAllText(tmpPath, json, Encoding.UTF8);

                    // Step 2: 기존 파일을 .bak으로 백업
                    if (File.Exists(filePath))
                    {
                        try { File.Copy(filePath, bakPath, overwrite: true); }
                        catch { /* 백업 실패해도 저장은 계속 진행 */ }
                    }

                    // Step 3: tmp → 메인 파일로 원자적 이동
                    File.Move(tmpPath, filePath, overwrite: true);
                });

                _isDirty = false;
                DebugLogger.Log($"[ConnectionManager] Saved {snapshot.Count} connections (atomic write)");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] ERROR saving connections: {ex.Message}");
                // tmp 파일 정리
                var tmpPath = Path.Combine(_storagePath, ConnectionsTempFileName);
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        /// <summary>
        /// 앱 종료 시 호출. 변경사항이 있으면 동기적으로 저장을 완료.
        /// </summary>
        public async Task FlushAsync()
        {
            if (!_isDirty) return;
            await SaveConnectionsAsync();
            DebugLogger.Log("[ConnectionManager] Flushed pending changes on shutdown");
        }

        /// <summary>
        /// 동기적 flush. 앱 종료 시 async 호출이 어려운 경우 사용.
        /// </summary>
        public void FlushSync()
        {
            if (!_isDirty) return;
            if (!_saveLock.Wait(TimeSpan.FromSeconds(5))) return;
            try
            {
                var filePath = Path.Combine(_storagePath, ConnectionsFileName);
                var tmpPath = Path.Combine(_storagePath, ConnectionsTempFileName);
                var bakPath = Path.Combine(_storagePath, ConnectionsBackupFileName);

                var snapshot = SavedConnections.ToList();
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                if (File.Exists(filePath))
                {
                    try { File.Copy(filePath, bakPath, overwrite: true); } catch { }
                }
                File.Move(tmpPath, filePath, overwrite: true);

                _isDirty = false;
                DebugLogger.Log($"[ConnectionManager] FlushSync: saved {snapshot.Count} connections");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] ERROR in FlushSync: {ex.Message}");
                try { var tmp = Path.Combine(_storagePath, ConnectionsTempFileName); if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
            finally
            {
                _saveLock.Release();
            }
        }

        public void AddConnection(ConnectionInfo connection)
        {
            if (connection == null) return;

            // Ensure unique ID
            if (SavedConnections.Any(c => c.Id == connection.Id))
            {
                DebugLogger.Log($"[ConnectionManager] Connection with ID {connection.Id} already exists, skipping");
                return;
            }

            SavedConnections.Add(connection);
            _isDirty = true;
            _ = SaveConnectionsAsync();
        }

        public void UpdateConnection(ConnectionInfo updated)
        {
            if (updated == null) return;

            var index = -1;
            for (int i = 0; i < SavedConnections.Count; i++)
            {
                if (SavedConnections[i].Id == updated.Id)
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                DebugLogger.Log($"[ConnectionManager] Connection with ID {updated.Id} not found for update");
                return;
            }

            SavedConnections[index] = updated;
            _isDirty = true;
            _ = SaveConnectionsAsync();
            DebugLogger.Log($"[ConnectionManager] Connection updated: {updated.DisplayName}");
        }

        public void RemoveConnection(string id)
        {
            var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
            if (existing != null)
            {
                SavedConnections.Remove(existing);
                _isDirty = true;
                _ = SaveConnectionsAsync();

                // Also remove stored credential
                RemoveCredential(id);
            }
        }

        public void SaveCredential(string connectionId, string password)
        {
            try
            {
                var credentials = LoadAllCredentials();
                credentials[connectionId] = password;
                WriteAllCredentials(credentials);
                DebugLogger.Log($"[ConnectionManager] Credential saved for connection {connectionId}");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Error saving credential: {ex.Message}");
            }
        }

        public string? LoadCredential(string connectionId)
        {
            try
            {
                var credentials = LoadAllCredentials();
                return credentials.TryGetValue(connectionId, out var password) ? password : null;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Error loading credential: {ex.Message}");
                return null;
            }
        }

        private void RemoveCredential(string connectionId)
        {
            try
            {
                var credentials = LoadAllCredentials();
                if (credentials.Remove(connectionId))
                {
                    WriteAllCredentials(credentials);
                    DebugLogger.Log($"[ConnectionManager] Credential removed for connection {connectionId}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Error removing credential: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads and decrypts the credentials dictionary from disk.
        /// Returns empty dictionary if file doesn't exist or decryption fails.
        /// </summary>
        private Dictionary<string, string> LoadAllCredentials()
        {
            var filePath = Path.Combine(_storagePath, CredentialsFileName);
            if (!File.Exists(filePath))
                return new Dictionary<string, string>();

            var encrypted = File.ReadAllBytes(filePath);
            if (encrypted.Length == 0)
                return new Dictionary<string, string>();

            var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var json = Encoding.UTF8.GetString(decrypted);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }

        /// <summary>
        /// Encrypts and writes the entire credentials dictionary to disk using DPAPI.
        /// </summary>
        private void WriteAllCredentials(Dictionary<string, string> credentials)
        {
            var filePath = Path.Combine(_storagePath, CredentialsFileName);

            if (credentials.Count == 0)
            {
                // Clean up file when no credentials remain
                if (File.Exists(filePath))
                    File.Delete(filePath);
                return;
            }

            var json = JsonSerializer.Serialize(credentials);
            var plainBytes = Encoding.UTF8.GetBytes(json);
            var encrypted = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(filePath, encrypted);
        }

        /// <summary>
        /// One-time migration from MSIX package LocalState to %LOCALAPPDATA%\Span.
        /// 마이그레이션 실패 시 로깅하여 데이터 유실 경로를 추적 가능하게 함.
        /// </summary>
        private void MigrateFromPackageFolder()
        {
            try
            {
                var packagePath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
                if (string.Equals(packagePath, _storagePath, StringComparison.OrdinalIgnoreCase)) return;

                DebugLogger.Log($"[ConnectionManager] Migration check: package={packagePath}, target={_storagePath}");

                foreach (var fileName in new[] { ConnectionsFileName, CredentialsFileName })
                {
                    var src = Path.Combine(packagePath, fileName);
                    var dst = Path.Combine(_storagePath, fileName);

                    if (File.Exists(src) && !File.Exists(dst))
                    {
                        try
                        {
                            File.Copy(src, dst);
                            DebugLogger.Log($"[ConnectionManager] Migrated {fileName} from package folder");
                        }
                        catch (Exception ex)
                        {
                            DebugLogger.Log($"[ConnectionManager] ERROR migrating {fileName}: {ex.Message}");
                        }
                    }
                    else if (File.Exists(src))
                    {
                        DebugLogger.Log($"[ConnectionManager] Skip migration for {fileName}: already exists at target");
                    }
                }
            }
            catch (InvalidOperationException)
            {
                // Package folder not accessible (unpackaged mode) — expected
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Migration error: {ex.Message}");
            }
        }

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
