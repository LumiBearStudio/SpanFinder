using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// LAN 네트워크 브라우저 서비스. WNetEnumResource로 네트워크 컴퓨터를 열거하고,
    /// NetShareEnum으로 서버의 공유 폴더를 열거한다. 타임아웃 보호(5초)를 적용.
    /// </summary>
    public class NetworkBrowserService
    {
        private const int TimeoutMs = 5000;

        /// <summary>
        /// 로컬 네트워크의 컴퓨터 목록을 열거합니다.
        /// WNetEnumResource(RESOURCE_GLOBALNET)를 사용합니다.
        /// </summary>
        public async Task<List<NetworkItem>> GetNetworkComputersAsync()
        {
            try
            {
                var task = Task.Run(() => EnumNetworkComputers());
                var completed = await Task.WhenAny(task, Task.Delay(TimeoutMs));
                if (completed == task)
                    return await task;

                DebugLogger.Log("[NetworkBrowserService] GetNetworkComputersAsync timed out");
                return new List<NetworkItem>();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NetworkBrowserService] GetNetworkComputersAsync error: {ex.Message}");
                return new List<NetworkItem>();
            }
        }

        /// <summary>
        /// 특정 서버의 공유 폴더 목록을 열거합니다.
        /// NetShareEnum level 1을 사용하여 STYPE_DISKTREE만 반환합니다.
        /// </summary>
        public async Task<List<NetworkItem>> GetServerSharesAsync(string serverName)
        {
            try
            {
                var task = Task.Run(() => EnumServerShares(serverName));
                var completed = await Task.WhenAny(task, Task.Delay(TimeoutMs));
                if (completed == task)
                    return await task;

                DebugLogger.Log($"[NetworkBrowserService] GetServerSharesAsync timed out for {serverName}");
                return new List<NetworkItem>();
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[NetworkBrowserService] GetServerSharesAsync error: {ex.Message}");
                return new List<NetworkItem>();
            }
        }

        private List<NetworkItem> EnumNetworkComputers()
        {
            var results = new List<NetworkItem>();

            // Recursively enumerate the network to find servers
            EnumNetworkResourcesRecursive(IntPtr.Zero, results);

            return results;
        }

        private void EnumNetworkResourcesRecursive(IntPtr lpNetResource, List<NetworkItem> results)
        {
            int ret = NativeMethods.WNetOpenEnumW(
                NativeMethods.RESOURCE_GLOBALNET,
                NativeMethods.RESOURCETYPE_ANY,
                0,
                lpNetResource,
                out IntPtr hEnum);

            if (ret != NativeMethods.NO_ERROR)
                return;

            try
            {
                int bufferSize = 16384;
                IntPtr buffer = Marshal.AllocHGlobal(bufferSize);
                try
                {
                    while (true)
                    {
                        int count = -1; // enumerate all entries
                        int size = bufferSize;
                        ret = NativeMethods.WNetEnumResourceW(hEnum, ref count, buffer, ref size);

                        if (ret == NativeMethods.ERROR_NO_MORE_ITEMS)
                            break;
                        if (ret != NativeMethods.NO_ERROR)
                            break;

                        int structSize = Marshal.SizeOf<NativeMethods.NETRESOURCE>();
                        for (int i = 0; i < count; i++)
                        {
                            IntPtr ptr = IntPtr.Add(buffer, i * structSize);
                            var nr = Marshal.PtrToStructure<NativeMethods.NETRESOURCE>(ptr);

                            if (nr.dwDisplayType == NativeMethods.RESOURCEDISPLAYTYPE_SERVER)
                            {
                                var name = nr.lpRemoteName?.TrimStart('\\') ?? string.Empty;
                                results.Add(new NetworkItem
                                {
                                    Name = name,
                                    Path = nr.lpRemoteName ?? $@"\\{name}",
                                    Type = NetworkItemType.Server,
                                    IconGlyph = IconService.Current?.NetworkGlyph ?? "\uEDD4",
                                    Comment = nr.lpComment ?? string.Empty
                                });
                            }
                            else if ((nr.dwUsage & NativeMethods.RESOURCEUSAGE_CONTAINER) != 0)
                            {
                                // Container (e.g., domain/workgroup) — recurse into it
                                int nrSize = Marshal.SizeOf<NativeMethods.NETRESOURCE>();
                                IntPtr nrPtr = Marshal.AllocHGlobal(nrSize);
                                try
                                {
                                    Marshal.StructureToPtr(nr, nrPtr, false);
                                    EnumNetworkResourcesRecursive(nrPtr, results);
                                }
                                finally
                                {
                                    Marshal.FreeHGlobal(nrPtr);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                NativeMethods.WNetCloseEnum(hEnum);
            }
        }

        // --- browsing a server root as a folder (Issue #67) ----------------------

        /// <summary>Outcome of a share enumeration, distinguishing failure from an empty server.</summary>
        public enum ShareListStatus
        {
            /// <summary>Shares listed (possibly zero — the server answered).</summary>
            Ok,
            /// <summary>The server did not answer within the timeout.</summary>
            TimedOut,
            /// <summary>The server answered with an error (unreachable, denied, not a file server).</summary>
            Failed,
        }

        /// <summary>Per-server gate: one in-flight enumeration, plus a short memory of failures.</summary>
        private sealed class ServerGate
        {
            internal Task<(ShareListStatus, List<NetworkItem>)>? InFlight;
            internal DateTime FailedUntilUtc;
        }

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, ServerGate> _serverGates =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// How long a failed server is remembered. NetShareEnum is a blocking P/Invoke that
        /// cannot be cancelled, so the 5s timeout only stops us waiting — the thread stays
        /// parked until Windows gives up. Without this, moving back and forth across an
        /// unreachable server's column parks a new thread every time.
        /// </summary>
        private static readonly TimeSpan FailureMemory = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Lists a server's shares for navigation, reporting whether the server actually
        /// answered. <see cref="GetServerSharesAsync"/> keeps its original shape for the
        /// network-browse dialog, which only needs the list.
        /// </summary>
        public Task<(ShareListStatus Status, List<NetworkItem> Shares)> ListSharesForNavigationAsync(string serverName)
        {
            var key = serverName.TrimStart('\\').TrimEnd('\\', '/');
            var gate = _serverGates.GetOrAdd(key, _ => new ServerGate());

            lock (gate)
            {
                if (DateTime.UtcNow < gate.FailedUntilUtc)
                {
                    DebugLogger.Log($"[NetworkBrowserService] '{key}' recently failed — skipping enumeration");
                    return Task.FromResult((ShareListStatus.Failed, new List<NetworkItem>()));
                }

                // Several columns can ask for the same server at once (navigate, refresh,
                // breadcrumb). Share one enumeration rather than parking a thread each.
                if (gate.InFlight is { IsCompleted: false } running)
                    return running;

                gate.InFlight = RunAsync(key, gate);
                return gate.InFlight;
            }

            async Task<(ShareListStatus, List<NetworkItem>)> RunAsync(string server, ServerGate g)
            {
                try
                {
                    var work = Task.Run(() => EnumServerSharesWithStatus(server));
                    var completed = await Task.WhenAny(work, Task.Delay(TimeoutMs)).ConfigureAwait(false);

                    if (completed != work)
                    {
                        DebugLogger.Log($"[NetworkBrowserService] '{server}' timed out after {TimeoutMs}ms");
                        lock (g) { g.FailedUntilUtc = DateTime.UtcNow + FailureMemory; }
                        return (ShareListStatus.TimedOut, new List<NetworkItem>());
                    }

                    var (ret, shares) = await work.ConfigureAwait(false);
                    if (ret != NativeMethods.NERR_Success)
                    {
                        DebugLogger.Log($"[NetworkBrowserService] '{server}' NetShareEnum failed (ret={ret})");
                        lock (g) { g.FailedUntilUtc = DateTime.UtcNow + FailureMemory; }
                        return (ShareListStatus.Failed, new List<NetworkItem>());
                    }

                    DebugLogger.Log($"[NetworkBrowserService] '{server}' returned {shares.Count} share(s)");
                    return (ShareListStatus.Ok, shares);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[NetworkBrowserService] '{server}' error: {ex.GetType().Name}: {ex.Message}");
                    lock (g) { g.FailedUntilUtc = DateTime.UtcNow + FailureMemory; }
                    return (ShareListStatus.Failed, new List<NetworkItem>());
                }
            }
        }

        /// <summary>Clears the failure memory for a server so an explicit refresh retries immediately.</summary>
        public static void ForgetServerFailure(string serverName)
        {
            var key = serverName.TrimStart('\\').TrimEnd('\\', '/');
            if (_serverGates.TryGetValue(key, out var gate))
                lock (gate) { gate.FailedUntilUtc = DateTime.MinValue; }
        }

        private (int Ret, List<NetworkItem> Shares) EnumServerSharesWithStatus(string serverName)
        {
            var results = new List<NetworkItem>();

            if (!serverName.StartsWith(@"\\"))
                serverName = @"\\" + serverName;

            int resumeHandle = 0;
            int ret = NativeMethods.NetShareEnum(
                serverName, 1,
                out IntPtr bufPtr,
                NativeMethods.MAX_PREFERRED_LENGTH,
                out int entriesRead, out int _,
                ref resumeHandle);

            if (ret != NativeMethods.NERR_Success || bufPtr == IntPtr.Zero)
                return (ret, results);

            try
            {
                CollectDiskShares(bufPtr, entriesRead, serverName, results);
            }
            finally
            {
                NativeMethods.NetApiBufferFree(bufPtr);
            }

            return (ret, results);
        }

        private static void CollectDiskShares(IntPtr bufPtr, int entriesRead, string serverName, List<NetworkItem> results)
        {
            int structSize = Marshal.SizeOf<NativeMethods.SHARE_INFO_1>();
            for (int i = 0; i < entriesRead; i++)
            {
                IntPtr ptr = IntPtr.Add(bufPtr, i * structSize);
                var share = Marshal.PtrToStructure<NativeMethods.SHARE_INFO_1>(ptr);

                // Only include disk shares, exclude admin shares ($ suffix)
                bool isDisk = (share.shi1_type & ~NativeMethods.STYPE_SPECIAL) == NativeMethods.STYPE_DISKTREE;
                bool isSpecial = (share.shi1_type & NativeMethods.STYPE_SPECIAL) != 0;

                if (isDisk && !isSpecial && !string.IsNullOrEmpty(share.shi1_netname))
                {
                    results.Add(new NetworkItem
                    {
                        Name = share.shi1_netname,
                        Path = $@"{serverName}\{share.shi1_netname}",
                        Type = NetworkItemType.Share,
                        IconGlyph = "", // ri-folder-shared-fill
                        Comment = share.shi1_remark ?? string.Empty
                    });
                }
            }
        }

        private List<NetworkItem> EnumServerShares(string serverName)
        {
            var results = new List<NetworkItem>();

            // Normalize server name: ensure it starts with \\
            if (!serverName.StartsWith(@"\\"))
                serverName = @"\\" + serverName;

            int resumeHandle = 0;
            int ret = NativeMethods.NetShareEnum(
                serverName, 1,
                out IntPtr bufPtr,
                NativeMethods.MAX_PREFERRED_LENGTH,
                out int entriesRead, out int _,
                ref resumeHandle);

            if (ret != NativeMethods.NERR_Success || bufPtr == IntPtr.Zero)
                return results;

            try
            {
                int structSize = Marshal.SizeOf<NativeMethods.SHARE_INFO_1>();
                for (int i = 0; i < entriesRead; i++)
                {
                    IntPtr ptr = IntPtr.Add(bufPtr, i * structSize);
                    var share = Marshal.PtrToStructure<NativeMethods.SHARE_INFO_1>(ptr);

                    // Only include disk shares, exclude admin shares ($ suffix)
                    bool isDisk = (share.shi1_type & ~NativeMethods.STYPE_SPECIAL) == NativeMethods.STYPE_DISKTREE;
                    bool isSpecial = (share.shi1_type & NativeMethods.STYPE_SPECIAL) != 0;

                    if (isDisk && !isSpecial && !string.IsNullOrEmpty(share.shi1_netname))
                    {
                        results.Add(new NetworkItem
                        {
                            Name = share.shi1_netname,
                            Path = $@"{serverName}\{share.shi1_netname}",
                            Type = NetworkItemType.Share,
                            IconGlyph = "\uED77", // ri-folder-shared-fill
                            Comment = share.shi1_remark ?? string.Empty
                        });
                    }
                }
            }
            finally
            {
                NativeMethods.NetApiBufferFree(bufPtr);
            }

            return results;
        }
    }
}
