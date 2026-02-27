using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// 파일 시스템 라우터. URI 스킴(file/sftp/ftp)에 따라 적절한 IFileSystemProvider를 반환하고,
    /// 활성 원격 연결을 URI prefix 기반 longest-match로 관리한다.
    /// </summary>
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
        /// Task.Run으로 스레드풀에서 실행하여 UI 스레드 데드락 방지.
        /// </summary>
        private static void DisposeProvider(IFileSystemProvider provider)
        {
            try
            {
                if (provider is IAsyncDisposable ad)
                {
                    // UI SynchronizationContext 우회: 스레드풀에서 비동기 dispose 실행
                    // 타임아웃 5초 — FTP 서버 무응답 시 무한 대기 방지
                    var task = Task.Run(async () => await ad.DisposeAsync());
                    if (!task.Wait(TimeSpan.FromSeconds(5)))
                    {
                        Helpers.DebugLogger.Log("[FileSystemRouter] Provider dispose timed out (5s), forcing cleanup");
                        // 타임아웃 시 동기 Dispose로 강제 정리 시도
                        if (provider is IDisposable d)
                        {
                            try { d.Dispose(); } catch { }
                        }
                    }
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
            {
                var path = string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath;
                // Uri.AbsolutePath는 URL 인코딩된 형태 (%20, %26 등)를 반환하지만
                // FTP/SFTP 커맨드는 raw 경로가 필요하므로 디코딩
                return Uri.UnescapeDataString(path);
            }
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
