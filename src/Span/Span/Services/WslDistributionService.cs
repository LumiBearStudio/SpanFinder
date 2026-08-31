using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;
using Span.Helpers;

namespace Span.Services;

/// <summary>
/// Lists installed WSL distributions so <c>\\wsl.localhost</c> can be browsed inside SPAN
/// rather than handed to File Explorer (Issue #67).
///
/// <c>\\wsl.localhost</c> is a shell namespace root, not a file server: NetShareEnum fails on
/// it (<c>net view</c> reports error 1707) and <c>Directory.Exists</c> is false. Only the level
/// below is real — <c>\\wsl.localhost\Ubuntu</c> is served by the P9 redirector and does exist.
/// So the distribution names have to come from somewhere else.
///
/// They come from the registry rather than from <c>wsl.exe --list</c>: no process to launch, no
/// UTF-16 output to decode, and nothing that can hang if the WSL service is unhealthy — which
/// matters on a path the file manager walks.
/// </summary>
internal static class WslDistributionService
{
    private const string LxssKey = @"Software\Microsoft\Windows\CurrentVersion\Lxss";

    /// <summary>A distribution and the UNC path that opens it.</summary>
    internal readonly record struct WslDistribution(string Name, string Path);

    /// <summary>
    /// Installed distributions whose share is actually reachable, in name order.
    ///
    /// Each candidate is probed with <c>Directory.Exists</c> because the registry also lists
    /// distributions whose P9 share is not mounted — a WSL1 distribution, or one that has never
    /// been started. Listing a folder that cannot be opened would be worse than not listing it.
    /// </summary>
    internal static List<WslDistribution> GetDistributions()
    {
        var results = new List<WslDistribution>();

        try
        {
            using var lxss = Registry.CurrentUser.OpenSubKey(LxssKey);
            if (lxss is null) return results;

            foreach (var id in lxss.GetSubKeyNames())
            {
                try
                {
                    using var distro = lxss.OpenSubKey(id);
                    if (distro?.GetValue("DistributionName") is not string name || name.Length == 0)
                        continue;

                    var path = $@"\\wsl.localhost\{name}";
                    if (!Directory.Exists(path))
                    {
                        DebugLogger.Log($"[WSL] '{name}' listed in registry but its share is not reachable — skipped");
                        continue;
                    }

                    results.Add(new WslDistribution(name, path));
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[WSL] distribution '{id}' unreadable: {ex.GetType().Name}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLogger.Log($"[WSL] registry unavailable: {ex.GetType().Name}: {ex.Message}");
            return results;
        }

        results.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        DebugLogger.Log($"[WSL] {results.Count} distribution(s) available");
        return results;
    }
}
