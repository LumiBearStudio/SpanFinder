using System;

namespace Span.Helpers;

/// <summary>
/// Classifies UNC paths (Issue #67).
///
/// Windows does not expose <c>\\server</c> as a directory, so <c>Directory.Exists</c> is
/// false for a server root even when the server is perfectly reachable and Explorer can
/// browse it. Code that gates navigation on <c>Directory.Exists</c> therefore rejects
/// <c>\\dave-mba</c> while accepting <c>\\dave-mba\lanshared</c> — which is exactly the
/// reported symptom. Listing a server's shares needs <c>NetShareEnum</c> instead.
///
/// Pure string classification, no I/O — safe to call on the UI thread.
/// </summary>
internal static class UncPathHelper
{
    /// <summary>
    /// UNC roots that are shell namespace extensions rather than file-system servers.
    ///
    /// <c>\\wsl.localhost</c> is the Linux node in the shell namespace; only the level
    /// below it (<c>\\wsl.localhost\Ubuntu</c>) is a real path served by the P9 redirector.
    /// Verified: <c>Directory.Exists(@"\\wsl.localhost")</c> is false while
    /// <c>Directory.Exists(@"\\wsl.localhost\Ubuntu")</c> is true. Enumerating shares on
    /// these fails, so they must not be routed to the server-root path.
    /// </summary>
    private static readonly string[] ShellNamespaceRoots = { "wsl.localhost", "wsl$" };

    /// <summary>True for any path in UNC form (<c>\\...</c>), including device paths.</summary>
    internal static bool IsUnc(string? path) =>
        !string.IsNullOrEmpty(path) && path.Length >= 2 && path[0] == '\\' && path[1] == '\\';

    /// <summary>
    /// True for <c>\\wsl.localhost</c> and <c>\\wsl$</c> — with or without a trailing
    /// separator, but not for anything beneath them.
    /// </summary>
    internal static bool IsShellNamespaceRoot(string? path)
    {
        var server = ServerNameOfRoot(path);
        if (server is null) return false;

        foreach (var known in ShellNamespaceRoots)
        {
            if (string.Equals(server, known, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True for a server root whose shares can be enumerated — <c>\\dave-mba</c>,
    /// <c>\\192.168.1.98</c> — and false for everything else, including
    /// <c>\\server\share</c>, device paths (<c>\\?\</c>, <c>\\.\</c>) and the shell
    /// namespace roots above.
    /// </summary>
    internal static bool IsServerRoot(string? path)
    {
        var server = ServerNameOfRoot(path);
        return server is not null && !IsShellNamespaceRoot(path);
    }

    /// <summary>
    /// The server name when <paramref name="path"/> is a bare UNC root, otherwise null.
    /// Device paths are rejected here so neither classifier can capture them.
    /// </summary>
    private static string? ServerNameOfRoot(string? path)
    {
        if (!IsUnc(path)) return null;

        var rest = path!.Substring(2);

        // \\?\... and \\.\... are device paths, not servers.
        if (rest.StartsWith("?", StringComparison.Ordinal) || rest.StartsWith(".", StringComparison.Ordinal))
        {
            // "?" / "." alone is not a meaningful server name either.
            if (rest.Length == 1 || rest[1] == '\\' || rest[1] == '/') return null;
        }

        rest = rest.TrimEnd('\\', '/');
        if (rest.Length == 0) return null;

        // A share is present as soon as another separator appears.
        if (rest.IndexOf('\\') >= 0 || rest.IndexOf('/') >= 0) return null;

        return rest;
    }
}
