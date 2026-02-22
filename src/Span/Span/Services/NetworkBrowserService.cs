using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
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
                                    IconGlyph = "\uEDD4", // ri-global-line
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
                            IconGlyph = "\uEEA7", // ri-folder-line
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
