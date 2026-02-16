using System;
using System.Runtime.InteropServices;

namespace Span.Services
{
    /// <summary>
    /// Shows native Windows Shell context menus with full shell extension support.
    /// Displays all registered shell extensions (Bandizip, 7-Zip, VS Code, etc.)
    /// identical to Windows Explorer.
    /// </summary>
    public static class ShellContextMenu
    {
        #region COM Interfaces

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig]
            int ParseDisplayName(IntPtr hwnd, IntPtr pbc,
                [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
                out uint pchEaten, out IntPtr ppidl, ref uint pdwAttributes);

            [PreserveSig]
            int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

            [PreserveSig]
            int BindToObject(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int BindToStorage(IntPtr pidl, IntPtr pbc, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

            [PreserveSig]
            int CreateViewObject(IntPtr hwndOwner, ref Guid riid, out IntPtr ppv);

            [PreserveSig]
            int GetAttributesOf(uint cidl,
                [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl, ref uint rgfInOut);

            [PreserveSig]
            int GetUIObjectOf(IntPtr hwndOwner, uint cidl,
                [MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
                ref Guid riid, IntPtr rgfReserved, out IntPtr ppv);

            [PreserveSig]
            int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);

            [PreserveSig]
            int SetNameOf(IntPtr hwnd, IntPtr pidl,
                [MarshalAs(UnmanagedType.LPWStr)] string pszName,
                uint uFlags, out IntPtr ppidlOut);
        }

        [ComImport, Guid("000214e4-0000-0000-c000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hmenu, uint indexMenu,
                uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(IntPtr idCmd, uint uType,
                IntPtr pReserved, IntPtr pszName, uint cchMax);
        }

        [ComImport, Guid("000214f4-0000-0000-c000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu2
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hmenu, uint indexMenu,
                uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(IntPtr idCmd, uint uType,
                IntPtr pReserved, IntPtr pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
        }

        [ComImport, Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IContextMenu3
        {
            [PreserveSig]
            int QueryContextMenu(IntPtr hmenu, uint indexMenu,
                uint idCmdFirst, uint idCmdLast, uint uFlags);

            [PreserveSig]
            int InvokeCommand(ref CMINVOKECOMMANDINFO pici);

            [PreserveSig]
            int GetCommandString(IntPtr idCmd, uint uType,
                IntPtr pReserved, IntPtr pszName, uint cchMax);

            [PreserveSig]
            int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

            [PreserveSig]
            int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam,
                out IntPtr plResult);
        }

        #endregion

        #region P/Invoke

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHParseDisplayName(string pszName, IntPtr pbc,
            out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

        [DllImport("shell32.dll")]
        private static extern int SHBindToParent(IntPtr pidl, ref Guid riid,
            out IntPtr ppv, out IntPtr ppidlLast);

        [DllImport("user32.dll")]
        private static extern IntPtr CreatePopupMenu();

        [DllImport("user32.dll")]
        private static extern bool DestroyMenu(IntPtr hMenu);

        [DllImport("user32.dll")]
        private static extern int TrackPopupMenuEx(IntPtr hmenu, uint fuFlags,
            int x, int y, IntPtr hwnd, IntPtr lptpm);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr pv);

        [DllImport("comctl32.dll")]
        private static extern bool SetWindowSubclass(IntPtr hWnd,
            SubclassProc pfnSubclass, IntPtr uIdSubclass, IntPtr dwRefData);

        [DllImport("comctl32.dll")]
        private static extern bool RemoveWindowSubclass(IntPtr hWnd,
            SubclassProc pfnSubclass, IntPtr uIdSubclass);

        [DllImport("comctl32.dll")]
        private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint uMsg,
            IntPtr wParam, IntPtr lParam);

        private delegate IntPtr SubclassProc(IntPtr hWnd, uint uMsg,
            IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData);

        #endregion

        #region Structs & Constants

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct CMINVOKECOMMANDINFO
        {
            public int cbSize;
            public uint fMask;
            public IntPtr hwnd;
            public IntPtr lpVerb;
            public IntPtr lpParameters;
            public IntPtr lpDirectory;
            public int nShow;
            public uint dwHotKey;
            public IntPtr hIcon;
        }

        private const uint CMF_NORMAL = 0x00000000;
        private const uint CMF_EXPLORE = 0x00000004;
        private const uint CMF_CANRENAME = 0x00000010;
        private const uint TPM_RETURNCMD = 0x0100;
        private const uint TPM_RIGHTBUTTON = 0x0002;
        private const int SW_SHOWNORMAL = 1;
        private const uint FIRST_CMD = 1;
        private const uint LAST_CMD = 0x7FFF;

        private const uint WM_INITMENUPOPUP = 0x0117;
        private const uint WM_DRAWITEM = 0x002B;
        private const uint WM_MEASUREITEM = 0x002C;
        private const uint WM_MENUCHAR = 0x0120;

        private static readonly IntPtr SUBCLASS_ID = (IntPtr)99;

        #endregion

        // Active context menu refs for message forwarding (set during TrackPopupMenu)
        private static IContextMenu2? s_cm2;
        private static IContextMenu3? s_cm3;
        private static SubclassProc? s_subclassDelegate; // prevent GC collection

        /// <summary>
        /// Show native shell context menu for a file or folder at current cursor position.
        /// Returns true if the menu was shown successfully.
        /// </summary>
        public static bool ShowForItem(IntPtr hwnd, string path)
        {
            GetCursorPos(out POINT pt);
            return ShowForItemAt(hwnd, path, pt.X, pt.Y);
        }

        /// <summary>
        /// Show native shell context menu for a file or folder at specified screen coordinates.
        /// </summary>
        public static bool ShowForItemAt(IntPtr hwnd, string path, int screenX, int screenY)
        {
            IntPtr pidl = IntPtr.Zero;
            IntPtr hMenu = IntPtr.Zero;
            IntPtr shellFolderPtr = IntPtr.Zero;
            IntPtr contextMenuPtr = IntPtr.Zero;
            object? shellFolderObj = null;
            object? contextMenuObj = null;

            try
            {
                // 1. Parse path to absolute PIDL
                int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu] SHParseDisplayName failed for '{path}': 0x{hr:X8}");
                    return false;
                }

                // 2. Get parent IShellFolder and relative child PIDL
                var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                hr = SHBindToParent(pidl, ref iidFolder, out shellFolderPtr, out IntPtr childPidl);
                if (hr != 0 || shellFolderPtr == IntPtr.Zero)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu] SHBindToParent failed: 0x{hr:X8}");
                    return false;
                }
                shellFolderObj = Marshal.GetObjectForIUnknown(shellFolderPtr);
                var shellFolder = (IShellFolder)shellFolderObj;

                // 3. Get IContextMenu from shell folder
                var iidCM = new Guid("000214e4-0000-0000-c000-000000000046");
                IntPtr[] childPidls = { childPidl };
                hr = shellFolder.GetUIObjectOf(hwnd, 1, childPidls, ref iidCM, IntPtr.Zero, out contextMenuPtr);
                if (hr != 0 || contextMenuPtr == IntPtr.Zero)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu] GetUIObjectOf failed: 0x{hr:X8}");
                    return false;
                }
                contextMenuObj = Marshal.GetObjectForIUnknown(contextMenuPtr);
                var contextMenu = (IContextMenu)contextMenuObj;

                // 4. Query for IContextMenu2/3 (needed for owner-drawn shell extension items)
                s_cm3 = null;
                s_cm2 = null;
                try { s_cm3 = (IContextMenu3)contextMenuObj; }
                catch { /* interface not supported */ }
                if (s_cm3 == null)
                {
                    try { s_cm2 = (IContextMenu2)contextMenuObj; }
                    catch { /* interface not supported */ }
                }

                // 5. Create native popup menu and populate with shell items
                hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return false;

                hr = contextMenu.QueryContextMenu(hMenu, 0, FIRST_CMD, LAST_CMD,
                    CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME);
                if (hr < 0)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu] QueryContextMenu failed: 0x{hr:X8}");
                    return false;
                }

                // 6. Install temporary window subclass for IContextMenu2/3 message forwarding
                //    (needed for shell extensions with owner-drawn menu items like Bandizip)
                s_subclassDelegate = new SubclassProc(MenuSubclassProc);
                SetWindowSubclass(hwnd, s_subclassDelegate, SUBCLASS_ID, IntPtr.Zero);

                try
                {
                    // 7. Show menu (blocking) and get selected command
                    int cmd = TrackPopupMenuEx(hMenu,
                        TPM_RETURNCMD | TPM_RIGHTBUTTON,
                        screenX, screenY, hwnd, IntPtr.Zero);

                    // 8. Invoke the selected command
                    if (cmd >= (int)FIRST_CMD)
                    {
                        var invokeInfo = new CMINVOKECOMMANDINFO
                        {
                            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                            fMask = 0,
                            hwnd = hwnd,
                            lpVerb = (IntPtr)(cmd - (int)FIRST_CMD),
                            nShow = SW_SHOWNORMAL
                        };
                        contextMenu.InvokeCommand(ref invokeInfo);
                        Helpers.DebugLogger.Log($"[ShellContextMenu] Command invoked: {cmd}");
                    }
                }
                finally
                {
                    // 9. Always remove temporary subclass
                    RemoveWindowSubclass(hwnd, s_subclassDelegate, SUBCLASS_ID);
                }

                return true;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellContextMenu] Error: {ex.Message}");
                return false;
            }
            finally
            {
                // 10. Cleanup COM objects and native resources
                s_cm2 = null;
                s_cm3 = null;
                s_subclassDelegate = null;

                if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
                if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);

                if (contextMenuObj != null)
                    try { Marshal.ReleaseComObject(contextMenuObj); } catch { }
                if (shellFolderObj != null)
                    try { Marshal.ReleaseComObject(shellFolderObj); } catch { }

                if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
                if (shellFolderPtr != IntPtr.Zero) Marshal.Release(shellFolderPtr);
            }
        }

        /// <summary>
        /// Temporary subclass procedure active during TrackPopupMenuEx.
        /// Forwards owner-drawn menu messages to IContextMenu2/3 so shell extensions
        /// (Bandizip, 7-Zip, etc.) can render their custom menu items.
        /// </summary>
        private static IntPtr MenuSubclassProc(IntPtr hWnd, uint uMsg,
            IntPtr wParam, IntPtr lParam, IntPtr uIdSubclass, IntPtr dwRefData)
        {
            switch (uMsg)
            {
                case WM_INITMENUPOPUP:
                case WM_DRAWITEM:
                case WM_MEASUREITEM:
                    if (s_cm3 != null)
                    {
                        if (s_cm3.HandleMenuMsg2(uMsg, wParam, lParam, out _) == 0)
                            return IntPtr.Zero;
                    }
                    else if (s_cm2 != null)
                    {
                        if (s_cm2.HandleMenuMsg(uMsg, wParam, lParam) == 0)
                            return IntPtr.Zero;
                    }
                    break;

                case WM_MENUCHAR:
                    if (s_cm3 != null)
                    {
                        if (s_cm3.HandleMenuMsg2(uMsg, wParam, lParam, out IntPtr result) == 0)
                            return result;
                    }
                    break;
            }

            return DefSubclassProc(hWnd, uMsg, wParam, lParam);
        }
    }
}
