# macOS Finder Comprehensive Feature List

> Reference document for comparing against Span file explorer.
> Covers macOS Sequoia (15.x) and macOS Tahoe (26.x) as of February 2026.
> Primary focus on Column View (Miller Columns) since Span is a Miller Columns explorer.

---

## Table of Contents

1. [Column View (Miller Columns) Specific](#1-column-view-miller-columns-specific)
2. [Navigation](#2-navigation)
3. [File Operations](#3-file-operations)
4. [View Modes](#4-view-modes)
5. [Selection](#5-selection)
6. [Search](#6-search)
7. [Tags](#7-tags)
8. [Quick Look](#8-quick-look)
9. [Quick Actions](#9-quick-actions)
10. [Drag and Drop](#10-drag-and-drop)
11. [Keyboard Shortcuts](#11-keyboard-shortcuts)
12. [Context Menu](#12-context-menu)
13. [Integration](#13-integration)
14. [Accessibility](#14-accessibility)
15. [Status Bar](#15-status-bar)
16. [Toolbar](#16-toolbar)
17. [Finder Preferences/Settings](#17-finder-preferencessettings)
18. [Window Management](#18-window-management)

---

## 1. Column View (Miller Columns) Specific

### 1.1 Column Width Adjustment

| Feature | Description |
|---------|-------------|
| Drag to resize individual column | Drag the column divider (thin vertical line between columns) left or right to adjust width of the column to its left |
| Double-click to auto-fit column | Double-click a column divider to automatically resize that column to fit its longest filename |
| Option+drag to resize all columns simultaneously | Hold Option while dragging any column divider to change all column widths at once |
| Option+double-click to auto-fit all columns | Hold Option while double-clicking a divider to auto-fit every column based on its own content |
| Right-click column divider | Control-click the divider to access: "Right Size This Column," "Right Size All Columns Individually," or "Right Size All Columns Equally" |
| Column minimum width | Columns have a minimum width that prevents them from being resized too small to be usable |
| Resize handle indicator | Small double-line icon at the bottom of the column divider provides a visual affordance for resizing |

### 1.2 Preview Column (Rightmost Column)

| Feature | Description |
|---------|-------------|
| File preview display | When a file is selected, a preview panel appears as the rightmost column showing a thumbnail/preview of the file |
| File type-specific previews | Different preview content based on file type: image thumbnails, PDF pages, document previews, audio waveforms, video thumbnails |
| Metadata display | Shows file name, kind, size, date created, date modified, last opened date |
| "Show More" / "Show Less" toggle | Expandable metadata section that shows additional details (EXIF data for images, audio metadata for music, etc.) |
| Quick Action buttons | Action buttons in the preview column (Rotate, Markup, Create PDF, etc.) based on file type |
| Toggle preview visibility | View menu > "Show Preview" / "Hide Preview" to toggle the preview column on/off |
| Keyboard shortcut for preview | Shift+Cmd+P toggles the preview pane |
| Preview column resizing | Drag the divider between the last file column and the preview column to resize |
| Image preview | Full thumbnail preview of images with dimensions displayed |
| Tags display in preview | Shows any tags applied to the selected file |
| Preview Options customization | View > Show Preview Options lets you choose which metadata fields to display per file type |

### 1.3 Column Scrolling Behavior

| Feature | Description |
|---------|-------------|
| Horizontal auto-scroll | When navigating deeper into folders, the view auto-scrolls horizontally to keep the selected/active column visible |
| Vertical scrolling per column | Each column has its own independent vertical scroll for navigating long file lists |
| Trackpad horizontal scroll | Two-finger horizontal swipe on trackpad scrolls between columns |
| Scroll to keep selection visible | Auto-scrolls the active column to ensure the selected item is always visible |
| Smooth scroll animation | Animated smooth scrolling when auto-scrolling to new columns |
| Inertial scrolling | Momentum-based scrolling on trackpad gestures |
| Scroll bar visibility | Scroll bars appear on hover or can be set to always visible via System Settings |

### 1.4 Selection Highlighting Across Columns

| Feature | Description |
|---------|-------------|
| Active selection (focused column) | Currently selected item in the active column has a prominent highlight (system accent color) |
| Path highlighting | Selected folder items in parent columns maintain a dimmed/inactive highlight to show the navigation path |
| Inactive window highlighting | When Finder window is not focused, selected items show a gray highlight instead of accent color |
| Multi-selection in single column | Cmd+click or Shift+click to select multiple items within the same column; only one column can have multi-selection at a time |
| Selection persistence | When navigating left/right between columns, the previous column's selection is preserved |
| Selection outline/ring | Focus ring around the active column or item indicating keyboard focus |

### 1.5 Path Bar Behavior in Column View

| Feature | Description |
|---------|-------------|
| Path bar at bottom | Shows the full path of the currently selected item as clickable breadcrumb segments at the bottom of the window |
| Click path segment | Clicking any segment in the path bar navigates to that folder |
| Double-click path segment | Opens that folder in a new Finder window |
| Drag from path bar | You can drag a path segment (folder icon) to another location to copy/move/create alias |
| Path bar toggle | View > "Show Path Bar" / "Hide Path Bar" or Option+Cmd+P |
| Path bar context menu | Right-click a path segment for context menu options |
| Path separator display | Uses ">" or "/" as visual separators between path components |
| Full path as text | The path bar shows the full filesystem path with proper icons for each segment |

### 1.6 Column View Keyboard Navigation

| Feature | Description |
|---------|-------------|
| Up/Down arrows | Navigate between items within the current column |
| Right arrow | Enter the selected folder (opens it as the next column); for files, has no effect |
| Left arrow | Move back to the parent column and close/collapse the current column |
| Cmd+Down arrow | Open the selected item (folder opens in same window, file opens in default app) |
| Cmd+Up arrow | Go to the parent folder (navigate up one level) |
| Return/Enter | Initiates inline rename of the selected item (NOT open, unlike Windows) |
| Cmd+O | Open the selected item (equivalent to double-click) |
| Tab key | Moves focus forward through UI elements (sidebar, columns, search, etc.) |
| Shift+Tab | Navigate back through columns without collapsing the path (preserves the breadcrumb trail) |
| Space bar | Quick Look preview of the selected item |
| Type-ahead / type-to-select | Typing characters jumps to the first item matching those characters in the active column |
| Cmd+A | Select all items in the active column |
| Home / End | Jump to first / last item in the active column |
| Page Up / Page Down | Scroll one page up/down in the active column |
| Escape | Cancel current action (close Quick Look, deselect, etc.) |

### 1.7 Column View Sorting Per-Column

| Feature | Description |
|---------|-------------|
| Sort options | Name, Kind, Date Last Opened, Date Modified, Date Created, Date Added, Size, Tags |
| Global sort preference | Sort setting applies to all columns simultaneously (not per-column independent sorting) |
| "Arrange By" / "Group By" | Can group items by Kind, Date, Size, Tags, etc. within columns |
| Folders on top | Option to keep folders at the top when sorting by name (Finder Settings > Advanced) |
| View Options dialog (Cmd+J) | Access text size, icon display, icon preview toggle, sort criteria, and grouping for column view |
| "Use as Defaults" button | Apply current view settings as the default for all new Finder windows opened in column view |

### 1.8 Column View Display Options

| Feature | Description |
|---------|-------------|
| Text size adjustment | Adjustable from small (10pt) to large (16pt) via View Options (Cmd+J) |
| Show icons | Toggle file/folder icons on or off in column view |
| Show icon preview | Toggle whether icons show thumbnail previews (e.g., image thumbnails) or generic file type icons |
| Show preview column | Toggle the rightmost preview column on or off |
| Filename truncation | Long filenames show "..." in the middle; hover tooltip shows full name |
| Alternating row backgrounds | Subtle alternating row shading for readability |
| Item count per folder | Small triangle/disclosure and item count indicators on folder entries |

---

## 2. Navigation

### 2.1 Path Bar / Breadcrumbs

| Feature | Description |
|---------|-------------|
| Path bar at bottom of window | Toggle with View > Show/Hide Path Bar or Option+Cmd+P |
| Clickable segments | Each folder in the path is clickable to navigate there |
| Drag from segments | Drag a path segment to copy/move that folder reference |
| Double-click segment | Opens that folder in a new Finder window |
| Full path display | Shows the complete path from root or volume to current location |

### 2.2 Go To Folder

| Feature | Description |
|---------|-------------|
| Cmd+Shift+G | Opens "Go to Folder" dialog |
| Path autocomplete | Type-ahead autocomplete with Tab key in the dialog |
| Tilde (~) expansion | Supports ~ for home directory |
| Path history | Remembers recently typed paths |
| Environment variable support | Supports standard UNIX path conventions |
| Relative paths | Supports relative path navigation |

### 2.3 Back / Forward Navigation

| Feature | Description |
|---------|-------------|
| Cmd+[ | Navigate back to previous folder location |
| Cmd+] | Navigate forward (after going back) |
| Back/Forward toolbar buttons | Clickable arrows in the toolbar with long-press for history dropdown |
| History dropdown | Long-press or right-click back/forward buttons shows navigation history list |
| Three-finger swipe | Trackpad gesture for back/forward navigation |
| Mouse back/forward buttons | Support for mouse side buttons (button 4/5) |

### 2.4 Sidebar

| Feature | Description |
|---------|-------------|
| **Favorites section** | User-customizable list of favorite folders (Desktop, Documents, Downloads, Applications, Home, etc.) |
| **iCloud section** | iCloud Drive, Shared folders |
| **Locations section** | This Mac, external drives, network volumes, connected servers, disk images |
| **Tags section** | All tags with color indicators; click a tag to see all tagged items |
| Toggle sidebar | View > Show/Hide Sidebar or Option+Cmd+S |
| Drag to reorder | Drag items within sections to reorder |
| Drag folders to Favorites | Drag any folder from the file area to the Favorites section to add it |
| Remove from sidebar | Drag item off sidebar or right-click > "Remove from Sidebar" |
| Sidebar item badges | Shows eject icon for removable volumes, sync status for cloud items |
| Collapse/expand sections | Click section headers to collapse/expand |
| Sidebar width | Drag the sidebar divider to resize |
| Shared section | Shows other Macs on the network for screen sharing and file sharing |
| Recent Tags | Shows recently used tags for quick access |

### 2.5 Quick Access Locations

| Feature | Description |
|---------|-------------|
| Shift+Cmd+H | Home folder |
| Shift+Cmd+D | Desktop |
| Shift+Cmd+O | Documents |
| Option+Cmd+L | Downloads |
| Shift+Cmd+A | Applications |
| Shift+Cmd+U | Utilities |
| Shift+Cmd+C | Computer (root) |
| Shift+Cmd+I | iCloud Drive |
| Shift+Cmd+K | Network |
| Shift+Cmd+R | AirDrop |
| Shift+Cmd+F | Recents (all recently viewed files) |
| Cmd+Shift+Delete | Empty Trash |

### 2.6 AirDrop

| Feature | Description |
|---------|-------------|
| AirDrop in sidebar | Shows AirDrop in sidebar Locations section |
| Shift+Cmd+R | Opens AirDrop window |
| Drag to AirDrop | Drag files to AirDrop icon or to a visible nearby device |
| AirDrop discovery | Shows nearby Apple devices for wireless file transfer |

### 2.7 Recents

| Feature | Description |
|---------|-------------|
| Shift+Cmd+F | Opens Recents view showing all recently accessed files |
| Smart aggregation | Automatically collects recently opened, modified, or created files |
| Recents in sidebar | Recents appears as a default Favorites sidebar item |
| Sorted by date | Files shown in reverse chronological order of last access |

### 2.8 Tab Support

| Feature | Description |
|---------|-------------|
| Cmd+T | Open a new tab |
| Cmd+W | Close current tab |
| Ctrl+Tab / Ctrl+Shift+Tab | Switch between tabs |
| Cmd+Shift+T | Show/hide tab bar |
| Tab bar display | Shows all open tabs with folder names and icons |
| Drag files between tabs | Drag a file onto another tab header to move/copy it there |
| Drag tab out | Drag a tab out of the window to create a new window |
| Drag tab into window | Drag a tab from one window into another window's tab bar |
| Window > Merge All Windows | Consolidate all open Finder windows into one window with tabs |
| Window > Move Tab to New Window | Detach the current tab as a separate window |
| Cmd+click folder | Opens the folder in a new tab (configurable) |
| Double-click tab | (No action by default, but some workflows use it) |
| Tab close button | Hover over tab to reveal X button for closing |
| New tab location | New tabs open to the default Finder window location (configurable in Settings) |
| Tab context menu | Right-click a tab for options like Close Tab, Close Other Tabs, Move Tab to New Window |
| Per-tab view modes | Each tab can independently use a different view mode (Icon, List, Column, Gallery) |
| Per-tab navigation | Each tab maintains its own navigation history and current location |

### 2.9 Window Groups

| Feature | Description |
|---------|-------------|
| Multiple Finder windows | Can have multiple independent Finder windows open simultaneously |
| Window > Merge All Windows | Merge all windows into tabs in a single window |
| Cmd+N | Open a new Finder window |
| Window cycling | Cmd+` cycles between open Finder windows |

---

## 3. File Operations

### 3.1 Copy, Cut, Paste

| Feature | Description |
|---------|-------------|
| Cmd+C | Copy selected files to clipboard |
| Cmd+V | Paste copied files to current location (creates a copy) |
| Cmd+Option+V | Move files from clipboard to current location (cut+paste equivalent) |
| Cut behavior | macOS does not have a traditional Cmd+X for files; instead, you Copy then Option+Cmd+V to move |
| Clipboard indicator | No visual cut indicator on source files (unlike Windows dimming); the move happens atomically on paste |
| Cross-window paste | Paste works across different Finder windows and tabs |
| Paste conflict resolution | If a file with the same name exists: offers Keep Both, Stop, or Replace options |
| Undo paste | Cmd+Z undoes the last paste/move operation |

### 3.2 Delete

| Feature | Description |
|---------|-------------|
| Cmd+Delete | Move selected items to Trash |
| Cmd+Shift+Delete | Empty Trash (with confirmation) |
| Cmd+Option+Shift+Delete | Empty Trash (without confirmation) |
| Put Back | Right-click item in Trash > "Put Back" restores it to original location |
| Trash in Dock | Trash icon in Dock shows full/empty state; click to open Trash folder |
| 30-day auto-delete | Option to automatically remove items from Trash after 30 days |
| Secure empty trash | (Removed in recent macOS versions, replaced by APFS instant erase) |
| Delete immediately | Cmd+Option+Delete or File > Delete Immediately (bypasses Trash) |

### 3.3 Rename

| Feature | Description |
|---------|-------------|
| Return/Enter key | Initiates inline rename on selected item (selects filename without extension) |
| Click-pause-click | Click once to select, then click the filename text to begin editing |
| Tab to rename next | After committing a rename with Return, pressing Tab renames the next item |
| Escape to cancel | Cancels inline rename and restores original name |
| Extension protection | By default, warns when changing file extension; filename text is selected without extension |
| Undo rename | Cmd+Z undoes the last rename |
| Batch rename | Select multiple files > right-click > "Rename..." opens batch rename dialog |
| Batch rename modes | Three modes: Replace Text (find/replace in names), Add Text (prepend/append), Format (sequential numbering with Name and Index, Name and Counter, Name and Date) |

### 3.4 Duplicate

| Feature | Description |
|---------|-------------|
| Cmd+D | Duplicate selected files in the same folder |
| Naming convention | Duplicated file named "filename copy", then "filename copy 2", etc. |
| Duplicate folders | Recursively duplicates folder contents |

### 3.5 Create Alias (Symbolic Link)

| Feature | Description |
|---------|-------------|
| Ctrl+Cmd+A | Create an alias of the selected item in the same folder |
| Option+Cmd+drag | Drag while holding Option+Cmd to create alias at drop location |
| Alias appearance | Aliases show a small arrow overlay on their icon |
| Cmd+R / Show Original | Reveals the original file that an alias points to |
| Broken alias detection | macOS detects and indicates when an alias target has been deleted or moved |

### 3.6 Compress / Archive

| Feature | Description |
|---------|-------------|
| Right-click > Compress | Creates a .zip archive of selected files/folders |
| Multiple file compression | Select multiple items > Compress creates "Archive.zip" |
| Single file compression | Compress a single item creates "filename.zip" |
| Archive naming | Single item uses item name; multiple items use "Archive.zip" |
| Automatic extraction | Double-click .zip to extract via Archive Utility |

### 3.7 New Folder

| Feature | Description |
|---------|-------------|
| Cmd+Shift+N | Create a new folder in the current location |
| Default name | "untitled folder" with inline rename activated immediately |
| Ctrl+Cmd+N | "New Folder with Selection" -- creates a new folder and moves all selected items into it |

### 3.8 Merge Folders

| Feature | Description |
|---------|-------------|
| Option+drag | When dragging a folder onto a folder with the same name while holding Option, macOS offers a Merge option |
| Merge dialog | Offers Keep Both, Stop, Merge, or Replace options |
| Non-destructive merge | Merge combines contents without overwriting files with different names |

### 3.9 Get Info / Properties

| Feature | Description |
|---------|-------------|
| Cmd+I | Opens the Get Info panel for the selected item |
| Option+Cmd+I | Opens the Inspector (floating Get Info panel that updates as selection changes) |
| General info | Name, kind, size, location, dates (created, modified, last opened) |
| More Info section | Content-specific metadata (image dimensions, audio duration, etc.) |
| Name & Extension | Edit name and toggle "Hide extension" |
| Comments | Free-text comments field stored in file metadata |
| Open With | Choose and change the default application for opening the file |
| Preview | Thumbnail preview of the file |
| Sharing & Permissions | File ownership and permission settings (read/write/no access per user/group) |
| Tags | View and edit tags on the file |
| Locked checkbox | Prevent the file from being modified or deleted |
| Stationery Pad checkbox | (Legacy) Opens as a template creating a copy |
| Size calculation | "Calculate" button to compute folder sizes including all contents |
| Multiple item info | Select multiple items and Cmd+I shows combined size info |

---

## 4. View Modes

### 4.1 Icon View (Cmd+1)

| Feature | Description |
|---------|-------------|
| Grid layout | Files displayed as icons in a flexible grid |
| Icon size slider | Drag slider in status bar to resize icons (16x16 to 512x512) |
| Icon preview | Thumbnails show file content preview (images, PDFs, etc.) |
| Label position | Labels below icons (default) or to the right |
| Background color/image | Custom background color or image per folder |
| Grid spacing | Adjustable grid spacing via View Options |
| Snap to Grid | View > Clean Up / Clean Up By to align icons to grid |
| Arrange By | Arrange icons by name, date, size, kind, tags |
| Free positioning | Icons can be placed anywhere (not snapped to grid) |
| Stacking | Group by criteria with collapsible stacks |

### 4.2 List View (Cmd+2)

| Feature | Description |
|---------|-------------|
| Column headers | Sortable columns: Name, Date Modified, Date Created, Date Last Opened, Date Added, Size, Kind, Tags |
| Click to sort | Click column header to sort by that column; click again to reverse |
| Disclosure triangles | Expand/collapse folders inline with triangle icons |
| Option+click triangle | Expands all subfolders recursively |
| Resize columns | Drag column header edges to resize |
| Reorder columns | Drag column headers to rearrange (Name column is always first) |
| Calculate folder sizes | View Options > "Calculate all sizes" to show folder sizes in the Size column |
| Show icon preview | Toggle thumbnail previews in the icon column |
| Relative dates | "Today", "Yesterday" instead of full dates |
| Column auto-fit | Double-click column header edge to auto-fit width |

### 4.3 Column View (Cmd+3)

| Feature | Description |
|---------|-------------|
| Miller Columns | Hierarchical navigation with cascading columns |
| Preview column | Rightmost column shows file preview and metadata |
| (See Section 1 for complete Column View details) | |

### 4.4 Gallery View (Cmd+4)

| Feature | Description |
|---------|-------------|
| Large preview | Top area shows a large preview of the selected file |
| Thumbnail strip | Bottom strip shows thumbnails of all items in the current folder |
| Arrow key navigation | Left/Right arrows move through items |
| Preview pane | Right sidebar shows metadata and Quick Actions |
| Full-width preview | Preview area uses most of the window width |
| Scrubbing | Drag through thumbnail strip to quickly browse items |
| Video playback | Videos can play inline in the preview area |

### 4.5 Sort & Group Options

| Feature | Description |
|---------|-------------|
| Sort by Name | Alphabetical sorting (natural sort: "file2" before "file10") |
| Sort by Date Modified | Most/least recently modified first |
| Sort by Date Created | Most/least recently created first |
| Sort by Date Last Opened | Most/least recently opened first |
| Sort by Date Added | Most/least recently added to folder first |
| Sort by Size | Largest/smallest first |
| Sort by Kind | Grouped by file type |
| Sort by Tags | Grouped by tag color/name |
| Group By | Group items by same criteria as Sort By, creating collapsible sections |
| Sort direction toggle | Click column header to toggle ascending/descending |
| Folders on top | Finder Settings > Advanced > "Keep folders on top in windows when sorting by name" |
| Folders on top (Desktop) | Separate option: "Keep folders on top on Desktop" |

### 4.6 Show/Hide UI Elements

| Feature | Description |
|---------|-------------|
| Cmd+Shift+. (period) | Toggle hidden files/folders visibility |
| Option+Cmd+T | Show/hide toolbar |
| Option+Cmd+S | Show/hide sidebar |
| Option+Cmd+P | Show/hide path bar |
| Cmd+/ | Show/hide status bar |
| Cmd+Shift+T | Show/hide tab bar |
| Cmd+Shift+P | Show/hide preview pane |
| View > Show View Options (Cmd+J) | Open view configuration panel |
| Show item info | Toggle extra info under icons (item count for folders, image dimensions, etc.) |
| Show file extensions | Finder Settings > Advanced > "Show all filename extensions" |
| Customize Toolbar | Right-click toolbar > "Customize Toolbar..." for drag-and-drop toolbar configuration |

---

## 5. Selection

### 5.1 Selection Methods

| Feature | Description |
|---------|-------------|
| Single click | Select one item |
| Cmd+click | Toggle selection of additional items (add/remove from selection) |
| Shift+click | Range selection from last selected item to clicked item |
| Cmd+A | Select all items in the current view/column |
| Rubber band / marquee selection | Click and drag in empty space to draw a selection rectangle; items within the rectangle are selected |
| Column view marquee | In column view, click in white space to the right of filenames and drag vertically to select a range |
| Cmd+drag marquee | Hold Cmd while rubber-banding to add to existing selection |

### 5.2 Selection Behavior

| Feature | Description |
|---------|-------------|
| Cross-column restriction | In column view, selection is restricted to one column at a time |
| Selection count in status bar | Status bar shows "X of Y selected" when items are selected |
| Deselect all | Click in empty space to deselect; or Cmd+Shift+A |
| Invert selection | Not natively supported |
| Select by pattern | Not natively supported (use search instead) |

### 5.3 Spring-Loaded Folders

| Feature | Description |
|---------|-------------|
| Hover-to-open while dragging | Drag a file over a folder and pause; after a delay, the folder springs open revealing its contents |
| Configurable delay | Spring loading speed adjustable in System Settings > Accessibility > Pointer Control |
| Spacebar acceleration | While dragging, press Spacebar to immediately spring-open the hovered folder |
| Nested spring loading | Can spring through multiple folder levels while maintaining the drag |
| Auto-close on exit | Sprung-open folders close when you drag the item away |
| Works in all views | Spring loading works in Icon, List, Column, and Gallery views |
| Enabled by default | Can be disabled in System Settings > Accessibility > Pointer Control |

---

## 6. Search

### 6.1 Search Bar

| Feature | Description |
|---------|-------------|
| Cmd+F | Activates the search field in the current Finder window |
| Search scope selector | Toggle between "This Mac" and the current folder |
| Default scope | Configurable default in Finder Settings (This Mac, Current Folder, or Previous Scope) |
| Real-time results | Results update as you type |
| Results in current view mode | Search results displayed using the current view mode |
| Clear search | Click X in search field or press Escape |

### 6.2 Search Tokens / Filters

| Feature | Description |
|---------|-------------|
| "+" button for criteria | Add search criteria (Kind, Date, etc.) |
| Kind filter | Filter by document type (Application, Document, Folder, Image, Movie, Music, PDF, Presentation, etc.) |
| Date filters | Created, Modified, Last Opened with date range selectors |
| Name filter | Contains, Begins With, Ends With, Is, Is Not, Matches |
| Size filter | Greater than, Less than, Equals with size units |
| Tag filter | Filter by specific tags |
| Metadata search tokens | Natural language tokens: "kind:document", "author:tom", "kind:images created:8/16/24" |
| Boolean operators | AND (implicit between tokens), OR, NOT |
| Nested criteria | Multiple criteria can be combined for complex queries |
| File content search | Searches file contents in addition to file names (Spotlight-powered) |
| Search suggestions | Dropdown suggestions as you type |

### 6.3 Smart Folders

| Feature | Description |
|---------|-------------|
| Option+Cmd+N | Create a new Smart Folder |
| Save search as Smart Folder | After searching, click "Save" to persist the search as a Smart Folder |
| Dynamic contents | Smart Folder contents update automatically based on search criteria |
| Sidebar placement | Smart Folders can appear in the sidebar |
| Nested criteria | Complex multi-criteria saved searches |
| Scope per Smart Folder | Each Smart Folder can have its own search scope |
| Edit Smart Folder | Right-click > "Show Search Criteria" to modify |

---

## 7. Tags

### 7.1 Color Tags

| Feature | Description |
|---------|-------------|
| 7 default colors | Red, Orange, Yellow, Green, Blue, Purple, Gray |
| Color dot display | Small colored dot appears next to tagged file names |
| Multiple tags per item | A file or folder can have multiple tags simultaneously |
| Color-only tags | Tags can be just a color without a custom name |

### 7.2 Custom Tags

| Feature | Description |
|---------|-------------|
| Custom tag names | Create tags with any name (e.g., "Work", "Personal", "Urgent") |
| Custom tag colors | Assign one of the 7 available colors to any custom tag |
| Unlimited custom tags | No limit on the number of custom tags |
| Tag management | Finder Settings > Tags to create, rename, reorder, and delete tags |
| Favorite Tags | Up to 7 tags in the right-click shortcut menu |

### 7.3 Applying Tags

| Feature | Description |
|---------|-------------|
| Right-click > Tags | Apply or remove tags from context menu |
| File > Tags | Apply tags from the File menu |
| Get Info > Tags | Edit tags in the Get Info window |
| Drag to tag in sidebar | Drag a file onto a tag in the sidebar to apply that tag |
| Cmd+1 through Cmd+7 | Apply favorite tags (when configured) -- Note: these conflict with view mode shortcuts by default |
| Save dialog tags | Apply tags when saving a new file |
| Batch tagging | Select multiple items and apply tags to all at once |

### 7.4 Tag-Based Organization

| Feature | Description |
|---------|-------------|
| Tags in sidebar | Each tag appears in the sidebar; clicking shows all items with that tag |
| Tag-based search | Search by tag name or filter search results by tag |
| Tag-based Smart Folders | Create Smart Folders filtered by specific tags |
| Sort/Group by Tags | Sort or group items by their tag assignments |
| Reorder tags in sidebar | Drag tags in sidebar to reorder |
| Hide/show tags in sidebar | Finder Settings > Tags to control sidebar visibility per tag |

---

## 8. Quick Look

### 8.1 Basic Quick Look

| Feature | Description |
|---------|-------------|
| Spacebar | Press Space to preview selected file; press again to close |
| Cmd+Y | Alternative shortcut for Quick Look |
| Three-finger tap | Trackpad gesture to invoke Quick Look (Force Touch trackpads) |
| Close Quick Look | Press Space, Cmd+Y, Escape, or click outside the preview |
| Supported formats | Images, PDFs, text, HTML, Office documents, audio, video, 3D models, fonts, and more |
| Plugin extensibility | Third-party Quick Look plugins for additional formats (Markdown, source code, etc.) |

### 8.2 Quick Look Window Features

| Feature | Description |
|---------|-------------|
| Full-screen Quick Look | Click the full-screen button in Quick Look to expand |
| Option+Cmd+Y | Quick Look slideshow of selected files |
| Navigation in Quick Look | Arrow keys to move between items while Quick Look is open |
| Open With button | Opens the file in its default application from Quick Look |
| Share button | Share the file via share sheet from Quick Look |
| Multi-page navigation | PDFs and multi-page documents show page thumbnails on the side |
| Page Up / Page Down | Navigate pages in multi-page Quick Look previews |
| Zoom | Pinch to zoom on trackpad; scroll to zoom in Quick Look |
| Drag from Quick Look | Drag the file proxy icon from the Quick Look title bar |

### 8.3 Quick Look Actions

| Feature | Description |
|---------|-------------|
| Rotate image | Rotate button for image files |
| Markup | Open Markup tools for annotation (draw, text, shapes, signatures, etc.) |
| Trim audio/video | Trim controls for media files |
| Create PDF | Convert compatible files to PDF |
| Crop image | Crop tool for images in Markup |
| Remove background | Remove image background (macOS Ventura+) |
| Instant Markup save | Changes can be saved directly without opening a full editor |

---

## 9. Quick Actions

### 9.1 Built-in Quick Actions

| Feature | Description |
|---------|-------------|
| Rotate image | Rotate Left/Right for image files |
| Markup | Open annotation/markup tools |
| Create PDF | Convert images/documents to PDF |
| Convert Image | Convert to JPEG, PNG, or HEIF at different sizes |
| Trim video | Trim start/end of video clips |
| Remove Background | Remove image background (macOS Ventura+) |

### 9.2 Quick Action Access Points

| Feature | Description |
|---------|-------------|
| Preview column | Quick Action buttons appear in the Column View preview column |
| Preview pane | Quick Actions appear in the Preview pane (all view modes) |
| Context menu | Right-click > Quick Actions submenu |
| Touch Bar | Quick Actions appear on Touch Bar (MacBook Pro with Touch Bar) |

### 9.3 Custom Quick Actions

| Feature | Description |
|---------|-------------|
| Automator workflows | Create custom Quick Actions using Automator |
| Shortcuts | Create Quick Actions via the Shortcuts app |
| Finder Extensions | Third-party apps can provide Quick Actions |
| Customization | System Settings > Privacy & Security > Extensions > Finder to enable/disable |
| Customize from context menu | "Customize..." option in Quick Actions submenu opens Settings |

---

## 10. Drag and Drop

### 10.1 Basic Drag and Drop

| Feature | Description |
|---------|-------------|
| Drag to move | Drag files between locations on the same volume to move |
| Drag to copy | Drag files to a different volume to copy |
| Option+drag | Force copy (hold Option while dragging to always copy, even on same volume) |
| Cmd+drag | Force move (hold Cmd while dragging to always move, even to different volume) |
| Option+Cmd+drag | Create alias at the drop location |
| Drag feedback | Cursor changes to show operation (copy badge "+", alias badge arrow, or plain for move) |
| Multi-file drag | Drag multiple selected files simultaneously; shows count badge |
| Drag cancel | Press Escape while dragging to cancel |
| Drag to Trash | Drag items to Trash in Dock to delete |

### 10.2 Spring-Loaded Folders (Drag)

| Feature | Description |
|---------|-------------|
| Hover to spring open | Pause over a folder while dragging to open it |
| Configurable delay | Adjust spring-loading speed in Accessibility settings |
| Spacebar to accelerate | Press Spacebar while hovering to instantly open |
| Nested navigation | Spring through multiple levels of folders |
| Auto-close | Folders close when drag moves away |

### 10.3 Drag Targets

| Feature | Description |
|---------|-------------|
| Drag to sidebar | Drag files to sidebar favorites to copy/move; drag folders to add to Favorites |
| Drag to tabs | Drag files onto a tab header to move/copy to that tab's location |
| Drag between windows | Drag between separate Finder windows |
| Drag to Dock | Drag to Dock folder stacks or Trash |
| Drag to applications | Drag files onto application icons to open with that app |
| Drag to Desktop | Drop files on Desktop to move/copy there |
| Drag to toolbar | Cmd+drag files/folders/apps to toolbar to add as shortcut |

### 10.4 Proxy Icon Drag

| Feature | Description |
|---------|-------------|
| Window title proxy icon | Small icon in the window title bar representing the current folder |
| Drag proxy icon | Drag the proxy icon to copy/move the current folder reference |
| Cmd+click proxy icon | Shows the full folder hierarchy as a pop-up menu |
| Proxy icon visibility | In macOS Ventura+, hover over title to reveal; or set always visible in Accessibility settings |
| Drag to Terminal | Drag proxy icon to Terminal to insert the full path |

---

## 11. Keyboard Shortcuts

### 11.1 File Operations

| Shortcut | Action |
|----------|--------|
| Cmd+C | Copy |
| Cmd+V | Paste |
| Cmd+Option+V | Move (paste and delete from source) |
| Cmd+D | Duplicate |
| Cmd+Delete | Move to Trash |
| Cmd+Shift+Delete | Empty Trash |
| Cmd+Option+Shift+Delete | Empty Trash (no confirmation) |
| Cmd+Z | Undo |
| Cmd+Shift+Z | Redo |
| Ctrl+Cmd+A | Make Alias |
| Return | Rename selected item |
| Cmd+I | Get Info |
| Cmd+Option+I | Show Inspector (live updating Get Info) |
| Cmd+E | Eject selected volume |
| Cmd+O | Open selected item |
| Cmd+Down | Open selected item |

### 11.2 Navigation

| Shortcut | Action |
|----------|--------|
| Cmd+Up | Go to parent folder |
| Ctrl+Cmd+Up | Open parent folder in new window |
| Cmd+[ | Go back |
| Cmd+] | Go forward |
| Cmd+Shift+G | Go to Folder |
| Cmd+Shift+H | Home |
| Cmd+Shift+D | Desktop |
| Cmd+Shift+O | Documents |
| Cmd+Option+L | Downloads |
| Cmd+Shift+A | Applications |
| Cmd+Shift+U | Utilities |
| Cmd+Shift+C | Computer |
| Cmd+Shift+I | iCloud Drive |
| Cmd+Shift+K | Network |
| Cmd+Shift+R | AirDrop |
| Cmd+Shift+F | Recents |
| Cmd+K | Connect to Server |
| Cmd+R | Show Original (of alias) |

### 11.3 View Controls

| Shortcut | Action |
|----------|--------|
| Cmd+1 | Icon View |
| Cmd+2 | List View |
| Cmd+3 | Column View |
| Cmd+4 | Gallery View |
| Cmd+J | Show View Options |
| Cmd+Shift+. | Toggle hidden files |
| Cmd+Option+T | Show/hide Toolbar |
| Cmd+Option+S | Show/hide Sidebar |
| Cmd+Option+P | Show/hide Path Bar |
| Cmd+/ | Show/hide Status Bar |
| Cmd+Shift+T | Show/hide Tab Bar |
| Cmd+Shift+P | Show/hide Preview Pane |

### 11.4 Window & Tab

| Shortcut | Action |
|----------|--------|
| Cmd+N | New Finder window |
| Cmd+T | New tab |
| Cmd+W | Close window/tab |
| Cmd+Option+W | Close all Finder windows |
| Cmd+` | Cycle between Finder windows |
| Ctrl+Tab | Next tab |
| Ctrl+Shift+Tab | Previous tab |
| Cmd+Shift+N | New Folder |
| Ctrl+Cmd+N | New Folder with Selection |
| Cmd+Option+N | New Smart Folder |

### 11.5 Search & Quick Look

| Shortcut | Action |
|----------|--------|
| Cmd+F | Start Spotlight search in Finder |
| Space | Quick Look |
| Cmd+Y | Quick Look (alternative) |
| Cmd+Option+Y | Quick Look slideshow |

### 11.6 Drag Modifier Keys

| Shortcut | Action |
|----------|--------|
| Option+drag | Copy item |
| Cmd+drag | Move item |
| Cmd+Option+drag | Create alias |
| Cmd+drag to toolbar | Add item to toolbar |

### 11.7 Column/List View Specific

| Shortcut | Action |
|----------|--------|
| Right Arrow (Column) | Enter selected folder / show children |
| Left Arrow (Column) | Go to parent column |
| Right Arrow (List) | Expand selected folder disclosure |
| Left Arrow (List) | Collapse selected folder disclosure |
| Option+Right Arrow (List) | Expand folder and all subfolders |
| Up/Down Arrow | Navigate items within column/list |
| Shift+Tab (Column) | Navigate back through columns preserving breadcrumb trail |
| Tab (Column) | Navigate forward through highlighted path |

---

## 12. Context Menu (Right-Click)

### 12.1 Standard File Context Menu Items

| Menu Item | Description |
|-----------|-------------|
| Open | Open the file with its default application |
| Open With > | Submenu showing compatible applications; "Other..." for full app picker |
| Move to Trash | Delete the item (move to Trash) |
| Get Info | Open Get Info panel |
| Rename | Begin inline rename |
| Compress "filename" | Create a .zip archive |
| Duplicate | Create a copy in the same folder |
| Make Alias | Create an alias in the same folder |
| Quick Look "filename" | Open Quick Look preview |
| Copy "filename" | Copy to clipboard |
| Share > | Share via AirDrop, Mail, Messages, Notes, and other sharing extensions |
| Tags > | Apply/remove tags with color dots and tag names |
| Quick Actions > | File-type-specific actions (Rotate, Markup, Create PDF, Convert Image, etc.) plus "Customize..." |
| Services > | System services menu (varies by installed apps) |
| Show View Options | Opens View Options panel (Cmd+J equivalent) |

### 12.2 Folder Context Menu Items

| Menu Item | Description |
|-----------|-------------|
| Open | Open the folder |
| Open in New Tab | Open folder in a new Finder tab |
| Open in New Window | Open folder in a new Finder window |
| Move to Trash | Delete the folder |
| Get Info | Open Get Info panel |
| Rename | Begin inline rename |
| Compress | Create .zip archive of the folder |
| Duplicate | Duplicate the folder |
| Make Alias | Create alias to the folder |
| Copy "foldername" | Copy folder to clipboard |
| Share > | Share folder |
| Tags > | Apply/remove tags |
| New Folder | Create a new subfolder (not always present) |
| Folder Actions Setup | Configure Automator folder actions |
| Services > | System services |

### 12.3 Desktop Context Menu (Empty Area)

| Menu Item | Description |
|-----------|-------------|
| New Folder | Create a new folder on Desktop |
| Get Info | Get Info on the Desktop folder itself |
| Change Desktop Background... | Opens Desktop wallpaper settings |
| Use Stacks | Toggle desktop icon stacking |
| Sort By > | Sort desktop items |
| Clean Up | Snap icons to grid |
| Clean Up By > | Clean up by Name, Kind, Date, Size, Tags |
| Show View Options | Desktop view options |

### 12.4 Hidden Context Menu Items (Option Key)

| Menu Item | Description |
|-----------|-------------|
| Copy "filename" as Pathname | Hold Option to reveal; copies full POSIX path to clipboard |
| Show in Enclosing Folder | Appears for search results and smart folders |
| Open and Close Window | Hold Option; opens item and closes the current Finder window |
| Always Open With | Hold Option in Open With submenu to set permanent default app |

### 12.5 Multi-Selection Context Menu

| Menu Item | Description |
|-----------|-------------|
| New Folder with Selection | Creates a new folder containing all selected items |
| Rename X Items... | Opens the batch rename dialog |
| Compress X Items | Creates a single .zip of all selected items |
| All standard items | Plus the standard context menu items that apply to multiple files |

---

## 13. Integration

### 13.1 Spotlight

| Feature | Description |
|---------|-------------|
| Cmd+F in Finder | Uses Spotlight index for fast file search |
| Spotlight metadata | Searches file contents, metadata, and names |
| Instant results | Results appear as you type |
| Content indexing | Indexes document text, image metadata, audio tags, etc. |

### 13.2 Automator / Shortcuts

| Feature | Description |
|---------|-------------|
| Folder Actions | Attach Automator workflows to folders that trigger on file additions |
| Quick Actions | Custom Automator/Shortcuts workflows appear in Finder Quick Actions |
| Services menu | Automator services appear in right-click > Services |
| Shortcuts integration | macOS Shortcuts can be run from Finder via Quick Actions |

### 13.3 iCloud Drive

| Feature | Description |
|---------|-------------|
| iCloud Drive in sidebar | Direct access to iCloud Drive contents |
| Download on demand | Cloud files shown with download icon; open to download |
| Upload status indicators | Shows sync status (uploaded, downloading, waiting) |
| Optimize Mac Storage | Automatically removes locally cached files when space is low |
| Shared folders | Share iCloud Drive folders with others |
| Collaboration indicators | Shows shared folder/file status |
| Desktop & Documents sync | Option to sync Desktop and Documents folders to iCloud |

### 13.4 AirDrop

| Feature | Description |
|---------|-------------|
| Sidebar access | AirDrop in Finder sidebar |
| Drag to AirDrop | Drag files to AirDrop in sidebar or to device icons |
| Right-click > Share > AirDrop | Share via context menu |
| Proximity-based | Shows nearby compatible Apple devices |

### 13.5 Handoff & Universal Clipboard

| Feature | Description |
|---------|-------------|
| Copy on Mac, paste on iPhone | Files copied to clipboard available on nearby Apple devices |
| Handoff indicator | Dock shows Handoff-capable apps from other devices |
| Cross-device paste | Cmd+V can paste content copied on iPhone/iPad |

### 13.6 Time Machine

| Feature | Description |
|---------|-------------|
| Enter Time Machine | Browse file history through Time Machine interface |
| Version browsing | Navigate through historical snapshots of folders |
| Individual file restore | Restore specific files from backups |
| Folder-level restore | Restore entire folders from specific dates |

### 13.7 iPhone Mirroring (macOS Sequoia+)

| Feature | Description |
|---------|-------------|
| Drag files to iPhone | Drag files from Finder to iPhone Mirroring window |
| Drag files from iPhone | Drag files from iPhone Mirroring to Mac Finder |
| Cross-device file management | Seamless file transfer via drag-and-drop |

---

## 14. Accessibility

### 14.1 VoiceOver

| Feature | Description |
|---------|-------------|
| Cmd+F5 | Toggle VoiceOver |
| Full screen reader support | VoiceOver reads file names, types, sizes, and navigation context |
| Column navigation announcements | VoiceOver announces column changes, folder entry/exit |
| Item descriptions | Reads file metadata, selection state, and available actions |
| Quick Nav | Navigate Finder using arrow keys with VoiceOver |
| Rotor support | VoiceOver rotor for quick navigation between elements |
| Braille display | Refreshable braille display support for Finder navigation |

### 14.2 Full Keyboard Access

| Feature | Description |
|---------|-------------|
| Tab navigation | Tab moves focus through all UI elements (sidebar, columns, toolbar, etc.) |
| Ctrl+F1 | Toggle Full Keyboard Access |
| Ctrl+F2 | Focus menu bar |
| Ctrl+F3 | Focus Dock |
| Ctrl+F5 | Focus toolbar |
| Ctrl+F6 | Focus floating window |
| Focus indicators | Visible focus rings on all interactive elements |
| Arrow key navigation | Complete navigation without mouse in all views |

### 14.3 Other Accessibility

| Feature | Description |
|---------|-------------|
| Zoom | System-wide zoom works in Finder |
| Increased contrast | Finder respects increased contrast settings |
| Reduce motion | Finder respects reduce motion settings |
| Reduce transparency | Finder respects reduce transparency settings |
| Large cursor | Finder supports system cursor size adjustments |
| Hover text | Point at text with cursor to see larger version |
| Voice Control | Navigate and control Finder using voice commands |
| Switch Control | Finder accessible via switch input devices |

---

## 15. Status Bar

### 15.1 Status Bar Information

| Feature | Description |
|---------|-------------|
| Toggle visibility | Cmd+/ or View > Show/Hide Status Bar |
| Item count | Shows number of items in the current folder (e.g., "42 items") |
| Available disk space | Shows free space on the current volume (e.g., "123.4 GB available") |
| Selection count | When items are selected, shows "X of Y selected" |
| Icon size slider | In Icon View, shows a slider for adjusting icon size |
| Status bar position | Appears at the bottom of the Finder window |

---

## 16. Toolbar

### 16.1 Default Toolbar Items

| Feature | Description |
|---------|-------------|
| Back / Forward buttons | Navigate history; long-press for dropdown of recent locations |
| View mode buttons | Toggle between Icon, List, Column, Gallery views |
| Group / Sort button | Quick access to grouping and sorting options |
| Share button | Share selected items |
| Tags button | Apply tags to selected items |
| Action menu (gear icon) | Dropdown with common actions |
| Search field | Spotlight-powered search within current scope |
| Edit tags button | Quick tag application |
| Path control | Shows and allows navigation via path in toolbar (macOS Tahoe) |

### 16.2 Toolbar Customization

| Feature | Description |
|---------|-------------|
| Right-click > Customize Toolbar | Opens drag-and-drop toolbar customization |
| Add items | Drag items from the customization palette to the toolbar |
| Remove items | Drag items off the toolbar |
| Reorder items | Drag items to rearrange |
| Cmd+drag items | Reorder toolbar items without entering customization mode |
| Add files/folders/apps | Cmd+drag files, folders, or apps to toolbar as shortcuts |
| Flexible space | Add flexible or fixed spacers between toolbar items |
| Icon and Text / Icon Only / Text Only | Toolbar display mode options |
| Restore defaults | Reset toolbar to default configuration |

---

## 17. Finder Preferences/Settings

### 17.1 General Tab

| Feature | Description |
|---------|-------------|
| Show on Desktop | Toggle which items appear on Desktop (Hard disks, External disks, CDs/DVDs, Connected servers) |
| New Finder windows show | Choose default location for new windows (Recents, Home, Desktop, Documents, iCloud Drive, or custom) |
| Open folders in tabs | Toggle whether folders open in new tabs instead of new windows |

### 17.2 Tags Tab

| Feature | Description |
|---------|-------------|
| Tag list | View, create, delete, rename, and reorder all tags |
| Favorite Tags | Choose which tags appear in the right-click menu (up to 7) |
| Tag colors | Assign colors to tags |

### 17.3 Sidebar Tab

| Feature | Description |
|---------|-------------|
| Favorites | Toggle visibility of: Recents, AirDrop, Applications, Desktop, Documents, Downloads, Movies, Music, Pictures, Home |
| iCloud | Toggle iCloud Drive and Shared |
| Locations | Toggle: This Mac, Hard disks, External disks, CDs/DVDs, Bonjour computers, Connected servers |
| Tags | Toggle: Recent Tags |

### 17.4 Advanced Tab

| Feature | Description |
|---------|-------------|
| Show all filename extensions | Toggle global extension visibility |
| Show warning before changing extension | Toggle extension change warning |
| Show warning before removing from iCloud | Toggle iCloud deletion warning |
| Show warning before emptying Trash | Toggle Trash empty confirmation |
| Remove items from Trash after 30 days | Toggle automatic Trash cleanup |
| Keep folders on top in windows | Sort folders before files when sorting by name |
| Keep folders on top on Desktop | Same but for Desktop specifically |
| When performing a search | Default scope: Search This Mac, Search Current Folder, Use Previous Scope |

---

## 18. Window Management

### 18.1 Window Features

| Feature | Description |
|---------|-------------|
| Resize from any edge | Drag any edge or corner to resize |
| Full screen | Green button or Ctrl+Cmd+F to enter full screen |
| Split View | Drag green button to tile with another window |
| Window tiling (macOS Sequoia) | Drag window to screen edge for automatic tiling suggestions |
| Minimize | Cmd+M to minimize to Dock |
| Zoom (green button) | Click green button to maximize/restore window |
| Window position memory | Finder remembers window size and position per folder |
| Title bar double-click | Configurable: Zoom or Minimize |
| Proxy icon in title | Drag or Cmd+click for path hierarchy |
| Title bar path display | Shows current folder name in title bar |
| Window snap | Snap to screen edges and corners |

### 18.2 macOS Sequoia Window Tiling

| Feature | Description |
|---------|-------------|
| Drag to edge | Snap windows to half or quarter of screen |
| Keyboard tiling | Fn+Ctrl+Arrow keys for window tiling |
| Tile suggestions | Automatic suggestions when tiling one window |

---

## Appendix A: Column View UX Polish Details

These are subtle UX details that make Finder Column View feel refined:

| Detail | Description |
|--------|-------------|
| Smooth column animation | New columns slide in with a smooth animation when navigating deeper |
| Column separator visual | Thin gray line between columns with a subtle resize handle at the bottom |
| Selection blue highlight | Active selection uses system accent color; inactive selections use gray |
| Filename ellipsis placement | Long names show "..." in the middle (e.g., "very-long-fi...ame.txt"), preserving the extension |
| Hover tooltip for truncated names | Full filename appears in tooltip on hover |
| Folder arrow indicator | Folders show a small right-pointing triangle/chevron at the right edge indicating they have contents |
| Empty folder message | Selecting an empty folder shows "(empty)" text in the next column or just an empty column |
| Permission denied handling | Folders without access show a "not authorized" or lock icon instead of contents |
| Loading indicator | Brief loading spinner/indicator for folders with many items or network folders |
| Scroll position memory per column | Each column remembers its scroll position when navigating away and back |
| Natural sort order | "file2" sorts before "file10" (human-friendly numeric sorting) |
| Case-insensitive sorting | macOS filesystem is case-insensitive by default; sorting reflects this |
| Drag feedback in columns | Visual drop indicator (blue highlight on target folder) when dragging within columns |
| Column view remembers depth | Reopening a Finder window in column view restores the previous navigation depth |
| Accent color customization | System-wide accent color affects selection highlighting |
| Keyboard focus visual ring | Clear visual indicator of which column currently has keyboard focus |
| Preview column transition | Smooth transition when switching between file previews |
| Column divider cursor change | Cursor changes to resize cursor when hovering over the column divider |
| Right-click in empty space | Right-clicking empty space in a column provides folder-level context menu |
| Multiple selection badge | When dragging multiple items, shows a count badge on the drag image |

---

## Appendix B: Feature Categories for Span Gap Analysis

Priority categories for comparing with Span:

### P0 - Core Miller Columns (Must Have)
- Column navigation (arrow keys, click)
- Column width management
- Preview column
- Path bar / breadcrumbs
- File operations (copy, move, delete, rename)
- Selection (single, multi, range)
- Sorting
- Keyboard navigation
- Type-ahead search

### P1 - Essential File Explorer Features
- Tabs
- Sidebar with favorites
- Search (basic)
- Quick Look / file preview
- Context menu (basic operations)
- Status bar
- Toolbar
- Hidden files toggle
- Back/Forward navigation
- Go to Folder

### P2 - Advanced Features
- Tags
- Smart Folders
- Batch rename
- Quick Actions
- Drag and drop with spring-loaded folders
- Gallery View
- Icon/List views
- Advanced search with tokens/filters
- Toolbar customization
- Compress/Archive

### P3 - Integration & Platform
- iCloud/cloud storage integration
- AirDrop equivalent
- Automator/Shortcuts equivalent
- Time Machine equivalent
- Handoff / Universal Clipboard
- Full VoiceOver/accessibility
- iPhone Mirroring

---

*Sources consulted:*
- [Apple Support - Mac keyboard shortcuts](https://support.apple.com/en-us/102650)
- [MacMost - 15 Tips for Column View](https://macmost.com/15-tips-for-using-column-view-in-the-mac-finder.html)
- [Apple Support - Finder Preview Pane](https://support.apple.com/guide/mac-help/use-the-preview-pane-in-the-finder-on-mac-mchl1e4644c2/mac)
- [Apple Support - Quick Actions in Finder](https://support.apple.com/guide/mac-help/perform-quick-actions-in-the-finder-on-mac-mchl97ff9142/mac)
- [Apple Support - Finder Sidebar](https://support.apple.com/guide/mac-help/customize-the-finder-sidebar-on-mac-mchl83c9e8b8/mac)
- [Apple Support - Tags](https://support.apple.com/guide/mac-help/tag-files-and-folders-mchlp15236/mac)
- [Apple Support - Finder Folder Display](https://support.apple.com/guide/mac-help/change-folders-displayed-finder-mac-mchldaafb302/mac)
- [Apple Support - Narrow Search Results](https://support.apple.com/guide/mac-help/narrow-search-results-mh15155/mac)
- [Apple Support - Rename Files](https://support.apple.com/guide/mac-help/rename-files-folders-and-disks-on-mac-mchlp1144/mac)
- [Apple Support - macOS Sequoia Updates](https://support.apple.com/en-us/120283)
- [MacRumors - macOS Tahoe](https://www.macrumors.com/roundup/macos-26/)
- [Robservatory - Column View Navigation](https://robservatory.com/two-ways-to-navigate-column-view-folders-in-finder/)
- [Miller columns - Wikipedia](https://en.wikipedia.org/wiki/Miller_columns)
- [iDownloadBlog - File Selection](https://www.idownloadblog.com/2018/03/02/how-to-select-files-mac-finder/)
- [MacPaw - Spring Loading](https://macpaw.com/how-to/use-spring-loading-on-mac)
- [iDownloadBlog - Finder Tabs](https://www.idownloadblog.com/2023/06/01/how-to-split-merge-finder-windows-mac/)
- [OSXDaily - Proxy Icons](https://osxdaily.com/2022/07/19/how-to-always-show-window-title-proxy-icons-on-mac/)
