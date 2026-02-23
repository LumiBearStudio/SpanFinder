using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    public class ConnectionManagerService
    {
        private const string ConnectionsFileName = "connections.json";
        private const string CredentialsFileName = "credentials.dat";

        private readonly string _storagePath;

        public ObservableCollection<ConnectionInfo> SavedConnections { get; } = new();

        public ConnectionManagerService()
        {
            try
            {
                _storagePath = Windows.Storage.ApplicationData.Current.LocalFolder.Path;
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Failed to get LocalFolder path: {ex.Message}");
                // Fallback to AppData\Local
                _storagePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Span");
                Directory.CreateDirectory(_storagePath);
            }
        }

        public async Task LoadConnectionsAsync()
        {
            try
            {
                var filePath = Path.Combine(_storagePath, ConnectionsFileName);
                if (!File.Exists(filePath))
                {
                    DebugLogger.Log("[ConnectionManager] No connections file found, starting empty");
                    return;
                }

                var json = await Task.Run(() => File.ReadAllText(filePath, Encoding.UTF8));
                var connections = JsonSerializer.Deserialize<List<ConnectionInfo>>(json, _jsonOptions);

                SavedConnections.Clear();
                if (connections != null)
                {
                    foreach (var conn in connections)
                        SavedConnections.Add(conn);
                }

                DebugLogger.Log($"[ConnectionManager] Loaded {SavedConnections.Count} connections");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Error loading connections: {ex.Message}");
            }
        }

        public async Task SaveConnectionsAsync()
        {
            try
            {
                var filePath = Path.Combine(_storagePath, ConnectionsFileName);
                var json = JsonSerializer.Serialize(SavedConnections.ToList(), _jsonOptions);
                await Task.Run(() => File.WriteAllText(filePath, json, Encoding.UTF8));
                DebugLogger.Log($"[ConnectionManager] Saved {SavedConnections.Count} connections");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[ConnectionManager] Error saving connections: {ex.Message}");
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
            _ = SaveConnectionsAsync();
            DebugLogger.Log($"[ConnectionManager] Connection updated: {updated.DisplayName}");
        }

        public void RemoveConnection(string id)
        {
            var existing = SavedConnections.FirstOrDefault(c => c.Id == id);
            if (existing != null)
            {
                SavedConnections.Remove(existing);
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

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }
}
