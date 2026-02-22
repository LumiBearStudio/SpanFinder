using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// Shows native Windows Shell context menus with full shell extension support.
    /// Also provides session-based enumeration for rendering shell extension items
    /// inside custom WinUI MenuFlyout controls.
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

        // HMENU enumeration
        [DllImport("user32.dll")]
        private static extern int GetMenuItemCount(IntPtr hMenu);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetMenuItemInfoW(IntPtr hmenu, uint uItem,
            bool fByPosition, ref MENUITEMINFOW lpmii);

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

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MENUITEMINFOW
        {
            public uint cbSize;
            public uint fMask;
            public uint fType;
            public uint fState;
            public uint wID;
            public IntPtr hSubMenu;
            public IntPtr hbmpChecked;
            public IntPtr hbmpUnchecked;
            public UIntPtr dwItemData;
            public IntPtr dwTypeData;
            public uint cch;
            public IntPtr hbmpItem;
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

        // MENUITEMINFO masks
        private const uint MIIM_FTYPE = 0x00000100;
        private const uint MIIM_ID = 0x00000002;
        private const uint MIIM_STATE = 0x00000001;
        private const uint MIIM_STRING = 0x00000040;
        private const uint MIIM_SUBMENU = 0x00000004;

        // MENUITEMINFO types
        private const uint MFT_SEPARATOR = 0x00000800;
        private const uint MFT_OWNERDRAW = 0x00000100;

        // MENUITEMINFO states
        private const uint MFS_DISABLED = 0x00000003;
        private const uint MFS_GRAYED = 0x00000001;

        // GetCommandString flags
        private const uint GCS_VERBW = 0x00000004;

        private static readonly IntPtr SUBCLASS_ID = (IntPtr)99;

        /// <summary>Standard shell verbs that are handled by our custom menu items.</summary>
        private static readonly HashSet<string> StandardVerbs = new(StringComparer.OrdinalIgnoreCase)
        {
            "open", "openas", "opencommand", "runas",
            "cut", "copy", "paste", "link",
            "delete", "rename", "properties",
            "explore", "find",
            "copyaspath"  // Span has its own "Copy path"
        };

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
                int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero) return false;

                var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                hr = SHBindToParent(pidl, ref iidFolder, out shellFolderPtr, out IntPtr childPidl);
                if (hr != 0 || shellFolderPtr == IntPtr.Zero) return false;

                shellFolderObj = Marshal.GetObjectForIUnknown(shellFolderPtr);
                var shellFolder = (IShellFolder)shellFolderObj;

                var iidCM = new Guid("000214e4-0000-0000-c000-000000000046");
                IntPtr[] childPidls = { childPidl };
                hr = shellFolder.GetUIObjectOf(hwnd, 1, childPidls, ref iidCM, IntPtr.Zero, out contextMenuPtr);
                if (hr != 0 || contextMenuPtr == IntPtr.Zero) return false;

                contextMenuObj = Marshal.GetObjectForIUnknown(contextMenuPtr);
                var contextMenu = (IContextMenu)contextMenuObj;

                s_cm3 = null;
                s_cm2 = null;
                try { s_cm3 = (IContextMenu3)contextMenuObj; } catch { }
                if (s_cm3 == null) { try { s_cm2 = (IContextMenu2)contextMenuObj; } catch { } }

                hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return false;

                hr = contextMenu.QueryContextMenu(hMenu, 0, FIRST_CMD, LAST_CMD,
                    CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME);
                if (hr < 0) return false;

                s_subclassDelegate = new SubclassProc(MenuSubclassProc);
                SetWindowSubclass(hwnd, s_subclassDelegate, SUBCLASS_ID, IntPtr.Zero);

                try
                {
                    int cmd = TrackPopupMenuEx(hMenu,
                        TPM_RETURNCMD | TPM_RIGHTBUTTON,
                        screenX, screenY, hwnd, IntPtr.Zero);

                    if (cmd >= (int)FIRST_CMD)
                    {
                        var invokeInfo = new CMINVOKECOMMANDINFO
                        {
                            cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                            fMask = 0, hwnd = hwnd,
                            lpVerb = (IntPtr)(cmd - (int)FIRST_CMD),
                            nShow = SW_SHOWNORMAL
                        };
                        contextMenu.InvokeCommand(ref invokeInfo);
                    }
                }
                finally
                {
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
                s_cm2 = null; s_cm3 = null; s_subclassDelegate = null;
                if (hMenu != IntPtr.Zero) DestroyMenu(hMenu);
                if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
                if (contextMenuObj != null) try { Marshal.ReleaseComObject(contextMenuObj); } catch { }
                if (shellFolderObj != null) try { Marshal.ReleaseComObject(shellFolderObj); } catch { }
                if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
                if (shellFolderPtr != IntPtr.Zero) Marshal.Release(shellFolderPtr);
            }
        }

        /// <summary>
        /// Create a session that enumerates shell extension items for a given path.
        /// The session must be disposed after the menu is closed.
        /// Non-standard shell extension items (Bandizip, 7-Zip, etc.) are extracted
        /// while standard items (open, copy, delete, etc.) are filtered out.
        /// </summary>
        public static Session? CreateSession(IntPtr hwnd, string path)
        {
            IntPtr pidl = IntPtr.Zero;
            IntPtr shellFolderPtr = IntPtr.Zero;
            IntPtr contextMenuPtr = IntPtr.Zero;
            object? shellFolderObj = null;
            object? contextMenuObj = null;

            try
            {
                int hr = SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _);
                if (hr != 0 || pidl == IntPtr.Zero) return null;

                var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
                hr = SHBindToParent(pidl, ref iidFolder, out shellFolderPtr, out IntPtr childPidl);
                if (hr != 0 || shellFolderPtr == IntPtr.Zero) return null;

                shellFolderObj = Marshal.GetObjectForIUnknown(shellFolderPtr);
                var shellFolder = (IShellFolder)shellFolderObj;

                var iidCM = new Guid("000214e4-0000-0000-c000-000000000046");
                IntPtr[] childPidls = { childPidl };
                hr = shellFolder.GetUIObjectOf(hwnd, 1, childPidls, ref iidCM, IntPtr.Zero, out contextMenuPtr);
                if (hr != 0 || contextMenuPtr == IntPtr.Zero) return null;

                contextMenuObj = Marshal.GetObjectForIUnknown(contextMenuPtr);
                var contextMenu = (IContextMenu)contextMenuObj;

                IContextMenu2? cm2 = null;
                IContextMenu3? cm3 = null;
                try { cm3 = (IContextMenu3)contextMenuObj; } catch { }
                if (cm3 == null) { try { cm2 = (IContextMenu2)contextMenuObj; } catch { } }

                IntPtr hMenu = CreatePopupMenu();
                if (hMenu == IntPtr.Zero) return null;

                hr = contextMenu.QueryContextMenu(hMenu, 0, FIRST_CMD, LAST_CMD,
                    CMF_NORMAL | CMF_EXPLORE | CMF_CANRENAME);
                if (hr < 0)
                {
                    DestroyMenu(hMenu);
                    return null;
                }

                // Enumerate and filter items
                var items = EnumerateMenuItems(hMenu, contextMenu, cm2, cm3, 0);

                var session = new Session(
                    contextMenu, contextMenuObj, shellFolderObj,
                    contextMenuPtr, shellFolderPtr, pidl,
                    hMenu, hwnd, items);

                // Ownership transferred to session — don't clean up here
                pidl = IntPtr.Zero;
                shellFolderPtr = IntPtr.Zero;
                contextMenuPtr = IntPtr.Zero;
                shellFolderObj = null;
                contextMenuObj = null;

                return session;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[ShellContextMenu] CreateSession error: {ex.Message}");
                return null;
            }
            finally
            {
                // Only clean up if ownership was NOT transferred
                if (pidl != IntPtr.Zero) CoTaskMemFree(pidl);
                if (contextMenuObj != null) try { Marshal.ReleaseComObject(contextMenuObj); } catch { }
                if (shellFolderObj != null) try { Marshal.ReleaseComObject(shellFolderObj); } catch { }
                if (contextMenuPtr != IntPtr.Zero) Marshal.Release(contextMenuPtr);
                if (shellFolderPtr != IntPtr.Zero) Marshal.Release(shellFolderPtr);
            }
        }

        /// <summary>
        /// Enumerate items from an HMENU, filtering out standard shell verbs.
        /// </summary>
        private static List<ShellMenuItem> EnumerateMenuItems(
            IntPtr hMenu, IContextMenu contextMenu,
            IContextMenu2? cm2, IContextMenu3? cm3,
            int depth)
        {
            var result = new List<ShellMenuItem>();
            int count = GetMenuItemCount(hMenu);
            if (count <= 0) return result;

            // Max recursion depth to prevent infinite loops
            if (depth > 5) return result;

            for (uint i = 0; i < (uint)count; i++)
            {
                // First pass: get type and ID
                var mii = new MENUITEMINFOW
                {
                    cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                    fMask = MIIM_FTYPE | MIIM_ID | MIIM_STATE | MIIM_SUBMENU
                };

                if (!GetMenuItemInfoW(hMenu, i, true, ref mii))
                    continue;

                // Separator
                if ((mii.fType & MFT_SEPARATOR) != 0)
                {
                    result.Add(new ShellMenuItem { IsSeparator = true });
                    continue;
                }

                // Get text (second pass)
                string text = string.Empty;
                bool isOwnerDrawn = (mii.fType & MFT_OWNERDRAW) != 0;

                if (!isOwnerDrawn)
                {
                    // Get text length first
                    var miiText = new MENUITEMINFOW
                    {
                        cbSize = (uint)Marshal.SizeOf<MENUITEMINFOW>(),
                        fMask = MIIM_STRING,
                        dwTypeData = IntPtr.Zero,
                        cch = 0
                    };
                    GetMenuItemInfoW(hMenu, i, true, ref miiText);

                    if (miiText.cch > 0)
                    {
                        miiText.cch++; // include null terminator
                        miiText.dwTypeData = Marshal.AllocCoTaskMem((int)miiText.cch * 2);
                        try
                        {
                            if (GetMenuItemInfoW(hMenu, i, true, ref miiText))
                            {
                                text = Marshal.PtrToStringUni(miiText.dwTypeData) ?? string.Empty;
                                // Strip accelerator markers (&)
                                text = text.Replace("&", "");
                            }
                        }
                        finally
                        {
                            Marshal.FreeCoTaskMem(miiText.dwTypeData);
                        }
                    }
                }

                // Try to get canonical verb
                string verb = string.Empty;
                if (mii.wID >= FIRST_CMD)
                {
                    IntPtr verbBuf = Marshal.AllocCoTaskMem(512);
                    try
                    {
                        int hr = contextMenu.GetCommandString(
                            (IntPtr)(mii.wID - FIRST_CMD),
                            GCS_VERBW, IntPtr.Zero, verbBuf, 256);
                        if (hr == 0)
                        {
                            verb = Marshal.PtrToStringUni(verbBuf) ?? string.Empty;
                        }
                    }
                    catch { /* GetCommandString not implemented by this extension */ }
                    finally
                    {
                        Marshal.FreeCoTaskMem(verbBuf);
                    }
                }

                // Filter out standard verbs (handled by our custom menu)
                if (!string.IsNullOrEmpty(verb) && StandardVerbs.Contains(verb))
                    continue;

                // Skip items with no text and no verb (usually internal shell items)
                if (string.IsNullOrWhiteSpace(text) && string.IsNullOrEmpty(verb) && !isOwnerDrawn)
                    continue;

                var item = new ShellMenuItem
                {
                    Text = text,
                    CommandId = (int)mii.wID,
                    Verb = verb,
                    IsDisabled = (mii.fState & MFS_DISABLED) != 0 || (mii.fState & MFS_GRAYED) != 0,
                    IsOwnerDrawn = isOwnerDrawn
                };

                // Handle submenus recursively
                if (mii.hSubMenu != IntPtr.Zero)
                {
                    // Send WM_INITMENUPOPUP to populate the submenu
                    if (cm2 != null)
                    {
                        try { cm2.HandleMenuMsg(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i); }
                        catch { /* not all extensions support this */ }
                    }
                    else if (cm3 != null)
                    {
                        try { cm3.HandleMenuMsg(WM_INITMENUPOPUP, mii.hSubMenu, (IntPtr)i); }
                        catch { }
                    }

                    item.Children = EnumerateMenuItems(mii.hSubMenu, contextMenu, cm2, cm3, depth + 1);
                }

                // Only add if we have text or it's an owner-drawn item with children
                if (!string.IsNullOrWhiteSpace(item.Text) || item.HasSubmenu)
                {
                    result.Add(item);
                }
            }

            // Trim leading/trailing separators
            while (result.Count > 0 && result[0].IsSeparator)
                result.RemoveAt(0);
            while (result.Count > 0 && result[^1].IsSeparator)
                result.RemoveAt(result.Count - 1);

            // Remove consecutive separators
            for (int i = result.Count - 1; i > 0; i--)
            {
                if (result[i].IsSeparator && result[i - 1].IsSeparator)
                    result.RemoveAt(i);
            }

            return result;
        }

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

        /// <summary>
        /// Holds references to shell COM objects for the duration of a menu interaction.
        /// Provides InvokeCommand for shell extension items and automatic cleanup on dispose.
        /// </summary>
        public sealed class Session : IDisposable
        {
            private readonly object _contextMenuImpl; // IContextMenu, stored as object to avoid exposing private type
            private readonly object _contextMenuObj;
            private readonly object _shellFolderObj;
            private readonly IntPtr _contextMenuPtr;
            private readonly IntPtr _shellFolderPtr;
            private readonly IntPtr _pidl;
            private readonly IntPtr _hMenu;
            private readonly IntPtr _hwnd;
            private bool _disposed;

            /// <summary>Shell extension menu items (standard verbs already filtered out)</summary>
            public List<ShellMenuItem> Items { get; }

            internal Session(
                object contextMenuImpl, object contextMenuObj, object shellFolderObj,
                IntPtr contextMenuPtr, IntPtr shellFolderPtr, IntPtr pidl,
                IntPtr hMenu, IntPtr hwnd, List<ShellMenuItem> items)
            {
                _contextMenuImpl = contextMenuImpl;
                _contextMenuObj = contextMenuObj;
                _shellFolderObj = shellFolderObj;
                _contextMenuPtr = contextMenuPtr;
                _shellFolderPtr = shellFolderPtr;
                _pidl = pidl;
                _hMenu = hMenu;
                _hwnd = hwnd;
                Items = items;
            }

            /// <summary>
            /// Invoke a shell extension command by its command ID.
            /// Call this when the user clicks a shell extension menu item.
            /// </summary>
            public void InvokeCommand(int commandId)
            {
                if (_disposed) return;
                try
                {
                    var invokeInfo = new CMINVOKECOMMANDINFO
                    {
                        cbSize = Marshal.SizeOf<CMINVOKECOMMANDINFO>(),
                        fMask = 0,
                        hwnd = _hwnd,
                        lpVerb = (IntPtr)(commandId - (int)FIRST_CMD),
                        nShow = SW_SHOWNORMAL
                    };
                    ((IContextMenu)_contextMenuImpl).InvokeCommand(ref invokeInfo);
                    Helpers.DebugLogger.Log($"[ShellContextMenu.Session] Command invoked: {commandId}");
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu.Session] InvokeCommand error: {ex.Message}");
                }
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    if (_hMenu != IntPtr.Zero) DestroyMenu(_hMenu);
                    if (_pidl != IntPtr.Zero) CoTaskMemFree(_pidl);

                    try { Marshal.ReleaseComObject(_contextMenuObj); } catch { }
                    try { Marshal.ReleaseComObject(_shellFolderObj); } catch { }

                    if (_contextMenuPtr != IntPtr.Zero) Marshal.Release(_contextMenuPtr);
                    if (_shellFolderPtr != IntPtr.Zero) Marshal.Release(_shellFolderPtr);
                }
                catch (Exception ex)
                {
                    Helpers.DebugLogger.Log($"[ShellContextMenu.Session] Dispose error: {ex.Message}");
                }
            }
        }
    }
}
