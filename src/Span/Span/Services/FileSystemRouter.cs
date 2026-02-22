using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Span.Services
{
    public class FileSystemRouter
    {
        private readonly Dictionary<string, IFileSystemProvider> _providers = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, IFileSystemProvider> _activeConnections = new(StringComparer.OrdinalIgnoreCase);
        private IFileSystemProvider? _defaultProvider;

        public void RegisterProvider(IFileSystemProvider provider)
        {
            _providers[provider.Scheme] = provider;

            // First registered provider (expected: "file") becomes default
            _defaultProvider ??= provider;
        }

        public IFileSystemProvider GetProvider(string path)
        {
            // Check for URI scheme (e.g. "sftp://host/path")
            if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Scheme))
            {
                // Local Windows paths parse as "file" scheme (C:\... → file:///C:/...)
                // For those, fall through to default provider
                if (!string.Equals(uri.Scheme, "file", StringComparison.OrdinalIgnoreCase)
                    && _providers.TryGetValue(uri.Scheme, out var schemeProvider))
                {
                    return schemeProvider;
                }
            }

            if (_defaultProvider is null)
                throw new InvalidOperationException("No file system providers registered.");

            return _defaultProvider;
        }

        public IReadOnlyCollection<IFileSystemProvider> GetAllProviders() => _providers.Values;

        // ── 활성 연결 관리 (URI prefix 키) ──

        public void RegisterConnection(string uriPrefix, IFileSystemProvider provider)
        {
            _activeConnections[uriPrefix.TrimEnd('/')] = provider;
        }

        public void UnregisterConnection(string uriPrefix)
        {
            var key = uriPrefix.TrimEnd('/');
            if (_activeConnections.TryGetValue(key, out var provider))
            {
                _activeConnections.Remove(key);
                DisposeProvider(provider);
            }
        }

        public IFileSystemProvider? GetConnectionForPath(string fullPath)
        {
            // Longest prefix match: 가장 긴 매칭 prefix를 반환하여 정확한 연결 반환
            IFileSystemProvider? bestMatch = null;
            int bestLength = 0;
            foreach (var kvp in _activeConnections)
            {
                if (fullPath.StartsWith(kvp.Key, StringComparison.OrdinalIgnoreCase)
                    && kvp.Key.Length > bestLength)
                {
                    bestMatch = kvp.Value;
                    bestLength = kvp.Key.Length;
                }
            }
            return bestMatch;
        }

        public void DisconnectAll()
        {
            foreach (var kvp in _activeConnections)
            {
                DisposeProvider(kvp.Value);
            }
            _activeConnections.Clear();
        }

        /// <summary>
        /// IAsyncDisposable과 IDisposable 모두 처리하여 FTP/SFTP 연결 정리.
        /// </summary>
        private static void DisposeProvider(IFileSystemProvider provider)
        {
            try
            {
                if (provider is IAsyncDisposable ad)
                {
                    // 동기 컨텍스트에서 비동기 dispose 실행
                    ad.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
                else if (provider is IDisposable d)
                {
                    d.Dispose();
                }
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[FileSystemRouter] Provider dispose 오류 (무시): {ex.Message}");
            }
        }

        // ── URI 헬퍼 (정적) ──

        public static bool IsRemotePath(string path)
            => !string.IsNullOrEmpty(path) && path.Contains("://") && !path.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

        public static string ExtractRemotePath(string fullUri)
        {
            if (Uri.TryCreate(fullUri, UriKind.Absolute, out var uri))
                return string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
            return "/";
        }

        public static string GetUriPrefix(string fullUri)
        {
            if (Uri.TryCreate(fullUri, UriKind.Absolute, out var uri))
            {
                var userInfo = string.IsNullOrEmpty(uri.UserInfo) ? "" : uri.UserInfo + "@";
                return $"{uri.Scheme}://{userInfo}{uri.Host}:{uri.Port}";
            }
            return fullUri;
        }
    }
}
