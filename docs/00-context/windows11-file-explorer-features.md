# Windows 11 File Explorer - Comprehensive Feature List

> **Purpose**: Complete feature inventory of Windows 11 File Explorer (as of February 2026) for comparison against the Span file explorer to identify missing features and guide the development roadmap.
>
> **Last Updated**: 2026-02-20

---

## 1. Navigation

### 1.1 Address Bar
- **Breadcrumb navigation**: Clickable path segments showing folder hierarchy; each segment has a dropdown arrow to access sibling folders
- **Edit mode**: Click the empty space in the address bar (or press `Ctrl+L` / `Alt+D` / `F4`) to switch to full-path text editing mode
- **Autocomplete / AutoSuggest**: Dropdown of suggestions based on address bar history as you type a path (AutoSuggest); inline autocomplete available via registry/Internet Options (disabled by default)
- **Drag-to-address-bar**: Drag files/folders onto breadcrumb segments to move/copy them to that location (removed in 23H2, restored in 2025 updates)
- **Dropdown arrow per breadcrumb segment**: Each breadcrumb segment has a small arrow; clicking it reveals a dropdown listing sibling folders at that level, enabling lateral navigation
- **Path copying**: The full path can be selected and copied from the address bar in edit mode
- **Run commands**: The address bar accepts shell commands, URIs, and environment variables (e.g., `%APPDATA%`, `cmd`, `powershell`)
- **Recent locations dropdown**: The address bar has a dropdown button on the right showing recently visited locations

### 1.2 Back / Forward / Up Navigation
- **Back button**: Navigate to previously visited folder (`Alt+Left Arrow` or `Backspace`)
- **Forward button**: Navigate forward after going back (`Alt+Right Arrow`)
- **Up button**: Navigate to parent folder (`Alt+Up Arrow`)
- **Back/Forward history dropdown**: Right-click or long-press the Back/Forward buttons to see a history list of recent locations
- **Mouse button support**: Mouse Button 4 (back) and Button 5 (forward) for navigation

### 1.3 Tab Support (introduced in Windows 11 22H2)
- **New tab**: `Ctrl+T` to open a new tab
- **Close tab**: `Ctrl+W` or middle-click to close current tab
- **Switch tabs**: `Ctrl+Tab` / `Ctrl+Shift+Tab` to cycle through tabs; `Ctrl+1-9` to jump to specific tab
- **Duplicate tab**: Right-click a tab to duplicate it (added in 24H2)
- **Drag tabs**: Drag tabs to reorder them within the window
- **Drag tab out**: Drag a tab out of the window to create a new File Explorer window
- **Merge tabs**: Drag a tab from one File Explorer window into another to merge
- **Tab context menu**: Right-click a tab for options (Close tab, Close other tabs, Close tabs to the right, Duplicate tab, Move to new window)
- **Session restore**: Previously open tabs are restored when File Explorer is reopened after restart (24H2 option: "Restore previous folder windows at logon")
- **Middle-click to open in new tab**: Middle-click a folder to open it in a new tab
- **New tab default location**: Configurable (Home, This PC, or a specific folder)

### 1.4 Quick Access / Home Page
- **Home page**: Default landing page with Quick Access, Favorites, Recent files, and Recommended sections
- **Quick Access**: Pinned folders and frequently accessed folders shown at the top
- **Favorites**: Pinned individual files for quick access (aligned with Office/OneDrive terminology)
- **Recent files**: Up to 25 most recently opened files displayed
- **Frequent folders**: Automatically tracked frequently visited folders
- **Recommended section**: AI-powered recommendations surfacing downloads, recent Office.com files, and Gallery content (25H2)
- **Shared files section**: Files shared with you from OneDrive/SharePoint displayed in Home
- **Customizable**: Can pin/unpin folders, toggle recent files and frequent folders visibility in Options

### 1.5 Navigation Pane (Sidebar)
- **Home**: Quick access to the Home page
- **Gallery**: Photo collection view (see View Modes section)
- **OneDrive**: Cloud storage folders (Personal and Business)
- **This PC**: Local drives, default user folders (Desktop, Documents, Downloads, Music, Pictures, Videos)
- **Network**: Network locations and shared resources
- **Linux**: WSL file system access (when WSL is installed)
- **Quick Access folders**: Pinned folders appear in the sidebar
- **Favorites**: Pinned files section
- **Recycle Bin**: Access to deleted items
- **Libraries**: Optional; can be enabled via View > Navigation pane > Show libraries (Documents, Music, Pictures, Videos)
- **Expandable tree**: Folders can be expanded/collapsed in the navigation pane to show hierarchy
- **Expand to open folder**: Option to auto-expand navigation pane tree to match the current folder (`Ctrl+Shift+E`)
- **Drag-to-sidebar**: Drag items to sidebar folders for quick move/copy operations
- **Reorder Quick Access pins**: Drag and drop to reorder pinned folders in Quick Access
- **Show/Hide navigation pane**: Toggle via View > Show > Navigation pane
- **Show all folders**: Option to show all top-level folders in the navigation pane
- **Compact mode for navigation pane**: Reduced spacing between items

### 1.6 Recent Files / Frequent Folders
- **Recent files tracking**: System-wide tracking of recently accessed files
- **Frequent folders tracking**: Automatic tracking of folder access frequency
- **Privacy controls**: Can clear recent file and frequent folder history
- **Toggle visibility**: Options to show/hide recent files and frequent folders independently

---

## 2. File Operations

### 2.1 Copy, Cut, Paste, Delete
- **Copy** (`Ctrl+C`): Copy selected items to clipboard
- **Cut** (`Ctrl+X`): Cut selected items (marks for move)
- **Paste** (`Ctrl+V`): Paste from clipboard to current location
- **Delete** (`Delete` / `Ctrl+D`): Move selected items to Recycle Bin
- **Permanent delete** (`Shift+Delete`): Delete without sending to Recycle Bin; confirmation dialog
- **Undo** (`Ctrl+Z`): Undo the last file operation (copy, move, rename, delete)
- **Redo** (`Ctrl+Y`): Redo the last undone operation
- **Multi-level undo/redo**: Supports multiple levels of undo/redo for file operations
- **Paste shortcut** (`Ctrl+Shift+V` in some contexts): Paste as shortcut

### 2.2 Rename
- **Inline rename** (`F2`): Rename selected item directly in the file list
- **Batch rename**: Select multiple files, press F2, type base name -- Windows appends sequential numbers (e.g., `Photo (1).jpg`, `Photo (2).jpg`)
- **Extension preservation**: When renaming, only the filename (not extension) is selected by default
- **Tab to next**: After renaming, pressing Tab moves to rename the next item

### 2.3 New File/Folder Creation
- **New folder** (`Ctrl+Shift+N`): Create a new folder in the current location
- **New items from context menu**: Right-click > New > choose type (Folder, Shortcut, Text Document, Bitmap Image, Compressed Folder, Rich Text Document, and registered file types)
- **Registered new file types**: Third-party applications can register custom "New" entries

### 2.4 Move Operations
- **Cut and paste**: Traditional cut/paste workflow
- **Drag and drop**: Move by dragging within same drive; copy by dragging between drives
- **Shift+drag**: Force move operation regardless of source/destination drives
- **Ctrl+drag**: Force copy operation regardless of source/destination drives
- **Alt+drag**: Create shortcut at destination

### 2.5 Compress / Extract (Archive Support)
- **Create ZIP**: Right-click > Compress to ZIP file
- **Create 7z**: Right-click > Compress to 7z file (24H2+)
- **Create TAR**: Right-click > Compress to TAR file (24H2+)
- **Archive creation wizard**: Choose compression method, level, and format when creating archives (24H2+)
- **Extract ZIP**: Double-click to browse; right-click > Extract All
- **Extract 7z**: Native extraction support (24H2+)
- **Extract TAR**: Native extraction support including tar.gz, tar.bz2, tar.xz, tar.zst (24H2+)
- **Extract RAR**: Native extraction support (23H2+)
- **Extract gz, bz2, xz, zst**: Various compression format support
- **Browse archives**: Navigate inside archive files like regular folders
- **Powered by libarchive**: Open-source library used for native archive support
- **Limitation**: No support for encrypted archives natively; no native RAR creation

### 2.6 Properties Dialog
- **General tab**: File name, type, location, size, size on disk, created/modified/accessed dates, attributes (Read-only, Hidden)
- **Security tab**: NTFS permissions, owner, ACL management, advanced security settings
- **Details tab**: File metadata (author, title, subject, tags, comments, camera info for photos, audio/video metadata)
- **Previous Versions tab**: File history / shadow copies / restore points
- **Sharing tab**: Network sharing settings for folders
- **Customize tab**: Folder type optimization, change folder icon, change folder picture
- **Compatibility tab**: (For executables) compatibility mode settings
- **Shortcut tab**: (For .lnk files) target, start in, shortcut key, run mode
- **Disk properties**: (For drives) Used/free space, disk cleanup, tools (error checking, defragment), sharing, security, hardware, quota management
- **Edit metadata**: Some fields in the Details tab are editable (e.g., tags, title, author)
- **Remove metadata**: "Remove Properties and Personal Information" option in Details tab
- **Keyboard shortcut**: `Alt+Enter` opens Properties for selected item

### 2.7 Share Functionality
- **Share button**: Command bar share button opens Windows Share dialog
- **Windows Share dialog**: Share via Nearby Sharing (Bluetooth/Wi-Fi), email, or installed apps
- **OneDrive sharing**: Generate shareable links for OneDrive-synced files
- **Share via context menu**: Right-click > Share
- **Multi-file sharing**: Updated drag tray supports sharing multiple files with relevant apps (2025+)
- **Copy link**: Generate and copy a sharing link for cloud-stored files

---

## 3. View Modes

### 3.1 Layout Options
- **Extra large icons** (`Ctrl+Shift+1`): Very large thumbnail/icon display
- **Large icons** (`Ctrl+Shift+2`): Large thumbnail/icon display
- **Medium icons** (`Ctrl+Shift+3`): Medium thumbnail/icon display
- **Small icons** (`Ctrl+Shift+4`): Small icon grid display
- **List** (`Ctrl+Shift+5`): Compact list with small icons
- **Details** (`Ctrl+Shift+6`): Columnar view with file metadata (name, date modified, type, size)
- **Tiles** (`Ctrl+Shift+7`): Medium icons with file type and size info beside each item
- **Content** (`Ctrl+Shift+8`): Detailed content view with preview and metadata
- **Ctrl+Mouse wheel**: Continuously adjust icon size

### 3.2 Column Headers (Details View)
- **Default columns**: Name, Date modified, Type, Size
- **Additional columns**: Date created, Date accessed, Authors, Tags, Title, Categories, Size on disk, Owner, Computer, Rating, Dimensions, Bit rate, and many more (100+ available)
- **Sort by column**: Click column header to sort ascending/descending
- **Sort by multiple columns**: Hold Shift while clicking additional column headers
- **Group by column**: Right-click column header > Group by; groups files by the selected column value
- **Filter by column**: Click the dropdown arrow on a column header to filter (checkboxes for specific values)
- **Resize columns**: Drag column header borders to resize
- **Auto-fit columns**: `Ctrl+Plus(+)` or right-click header > Size All Columns to Fit
- **Add/remove columns**: Right-click column header to add/remove columns
- **Choose columns dialog**: Right-click header > More... to see all available columns
- **Reorder columns**: Drag column headers to rearrange
- **Custom columns**: Shell extensions can register custom property columns

### 3.3 Preview Pane
- **Toggle**: `Alt+P` or View > Preview
- **Supported types**: Text files, images, PDFs, Office documents, videos, audio (with appropriate handlers)
- **Extensible**: Third-party preview handlers can be installed (e.g., PowerToys adds SVG, Markdown, source code preview)
- **Mark of the Web protection**: Preview pane blocked for internet-downloaded files by default (25H2 security change)
- **Resizable**: Drag the pane border to resize

### 3.4 Details Pane
- **Toggle**: `Alt+Shift+P` or View > Details
- **Modern details pane**: Shows file preview, metadata, sharing status, recent activity, related files, and collaboration info
- **Edit metadata**: Some metadata fields are editable directly in the Details pane
- **Collaboration features**: Shows recent comments, shared status, and People cards for collaborators
- **OneDrive status**: Shows sync status for cloud-synced files

### 3.5 Gallery View
- **Photo collection view**: Displays photos from configured folders in a timeline layout
- **Collection dropdown**: Choose which folders are included in Gallery
- **OneDrive Camera Roll**: Integrates with OneDrive camera backup
- **Android phone photos**: Wireless access to Android device photos (via Phone Link)
- **Timeline organization**: Photos organized chronologically
- **Navigation pane shortcut**: Gallery appears in the sidebar

### 3.6 Compact / Comfortable Spacing
- **Compact view**: View > Compact view -- reduces spacing between items (better for mouse users)
- **Default spacing**: Standard spacing optimized for touch input
- **System-wide setting**: Applies across all File Explorer windows

### 3.7 Show / Hide Options
- **File name extensions**: View > Show > File name extensions
- **Hidden items**: View > Show > Hidden items
- **Item checkboxes**: View > Show > Item check boxes
- **Navigation pane**: View > Show > Navigation pane
- **Status bar**: Configurable via Folder Options > View > Advanced settings

---

## 4. Selection

### 4.1 Mouse Selection
- **Single click**: Select an item (default; can be configured to single-click-to-open)
- **Ctrl+Click**: Add/remove individual items from selection
- **Shift+Click**: Select a contiguous range of items
- **Rubber band selection**: Click and drag on empty space to draw a selection rectangle
- **Ctrl+rubber band**: Add to existing selection with rubber band

### 4.2 Keyboard Selection
- **Arrow keys**: Move selection focus
- **Shift+Arrow keys**: Extend selection
- **Ctrl+Arrow keys**: Move focus without changing selection
- **Ctrl+Space**: Toggle selection of focused item
- **Home / End**: Jump to first/last item
- **Shift+Home / Shift+End**: Select from current to first/last item
- **Page Up / Page Down**: Scroll by page
- **Shift+Page Up / Shift+Page Down**: Extend selection by page

### 4.3 Selection Commands
- **Select All** (`Ctrl+A`): Select all items in current folder
- **Select None**: Via "See more" menu (...) > Select none
- **Invert Selection**: Via "See more" menu (...) > Invert selection

### 4.4 Checkbox Selection Mode
- **Enable**: View > Show > Item check boxes
- **Behavior**: Hovering over an item reveals a checkbox; click the checkbox to select without holding Ctrl
- **Particularly useful for touch/tablet users**

---

## 5. Search

### 5.1 Search Bar
- **Focus shortcuts**: `Ctrl+E`, `Ctrl+F`, or `F3` to focus the search box
- **Search scope**: Searches within the current folder and its subfolders by default
- **Search UI toolbar**: After searching, a toolbar appears with filter options (Kind, Size, Date modified, Other properties)
- **Search suggestions**: Dropdown suggestions based on search history and indexed content
- **Clear search**: `Esc` to clear search and return to folder view

### 5.2 Search Indexing
- **Windows Search service**: Background indexing of file contents and metadata
- **Indexed locations**: Configurable; default includes user profile folders
- **Non-indexed search**: Falls back to slower enumeration-based search for non-indexed locations
- **Index rebuilding**: Can rebuild the search index from Settings > Privacy & Security > Searching Windows
- **Enhanced search mode**: Option to search entire PC (not just indexed locations)

### 5.3 Search Syntax (Advanced Query Syntax - AQS)
- **kind:**: Filter by file kind (e.g., `kind:document`, `kind:image`, `kind:music`, `kind:video`, `kind:folder`, `kind:program`, `kind:email`, `kind:note`, `kind:game`)
- **type:**: Filter by file type/extension (e.g., `type:.pdf`, `type:.docx`)
- **ext:**: Filter by file extension (e.g., `ext:.png`)
- **size:**: Filter by file size (e.g., `size:>5MB`, `size:empty`, `size:tiny`, `size:small`, `size:medium`, `size:large`, `size:huge`, `size:gigantic`)
- **date:**: Filter by date modified (e.g., `date:today`, `date:yesterday`, `date:last week`, `date:last month`, `date:2025-01-01..2025-01-31`)
- **datemodified:**: Explicit date modified filter
- **datecreated:**: Filter by creation date
- **name:**: Filter by file name
- **tag:**: Filter by file tags
- **author:**: Filter by document author
- **Boolean operators**: `AND`, `OR`, `NOT` (e.g., `report AND budget`, `type:.pdf OR type:.docx`, `report NOT draft`)
- **Wildcards**: `*` for multiple characters, `?` for single character
- **Quotes**: Exact phrase matching (e.g., `"quarterly report"`)
- **Parentheses**: Group conditions (e.g., `(type:.pdf OR type:.docx) AND date:this week`)
- **Comparison operators**: `>`, `<`, `>=`, `<=`, `=` for numeric/date fields

### 5.4 Search from Address Bar
- **Direct search**: Typing in the address bar searches if input is not a valid path
- **Web search**: Can trigger web search from the address bar

---

## 6. Drag & Drop

### 6.1 Within Explorer
- **Default drag within same drive**: Move operation
- **Default drag between drives**: Copy operation
- **Shift+drag**: Force move
- **Ctrl+drag**: Force copy
- **Alt+drag**: Create shortcut
- **Visual feedback**: Drag cursor shows operation type (copy, move, link icons)
- **Drop target highlighting**: Folders highlight when hovered during drag

### 6.2 Between Explorer Windows
- **Tab-to-tab drag**: Drag items between tabs in the same or different File Explorer windows
- **Window-to-window drag**: Drag between separate File Explorer windows
- **Same rules apply**: Same drive = move, different drive = copy, modifiers override

### 6.3 To/From Desktop and Other Apps
- **Drag to desktop**: Copy/move files to desktop
- **Drag from desktop**: Drag files from desktop into Explorer
- **Drag to/from other apps**: Standard Windows drag-and-drop interop with any app supporting OLE drag-and-drop
- **Drag to taskbar apps**: Hover over taskbar icon to bring that app to front, then drop

### 6.4 Drag to Breadcrumb / Sidebar
- **Drag to breadcrumb segments**: Move/copy files to any folder in the breadcrumb path (restored in 2025)
- **Drag to sidebar folders**: Move/copy files to Quick Access pinned folders, drives, or navigation pane items
- **Drag to tab**: Drag files onto a tab header to switch to that tab and drop

### 6.5 Spring-loaded Folders
- **Hover to expand**: When dragging over a folder, hovering briefly causes it to open/expand so you can drop inside a subfolder

---

## 7. Keyboard Shortcuts

### 7.1 General / Window Management
| Shortcut | Action |
|----------|--------|
| `Win+E` | Open new File Explorer window |
| `Ctrl+N` | Open new File Explorer window (from existing) |
| `Ctrl+W` | Close current tab (or window if only one tab) |
| `Alt+F4` | Close File Explorer window |
| `F11` | Toggle full-screen mode |
| `Alt+Space` | Open window system menu (Move, Size, Minimize, Maximize, Close) |
| `F5` | Refresh current folder |
| `F6` | Cycle through panes/elements in the window |
| `Tab` | Cycle focus forward through UI elements |
| `Shift+Tab` | Cycle focus backward through UI elements |
| `Esc` | Cancel current operation / close search / close dialog |

### 7.2 Navigation
| Shortcut | Action |
|----------|--------|
| `Alt+Left Arrow` | Navigate back |
| `Alt+Right Arrow` | Navigate forward |
| `Alt+Up Arrow` | Navigate to parent folder |
| `Backspace` | Navigate back (same as Alt+Left) |
| `Enter` | Open selected item |
| `Alt+Enter` | Open Properties for selected item |
| `Alt+D` / `Ctrl+L` / `F4` | Focus and select the address bar |
| `Ctrl+E` / `Ctrl+F` / `F3` | Focus the search box |

### 7.3 Tab Management
| Shortcut | Action |
|----------|--------|
| `Ctrl+T` | Open new tab |
| `Ctrl+W` | Close current tab |
| `Ctrl+Tab` | Switch to next tab |
| `Ctrl+Shift+Tab` | Switch to previous tab |
| `Ctrl+1` through `Ctrl+9` | Switch to tab by number |

### 7.4 Selection
| Shortcut | Action |
|----------|--------|
| `Ctrl+A` | Select all items |
| `Arrow keys` | Move focus/selection |
| `Shift+Arrow keys` | Extend selection |
| `Ctrl+Arrow keys` | Move focus without selecting |
| `Ctrl+Space` | Toggle selection of focused item |
| `Home` | Move to first item |
| `End` | Move to last item |
| `Shift+Home` | Select from current to first item |
| `Shift+End` | Select from current to last item |

### 7.5 File Operations
| Shortcut | Action |
|----------|--------|
| `Ctrl+C` | Copy |
| `Ctrl+X` | Cut |
| `Ctrl+V` | Paste |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` | Redo |
| `Delete` / `Ctrl+D` | Delete (to Recycle Bin) |
| `Shift+Delete` | Permanent delete |
| `F2` | Rename selected item |
| `Ctrl+Shift+N` | Create new folder |
| `Ctrl+Shift+E` | Expand all folders in navigation pane tree |

### 7.6 View
| Shortcut | Action |
|----------|--------|
| `Alt+P` | Toggle Preview pane |
| `Alt+Shift+P` | Toggle Details pane |
| `Ctrl+Shift+1` | Extra large icons |
| `Ctrl+Shift+2` | Large icons |
| `Ctrl+Shift+3` | Medium icons |
| `Ctrl+Shift+4` | Small icons |
| `Ctrl+Shift+5` | List view |
| `Ctrl+Shift+6` | Details view |
| `Ctrl+Shift+7` | Tiles view |
| `Ctrl+Shift+8` | Content view |
| `Ctrl+Mouse scroll wheel` | Zoom in/out (change icon size) |
| `Ctrl+Plus(+)` | Auto-fit all columns in Details view |

### 7.7 Context and Menus
| Shortcut | Action |
|----------|--------|
| `Shift+F10` | Open context menu for selected item |
| `Shift+Right-click` | Open extended/legacy context menu directly |
| `Alt` | Show keyboard accelerators |
| `Alt+F` | Open File menu |

### 7.8 Navigation Pane (Tree)
| Shortcut | Action |
|----------|--------|
| `Left Arrow` | Collapse current node / select parent |
| `Right Arrow` | Expand current node / select first child |
| `*` (Numpad) | Expand all children of selected node |
| `Ctrl+Shift+E` | Expand entire tree to current folder |

### 7.9 Modifier+Drag Shortcuts
| Shortcut | Action |
|----------|--------|
| `Drag` | Default (move same drive, copy different drive) |
| `Shift+Drag` | Force move |
| `Ctrl+Drag` | Force copy |
| `Alt+Drag` | Create shortcut |

---

## 8. Context Menu

### 8.1 Windows 11 New Context Menu (Default)
- **Top row icons**: Cut, Copy, Paste, Rename, Share, Delete (icon-only action bar)
- **Open**: Open with default application
- **Open with**: Choose application to open file with; includes "Choose another app" option
- **Open in new window**: Opens folder in new File Explorer window
- **Open in new tab**: Opens folder in new tab
- **Edit**: Open with associated editor (for supported types)
- **Edit with [App]**: Context-aware editing options (e.g., "Edit with Paint", "Edit with Photos")
- **Print**: Print the selected file
- **Set as desktop background**: For image files
- **Rotate right / Rotate left**: For image files
- **Manage file flyout**: Groups "Compress to ZIP file", "Copy as path", "Set as Desktop Background", "Rotate" (2025 reorganization)
- **AI Actions**: Right-click context entry for AI-powered actions on files (2025+)
- **Copy as path**: Copies the full file path wrapped in quotes to clipboard
- **Compress to...**: ZIP, 7z, TAR options
- **Extract All**: For archive files
- **Pin to Quick Access**: Pin folder to sidebar Quick Access
- **Unpin from Quick Access**: Remove folder from Quick Access
- **Pin to Start**: Pin item to Start menu (primarily for apps/executables)
- **Properties**: Open Properties dialog
- **Show more options**: Reveals the full legacy/classic context menu

### 8.2 Legacy/Classic Context Menu ("Show more options" or Shift+Right-click)
- **All items from the new menu plus**:
- **Send to**: Submenu with Bluetooth device, Compressed folder, Desktop shortcut, Documents, Fax recipient, Mail recipient, and custom Send To targets
- **Open with**: Extended open-with options
- **Create shortcut**: Create a .lnk shortcut file
- **New**: Create new items (folder, text file, shortcut, etc.)
- **Give access to**: Network sharing options (specific people, remove access)
- **Include in library**: Add folder to a library
- **Pin to taskbar**: (For executables)
- **Run as administrator**: (For executables)
- **Troubleshoot compatibility**: (For executables)
- **Scan with Microsoft Defender**: Virus scan
- **Restore previous versions**: Access file history
- **Copy**: Copy file(s)
- **Cut**: Cut file(s)
- **Paste**: Paste clipboard content
- **Delete**: Delete file(s)
- **Rename**: Rename file
- **Third-party shell extensions**: 7-Zip, Git, VS Code, etc. add entries here

### 8.3 Folder Background Context Menu (Right-click on empty space)
- **View**: Submenu for layout options
- **Sort by**: Submenu for sort criteria
- **Group by**: Submenu for grouping criteria
- **Refresh**: Refresh folder contents
- **Customize this folder**: Open folder customization
- **New**: Create new items
- **Paste**: Paste clipboard content
- **Paste shortcut**: Paste as shortcut
- **Undo**: Undo last operation
- **Properties**: Folder properties
- **Open in Terminal**: Launch terminal at current path
- **Show more options**: Access legacy context menu

### 8.4 Desktop Context Menu
- **Display settings**: Open display settings
- **Personalize**: Open personalization settings
- **View**: Icon size, auto-arrange, align to grid, show desktop icons
- **Sort by**: Name, size, type, date modified
- **Refresh**: Refresh desktop
- **New**: Create new items on desktop

---

## 9. File System Features

### 9.1 Drive Management
- **This PC view**: Shows all local drives with used/free space visualization
- **Drive properties**: Capacity, used space, free space bar chart
- **Disk Cleanup**: Built-in storage cleanup utility
- **Drive formatting**: Right-click drive > Format
- **Error checking**: Tools tab > Check (chkdsk)
- **Defragment and Optimize**: Tools tab > Optimize
- **BitLocker management**: (If enabled) Manage BitLocker encryption from drive context menu
- **Drive letters**: Shown next to drive names
- **Ejecting removable drives**: Right-click > Eject for USB drives, optical media

### 9.2 Network Drives
- **Map network drive**: This PC > ... menu > Map network drive; assign a drive letter to a network path
- **Disconnect network drive**: Right-click mapped drive > Disconnect; or ... menu > Disconnect network drive
- **Reconnect at logon**: Option when mapping to reconnect the drive automatically
- **Network discovery**: Browse network locations
- **UNC paths**: Navigate directly to `\\server\share` in the address bar
- **WebDAV support**: Map WebDAV locations as drives
- **SMB protocol**: Standard Windows file sharing protocol

### 9.3 Cloud Storage Integration (OneDrive)
- **OneDrive in sidebar**: OneDrive Personal and Business folders appear in navigation pane
- **Sync status icons**: Overlay icons showing sync state (synced, syncing, pending, cloud-only, always available)
- **Files On-Demand**: Download files only when accessed; free up space by making files online-only
- **Status column**: Sync status column in Details view
- **Right-click OneDrive options**: Free up space, Always keep on this device, Share, View online
- **Colored folders**: OneDrive supports colored folders in File Explorer (2025+)
- **Intelligent Search**: On Copilot+ PCs, searches both local and cloud files simultaneously
- **Copilot integration**: Right-click > OneDrive submenu with Copilot actions (summarize, create FAQ, compare files)

### 9.4 Libraries
- **Purpose**: Virtual folders that aggregate content from multiple physical locations
- **Default libraries**: Documents, Music, Pictures, Videos
- **Custom libraries**: Create new libraries combining multiple folders
- **Library management**: Add/remove folders to/from a library
- **Enable in navigation pane**: View > Navigation pane > Show libraries
- **Folder type optimization**: Libraries can be optimized for General items, Documents, Music, Pictures, or Videos

### 9.5 Recycle Bin Management
- **Recycle Bin folder**: Accessible from desktop icon and navigation pane
- **Restore items**: Right-click > Restore to return items to original location
- **Restore all**: Recycle Bin toolbar > Restore all items
- **Empty Recycle Bin**: Permanently delete all items in Recycle Bin
- **Per-drive settings**: Each drive has its own Recycle Bin with configurable size
- **Bypass Recycle Bin option**: Can configure to skip Recycle Bin (permanent delete by default)
- **Display delete confirmation**: Optional confirmation dialog before deleting
- **Sort/filter in Recycle Bin**: Sort by name, original location, date deleted, size, type

### 9.6 File Permissions / Security
- **Security tab in Properties**: View and edit NTFS permissions (ACLs)
- **Owner information**: View and change file/folder owner
- **Advanced Security Settings**: Detailed permission entries, inheritance, auditing
- **Effective Access**: Check effective permissions for a specific user/group
- **Take ownership**: Take ownership of files/folders
- **Sharing permissions**: Configure network share permissions separately from NTFS
- **Read-only / Hidden attributes**: Set via Properties > General tab

### 9.7 Symbolic Links, Junctions, Hard Links
- **Symbolic links**: Displayed with shortcut overlay arrow; transparent navigation
- **Junctions**: Directory junctions shown in file listing; transparent navigation
- **Hard links**: Multiple file names pointing to same data; no visual indicator in Explorer
- **Create symbolic links**: Requires `mklink` command (no GUI in Explorer); no admin privilege required on Windows 11
- **Navigate through links**: File Explorer transparently follows symbolic links and junctions
- **Type column**: Shows "Shortcut" or file type; junctions/symlinks may show special indicators

### 9.8 Alternate Data Streams
- **No native GUI support**: File Explorer does not display or manage NTFS Alternate Data Streams
- **Mark of the Web**: Zone.Identifier ADS used for security (downloaded file tracking)
- **Properties > Unblock**: Can remove Zone.Identifier ADS via Properties > General > "Unblock" checkbox

---

## 10. Integration

### 10.1 Shell Extensions
- **Context menu handlers**: Third-party apps add context menu items (e.g., 7-Zip, Git, VS Code "Open with Code")
- **Property sheet handlers**: Add custom tabs to Properties dialog
- **Copy hook handlers**: Intercept file copy/move/rename/delete operations
- **Drag-and-drop handlers**: Customize behavior when files are dropped
- **Icon overlay handlers**: Add overlay icons to file/folder icons (e.g., OneDrive sync status, Git status, Dropbox)
- **Column handlers**: Add custom columns to Details view
- **InfoTip handlers**: Customize tooltip content for files
- **Shell namespace extensions**: Virtual folders and custom navigation

### 10.2 Thumbnail Providers
- **Built-in thumbnails**: Images, videos, PDFs, Office documents
- **Custom thumbnail providers**: Third-party COM servers provide thumbnails for custom file types
- **Thumbnail caching**: Thumbnails stored in `thumbcache_*.db` files for performance
- **Thumbnail size options**: Multiple resolution tiers (32, 96, 256, 1024, etc.)

### 10.3 Preview Handlers
- **Built-in handlers**: Text, images, PDF, Office documents, HTML, certain media files
- **PowerToys extensions**: SVG, Markdown, source code, PDF enhanced preview
- **Third-party handlers**: Extensible system for any file type
- **Registration**: COM-based registration in Windows Registry

### 10.4 Property Handlers
- **Built-in properties**: Standard metadata for common file types (images, documents, audio, video)
- **Custom properties**: Third-party property handlers expose custom metadata
- **Indexable properties**: Properties indexed by Windows Search for fast querying
- **Editable properties**: Some properties can be edited directly in Details pane or Properties dialog

### 10.5 Version Control Integration (2025+)
- **Git integration**: Shows branch name, last commit author, last commit message for files in Git repositories
- **Dev Home integration**: Works with Dev Home for enhanced developer features
- **WSL repository support**: Supports Git repositories in WSL file systems
- **Read-only**: View-only; cannot switch branches or commit from Explorer
- **Configuration**: Repository folders configured in Windows Advanced Settings
- **Source control columns**: Additional columns showing Git status in Details view

### 10.6 AI Actions Integration (2025+)
- **Image AI actions**: Bing Visual Search, Blur Background (Photos), Erase Objects (Photos), Remove Background (Paint)
- **Document AI actions**: Summarize (requires Microsoft 365 + Copilot license), Create FAQ
- **File comparison**: Compare up to 5 files via Copilot
- **Third-party AI access**: Testing framework for third-party AI apps (Claude, etc.) to access files through File Explorer
- **Supported formats**: JPG, JPEG, PNG for images; DOCX, DOC, PPTX, PPT, XLSX, XLS, PDF, RTF, TXT, LOOP for documents
- **Copilot sidebar**: Planned Copilot integration in File Explorer sidebar (similar to Preview/Details pane)

---

## 11. Accessibility

### 11.1 Screen Reader Support
- **Narrator compatibility**: Full support for Windows Narrator screen reader
- **ARIA/UIA support**: UI Automation properties for all controls
- **Screen reader announcements**: File operations, navigation changes, selection changes announced
- **Standard keyboard layout for Narrator**: More intuitive keyboard layout matching other screen readers

### 11.2 High Contrast / Themes
- **High contrast themes**: Full support for Windows high contrast themes
- **Dark mode**: Complete dark mode support including dialogs, progress windows, and conflict resolution dialogs (enhanced in 2025)
- **Light mode**: Standard light theme
- **Accent color**: Respects system accent color
- **Custom themes**: Supports custom Windows themes

### 11.3 Keyboard-Only Navigation
- **Full keyboard accessibility**: Every feature accessible via keyboard
- **Tab order**: Logical tab order through all UI elements
- **Access keys**: Alt key reveals keyboard accelerators
- **Arrow key navigation**: Navigate file lists, tree views, breadcrumbs
- **Keyboard context menu**: `Shift+F10` or Menu key

### 11.4 Touch / Tablet Mode
- **Touch-friendly spacing**: Default comfortable spacing designed for touch input
- **Touch context menu**: Long-press for context menu
- **Swipe gestures**: Swipe to select items
- **Pinch to zoom**: Change icon/thumbnail size
- **On-screen keyboard**: Automatically appears when needed
- **Drag and drop via touch**: Touch-based drag and drop supported

### 11.5 Display Scaling
- **DPI awareness**: Properly scales on high-DPI displays
- **Per-monitor DPI**: Supports different scaling on different monitors
- **Text scaling**: Dialog boxes and UI honor system text scaling settings (improved in 2025)
- **Large fonts**: Supports system-wide font size changes

---

## 12. Command Bar (Toolbar)

### 12.1 Dynamic Command Bar
- **Context-sensitive**: Buttons change based on selection type and current view
- **Standard buttons**: New, Cut, Copy, Paste, Rename, Share, Delete, Sort, View
- **See more (...) menu**: Additional options including Select all, Select none, Invert selection, Options
- **Folder-specific options**: Different options for This PC, Recycle Bin, Libraries, etc.
- **Not customizable**: Cannot add/remove/rearrange buttons (unlike Windows 10 ribbon)

### 12.2 Sort & Group Options Menu
- **Sort by**: Name, Date modified, Type, Size, Date created, Authors, Categories, Tags, Title
- **Group by**: Same fields as sort; plus (None) to remove grouping
- **Ascending/Descending**: Toggle sort direction

### 12.3 Layout & View Options Menu
- **Layout options**: All 8 view modes (Extra large icons through Content)
- **Compact view**: Toggle compact spacing
- **Show/Hide**: File name extensions, Hidden items, Item check boxes, Navigation pane

---

## 13. Status Bar

- **Item count**: Shows number of items in current folder
- **Selected items**: Shows number and total size of selected items
- **Free space**: Shows free space on current drive
- **View toggle**: Quick buttons to switch between Details and Large icons view
- **Toggle visibility**: Can be shown/hidden via Folder Options > View > Advanced settings

---

## 14. Folder Options (Advanced Settings)

### 14.1 General Tab
- **Open File Explorer to**: Home, This PC, or custom folder
- **Browse folders**: Open each folder in same window or new window
- **Click items as follows**: Single-click to open or double-click to open
- **Privacy**: Show recently used files, Show frequently used folders, Show files from Office.com, Clear history

### 14.2 View Tab (Advanced Settings)
- **Always show icons, never thumbnails**: Disable thumbnails for performance
- **Always show menus**: Show classic menu bar
- **Display file icon on thumbnails**: Show file type icon overlay on thumbnails
- **Display file size information in folder tips**: Show total size when hovering folders
- **Display the full path in the title bar**: Show full path instead of folder name
- **Hidden files and folders**: Show or hide hidden items
- **Hide empty drives**: Hide drives with no media
- **Hide extensions for known file types**: Toggle extension visibility
- **Hide folder merge conflicts**: Skip conflict dialog when merging folders
- **Hide protected operating system files**: Hide system files (Recommended)
- **Launch folder windows in a separate process**: Isolate Explorer windows
- **Restore previous folder windows at logon**: Session restore
- **Show drive letters**: Show/hide drive letters
- **Show encrypted or compressed NTFS files in color**: Color-code encrypted (green) and compressed (blue) files
- **Show pop-up description for folder and desktop items**: Tooltips
- **Show preview handlers in preview pane**: Enable/disable preview pane content
- **Show status bar**: Toggle status bar
- **Show sync provider notifications**: Toggle cloud sync notifications
- **Use check boxes to select items**: Checkbox selection mode
- **Use Sharing Wizard**: Simplified sharing dialog vs. advanced permissions
- **When typing into list view**: Select matching item / Type in search box
- **Apply to Folders**: Apply current view settings to all folders of same type
- **Reset Folders**: Reset all folder views to defaults

### 14.3 Search Tab
- **What to search**: Indexed locations vs. always search file names and contents
- **How to search**: Include system directories, Include compressed files (ZIP, CAB), Always search file names and contents
- **When searching non-indexed locations**: Include system directories, Include compressed files

---

## 15. Progress Dialogs & Conflict Resolution

### 15.1 File Transfer Progress
- **Progress bar**: Visual progress indicator for copy/move/delete operations
- **Speed and time estimate**: Shows transfer speed and estimated time remaining
- **More details / Fewer details**: Expandable view showing per-file progress, throughput graph
- **Pause / Resume**: Pause and resume file transfer operations
- **Cancel**: Cancel the operation
- **Multiple operations**: Multiple concurrent operations shown in same dialog with individual progress bars
- **Background operation**: Progress dialog can be minimized; operation continues in background
- **Dark mode support**: Updated blue progress bar matching Windows 11 palette (2025)

### 15.2 File Conflict Resolution
- **Replace or Skip dialog**: When destination contains items with same name
- **Replace**: Overwrite existing file
- **Skip**: Keep existing file, skip the conflicting one
- **Compare info**: Shows both files' sizes and dates for comparison
- **Let me decide for each file**: Review conflicts individually
- **Keep both**: Rename the incoming file (append number)
- **Apply to all**: Apply same decision to all remaining conflicts
- **Folder merge**: When copying folder that already exists at destination

### 15.3 Delete Confirmation
- **Recycle Bin confirmation**: Optional dialog confirming delete to Recycle Bin
- **Permanent delete confirmation**: Always shown for Shift+Delete operations
- **Multiple items**: Shows count of items being deleted

---

## 16. Miscellaneous Features

### 16.1 Type-Ahead / Incremental Search
- **Type to select**: Start typing a file name to jump to matching items in the file list
- **Incremental search**: Characters typed within a short window are accumulated for matching
- **Configurable behavior**: Folder Options > View > "When typing into list view" -- select matching item vs. search box

### 16.2 Tooltips / InfoTips
- **File tooltips**: Hovering over files shows type, size, date modified
- **Folder tooltips**: Shows folder size (if enabled) and date modified
- **Thumbnail preview in tooltip**: Some file types show a small preview
- **Custom InfoTips**: Shell extension InfoTip handlers can customize tooltip content

### 16.3 File Preview on Hover
- **Thumbnail generation**: Thumbnails generated for images, videos, documents
- **Live folder previews**: Folder icons can show previews of contained images

### 16.4 Path Operations
- **Copy as path**: Right-click > Copy as path (or `Ctrl+Shift+C` in some contexts) -- copies full path with quotes
- **Open in Terminal**: Right-click folder background > Open in Terminal
- **Open command window here**: Available via extended context menu
- **Open PowerShell window here**: Available via extended context menu

### 16.5 Folder Customization
- **Optimize folder for**: General items, Documents, Music, Pictures, Videos (changes default columns and view)
- **Change folder icon**: Properties > Customize > Change Icon
- **Change folder picture**: Folder thumbnail can be customized
- **Apply view to subfolders**: Apply current folder's view to all subfolders

### 16.6 Clipboard History
- **Windows clipboard history** (`Win+V`): Access clipboard history including previously copied files
- **Clipboard across devices**: Sync clipboard content across devices (if enabled)

### 16.7 Snap Layouts
- **Snap assist**: Windows 11 snap layouts work with File Explorer windows
- **Drag to screen edge**: Snap File Explorer to half/quarter of screen
- **Win+Arrow keys**: Snap windows using keyboard

### 16.8 Multi-Monitor Support
- **Window management**: File Explorer windows can span or be placed on any monitor
- **Per-monitor DPI**: Scales correctly across different DPI monitors

### 16.9 Performance Features
- **Virtual scrolling**: Only renders visible items for large folders
- **Lazy loading**: Thumbnails and metadata loaded on demand
- **Background enumeration**: Folder contents loaded asynchronously
- **Thumbnail caching**: Pre-computed thumbnails stored in database files
- **Optimized Home page loading**: Faster loading of Home tab (2025 improvements)
- **Reduced RAM usage**: Narrowed search parameters for efficiency (2026 update)

### 16.10 Developer Features
- **Open in Terminal**: Open Windows Terminal at current folder location
- **Git integration**: View branch, commit info for repositories (2025+)
- **WSL integration**: Navigate Linux file systems via `\\wsl$\` or Linux entry in sidebar
- **Dev Drive**: Optimized storage volume for development workloads (ReFS-based)

### 16.11 Phone Integration
- **Android phone access**: Browse Android phone files wirelessly via Phone Link
- **Gallery integration**: Android photos appear in File Explorer Gallery
- **Phone Link sidebar**: Battery status, messages, recent photos from connected phone

---

## 17. Summary of Features by Windows 11 Version

### Windows 11 22H2 (Original)
- Redesigned File Explorer with WinUI 3
- New simplified context menu with "Show more options"
- Command bar replacing ribbon
- Tabs support (added via update)

### Windows 11 23H2
- Gallery view
- Native RAR/7z/tar extraction
- Modern Details pane
- Home page with Recommended section

### Windows 11 24H2
- Native archive creation (ZIP, 7z, TAR with compression options)
- Tab session restore
- Duplicate tab feature
- Improved dark mode for dialogs
- Performance improvements

### Windows 11 25H2 / 2025 Updates
- AI Actions in context menu (image and document actions)
- Copilot integration (summarize, create FAQ, compare files)
- Git/version control integration via Dev Home
- Drag-and-drop to address bar restored
- Context menu reorganization (Manage file flyout)
- Enhanced dark mode for all file operation dialogs
- Mark of the Web preview pane protection
- Multi-file sharing in drag tray
- Android phone wireless file access in Gallery

### 2026 Updates (Early)
- Reduced RAM usage / faster search
- Third-party AI app file access framework
- Copilot sidebar exploration (in testing)
- GitHub repository integration enhancements

---

## Sources

- [Microsoft Support - File Explorer in Windows](https://support.microsoft.com/en-us/windows/file-explorer-in-windows-ef370130-1cca-9dc5-e0df-2f7416fe1cb1)
- [Microsoft Support - Keyboard shortcuts in Windows](https://support.microsoft.com/en-us/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec)
- [Windows Central - File Explorer's Best Features in 2025](https://www.windowscentral.com/microsoft/windows-11/file-explorers-best-features-available-in-2025-on-windows-11-version-25h2-and-24h2)
- [Windows Central - What's New on File Explorer 24H2](https://www.windowscentral.com/software-apps/windows-11/whats-new-on-file-explorer-on-windows-11-2024-update-version-24h2)
- [Windows Latest - File Explorer AI Features](https://www.windowslatest.com/2025/10/16/microsoft-confirms-windows-11s-file-explorer-is-getting-third-party-ai-features/)
- [Windows Latest - Tab Session Restore](https://www.windowslatest.com/2024/12/09/windows-11-24h2-file-explorer-now-restore-tabs-on-restart-gets-other-new-features/)
- [Windows Central - AI Actions Management](https://www.windowscentral.com/microsoft/windows-11/how-to-manage-ai-actions-in-file-explorer-on-windows-11)
- [Microsoft Learn - File Explorer Version Control](https://learn.microsoft.com/en-us/windows/advanced-settings/fe-version-control)
- [Microsoft Learn - Advanced Query Syntax](https://learn.microsoft.com/en-us/windows/win32/lwef/-search-2x-wds-aqsreference)
- [Pureinfotech - Windows 11 Archive Support](https://pureinfotech.com/windows-11-create-7-zip-tar-archive-files/)
- [Windows Central - December 2025 Update](https://www.windowscentral.com/microsoft/windows-11/top-16-features-on-windows-11s-december-9-2025-update-file-explorer-start-menu-virtual-workspaces-and-more)
- [Windows Latest - 2026 Features List](https://www.windowslatest.com/2026/02/05/list-of-new-features-coming-to-windows-11-in-2026-so-far-and-its-not-just-copilot-ai-stuff/)
- [XDA Developers - File Explorer Guide](https://www.xda-developers.com/file-explorer-windows-11/)
- [WebNots - File Explorer Keyboard Shortcuts](https://www.webnots.com/keyboard-shortcuts-for-file-explorer-in-windows-11/)
- [AnyTxt - Windows File Explorer Search Syntax](https://anytxt.net/windows-file-explorer-search-syntax/)
- [Windows Central - Search Efficiency Guide](https://www.windowscentral.com/software-apps/windows-11/how-to-boost-search-efficiency-on-file-explorer-for-windows-11)
