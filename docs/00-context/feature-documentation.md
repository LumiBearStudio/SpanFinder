# Span File Explorer - Feature & UX Documentation

> Comprehensive documentation of all implemented features, UI components, keyboard shortcuts, and UX patterns.
> Last updated: 2026-02-17

---

## Table of Contents

1. [Architecture Overview](#1-architecture-overview)
2. [ViewModels](#2-viewmodels)
3. [Services](#3-services)
4. [Views & UI Components](#4-views--ui-components)
5. [Keyboard Shortcuts](#5-keyboard-shortcuts)
6. [Mouse Interactions](#6-mouse-interactions)
7. [Drag & Drop](#7-drag--drop)
8. [Navigation Flows](#8-navigation-flows)
9. [File Operations](#9-file-operations)
10. [Settings System](#10-settings-system)
11. [Preview System](#11-preview-system)
12. [Focus Management](#12-focus-management)
13. [Localization](#13-localization)
14. [Theme & Styling](#14-theme--styling)

---

## 1. Architecture Overview

### Tech Stack
- **Framework**: WinUI 3 (Windows App SDK 1.8)
- **Language**: C# (.NET 8)
- **Pattern**: MVVM with CommunityToolkit.Mvvm
- **DI**: Microsoft.Extensions.DependencyInjection
- **Target**: net8.0-windows10.0.19041.0

### Project Structure
```
src/Span/Span/
  App.xaml(.cs)           -- DI, startup, global resources
  MainWindow.xaml(.cs)    -- Main shell (3400+ lines)
  Models/                 -- Data models (DriveItem, FolderItem, FileItem, etc.)
  ViewModels/             -- 9 ViewModels
  Views/                  -- 11 XAML views
  Services/               -- 10 services + FileOperations subsystem
  Helpers/                -- Converters, extensions
  Assets/                 -- Fonts, icons, images
```

### DI Registration (App.xaml.cs)
```
Singleton: FileSystemService, IconService, FavoritesService, PreviewService,
           ShellService, LocalizationService, ContextMenuService,
           ActionLogService, SettingsService
Transient: MainViewModel
```

---

## 2. ViewModels

### 2.1 MainViewModel
**Purpose**: Root application state -- split view, tabs, drives, favorites, file operations.

| Property | Type | Description |
|----------|------|-------------|
| LeftExplorer / RightExplorer | ExplorerViewModel | Left/right pane Miller Columns engine |
| ActivePane | ActivePane | Currently active pane (Left/Right) |
| IsSplitViewEnabled | bool | Split view toggle |
| LeftViewMode / RightViewMode | ViewMode | Per-pane view mode |
| CanUndo / CanRedo | bool | Undo/redo availability |
| StatusBarText | string | Status bar content |
| ToastMessage / IsToastVisible | string / bool | Toast notification |
| Drives | ObservableCollection | Local drives |
| NetworkDrives | ObservableCollection | Network drives |
| Favorites | ObservableCollection | User favorites |
| RecentFolders | ObservableCollection | Recent folders (max 20) |

**Key Methods**:
- `Initialize()` -- Load drives, favorites, recent folders
- `RefreshDrives()` -- Hot-plug detection response
- `ExecuteFileOperationAsync()` -- Execute file op with progress tracking
- `UndoAsync() / RedoAsync()` -- Undo/redo with history
- `SwitchViewMode()` -- Miller/Details/Icons/Home
- `TogglePreview()` -- Preview panel per pane
- `ShowToast()` -- Toast notification (default 3s)

### 2.2 ExplorerViewModel
**Purpose**: Miller Columns navigation engine -- column management, path-based navigation.

| Property | Type | Description |
|----------|------|-------------|
| Columns | ObservableCollection<FolderViewModel> | Active columns |
| PathSegments | ObservableCollection<PathSegment> | Breadcrumb segments |
| CurrentPath | string | Active path |
| CurrentFolder | FolderViewModel | Last column (current folder) |
| CurrentItems | ObservableCollection | Items for Details/Icon mode |
| EnableAutoNavigation | bool | Auto-nav on select (Miller=true, Details/Icon=false) |

**Key Methods**:
- `NavigateTo(FolderItem)` -- Full reset navigation
- `NavigateToPath(string)` -- Address bar navigation
- `NavigateToSegment(PathSegment)` -- Breadcrumb click (preserves existing columns)
- `NavigateIntoFolder(FolderViewModel)` -- Details/Icon double-click
- `NavigateUp()` -- Go to parent
- `CleanupColumnsFrom(int)` -- Remove columns from index

**Selection propagation**: `FolderVm_PropertyChanged` -- 150ms debounce, multi-select guard, sorting guard.

### 2.3 FolderViewModel
**Purpose**: Folder representation, child loading, selection management.

| Property | Type | Description |
|----------|------|-------------|
| Children | ObservableCollection | Child items |
| SelectedChild | FileSystemViewModel | Single selection |
| SelectedItems | ObservableCollection | Multi-selection |
| HasMultiSelection | bool | Multi-select active |
| IsLoading | bool | Loading indicator |
| IsActive | bool | Column has focus |
| IsSorting | bool | Sorting flag (suppresses PropertyChanged) |

**Key Methods**:
- `EnsureChildrenLoadedAsync()` -- Lazy load on first display (background thread, natural sort)
- `ReloadAsync()` -- F5 refresh
- `SyncSelectedItems(IList<object>)` -- ListView.SelectionChanged sync
- `ResetState()` -- Column removal cleanup

### 2.4 FileSystemViewModel (Base Class)
**Purpose**: Shared file/folder base -- inline rename, metadata.

| Property | Type | Description |
|----------|------|-------------|
| Name / Path | string | Item name and path (from model) |
| IsRenaming | bool | Inline rename active |
| EditableName | string | Rename text input |
| IconGlyph / IconBrush | string / Brush | Icon (virtual, overridden) |
| DateModified | string | Formatted date |
| Size | string | Formatted size (B/KB/MB/GB/TB) |
| FileType | string | File extension type |

**Inline Rename**: `BeginRename()` -> `CommitRename()` / `CancelRename()`

### 2.5 FileViewModel
Inherits FileSystemViewModel. Provides extension-based icon via IconService.

### 2.6 PreviewPanelViewModel
**Purpose**: Preview panel content and metadata management. Supports Image, Text, PDF, Media, Folder, Generic types.

**Features**: 200ms debounce, CancellationToken for in-flight loads, IDisposable.

### 2.7 FileOperationProgressViewModel
**Purpose**: Progress display for file operations (copy/move/delete).

Properties: IsVisible, OperationDescription, CurrentFile, Percentage, SpeedText, RemainingTimeText, FileCountText.

### 2.8 FileConflictDialogViewModel
**Purpose**: File conflict resolution -- Replace/Skip/KeepBoth with ApplyToAll option.

### 2.9 DetailsColumnWidths
**Purpose**: Shared column widths for Details view header-item synchronization.

---

## 3. Services

### 3.1 FileSystemService
- Async file/folder enumeration with hidden file filtering
- Parallel drive loading with 500ms timeout per drive
- Network/removable drive detection

### 3.2 SettingsService
**Storage**: ApplicationData.Current.LocalSettings

| Category | Setting | Default |
|----------|---------|---------|
| General | Language | "system" |
| General | StartupBehavior | 0 (restore) |
| Appearance | Theme | "system" |
| Appearance | Density | "comfortable" |
| Appearance | FontFamily | "Segoe UI Variable" |
| Browsing | ShowHiddenFiles | false |
| Browsing | ShowFileExtensions | true |
| Browsing | ShowCheckboxes | false |
| Browsing | MillerClickBehavior | "single" |
| Browsing | ShowThumbnails | true |
| Browsing | EnableQuickLook | true |
| Browsing | ConfirmDelete | true |
| Browsing | UndoHistorySize | 50 |
| Tools | DefaultTerminal | "wt" |
| Tools | ShowContextMenu | true |

**Event**: `SettingChanged(string key, object? value)` -- Real-time propagation.

### 3.3 IconService
- RemixIcon font loading from `icons.json`
- Extension-to-glyph/brush caching
- Default folder icon: folder-3-fill (#FFD54F)

### 3.4 FavoritesService
- Persistent favorites storage (LocalSettings)
- Default favorites: Desktop, Downloads, Documents, Pictures
- Add/remove/check favorites

### 3.5 PreviewService

| Type | Extensions |
|------|-----------|
| Image | .jpg, .jpeg, .png, .bmp, .gif, .tiff, .webp, .ico |
| Text | .txt, .cs, .json, .xml, .md, .log, .html, .css, .js, .ts, .py, .java, .cpp, .go, .rs, .sh, .bat, .ps1, .sql, .csv, .xaml, .csproj, .sln + 20 more |
| PDF | .pdf |
| Media | .mp4, .mp3, .wav, .avi, .mkv, .flac, .ogg, .aac, .m4a, .mov, .wmv, .webm + more |

Constants: MaxPreviewFileSize=100MB, MaxTextChars=50000.

### 3.6 ShellService
- `OpenWithAsync()` -- "Open with" dialog
- `ShowProperties()` -- Win32 properties dialog (P/Invoke)
- `OpenInExplorer()` -- Show in Windows Explorer
- `CopyPathToClipboard()` -- Copy path

### 3.7 ContextMenuService
Builds context menus for files, folders, drives, favorites, empty areas.

**Shell Extension Integration**: Enumerates third-party shell extensions (Bandizip, 7-Zip, VS Code, etc.), filters standard verbs (open, copy, delete).

### 3.8 ActionLogService
- JSON file storage at `%LOCALAPPDATA%/Span/action_log.json`
- Max 1000 entries, FIFO
- Thread-safe (lock-based)
- `LogOperation()`, `GetEntries()`, `Clear()`

### 3.9 LocalizationService
- Languages: en, ko, ja
- Runtime language switching via `LanguageChanged` event
- Fallback chain: current language -> English -> key name

### 3.10 ShellContextMenu (Static)
- Win32 COM-based native shell context menu
- `IShellFolder`, `IContextMenu` interfaces
- Session-based shell extension enumeration

---

## 4. Views & UI Components

### 4.1 MainWindow.xaml -- Main Application Shell

**Layout (4 rows)**:
```
Row 0: Title Bar (40px) -- App icon, title, tab area, new tab button
Row 1: Command Bar (44px) -- Navigation, address bar, file commands, sort, view, search
Row 2: Main Content (*) -- Sidebar + split view area
Row 3: Status Bar (28px) -- File count, selection info, view mode
```

**Command Bar Components**:
1. Navigation buttons (Back E72B, Forward E72A, Up E74A)
2. Address bar container (breadcrumb mode / edit mode AutoSuggestBox)
3. File commands (New Folder, Cut, Copy, Paste, Rename, Delete)
4. Sort menu (Name/Date/Size/Type, Ascending/Descending)
5. View mode menu (Miller Columns, Details, Icon sizes)
6. Split view toggle (Ctrl+Shift+E)
7. Preview panel toggle (Ctrl+Shift+P)
8. Search box (Ctrl+F)
9. Help / Settings / Log buttons

**Sidebar (200px)**:
- Home item
- Favorites list (drag-to-add support)
- Local drives list

**Main Content Area**:
```
Left Pane Container
  +-- Path Header (32px, split mode only)
  +-- Content Grid
      +-- Miller Columns (ScrollViewer with ItemsControl)
      +-- Details View
      +-- Icon View
      +-- Home View
      +-- Preview Splitter (2px)
      +-- Preview Panel
Splitter (2px)
Right Pane Container (visible when IsSplitViewEnabled)
  +-- (Same structure as Left Pane)
```

**Miller Columns Structure**:
- Each column: 220px width
- Header: 2px accent bar (IsActive binding)
- ListView: SelectionMode="Extended", CanDragItems="True"
- Item templates: Folder (icon + name + chevron) / File (icon + name)
- Inline rename: TextBox overlay toggled by IsRenaming

**Toast Overlay**: Animated notification (fade in 200ms, fade out 300ms, TranslateY).

### 4.2 App.xaml -- Theme & Global Resources

**Color Palette (Dark Theme)**:
```
Background: #202020 (Mica) -> #2d2d2d (Layer1) -> #383838 (Layer2) -> #404040 (Layer3)
Accent: #60cdff (primary), #7ed8ff (hover), #2660CDFF (dim)
Text: #ffffff (primary), #ababab (secondary), #787878 (tertiary)
Border: 6-10% translucent white
Hover: #0FFFFFFF (6%), Selected: #1460CDFF (8% accent)
```

**Dimensions**: TitleBar=40, CommandBar=44, StatusBar=28, Sidebar=200, MillerColumn=220, Preview=280.
**Corner Radius**: Sm=4, Md=6, Lg=8, Xl=12.

**Styles**: UnifiedButtonStyle (command bar), CaptionTextBlockStyle, SidebarItemStyle.

### 4.3 SettingsDialog.xaml -- Settings (780x560 min)

NavigationView (180px left) + 5 sections:

**A. General**: Language (System/EN/KO/JA), Startup behavior (Restore/Home/Folder), System tray toggle.

**B. Appearance**: Theme (System/Light/Dark), Pro themes (Midnight Gold/Cyberpunk/Nordic), Density (Compact/Comfortable/Spacious), Font (Segoe UI Variable/Cascadia Code/Consolas).

**C. Browsing**: Show hidden (Ctrl+H), Show extensions, Checkbox selection, Miller click (single/double), Thumbnails, Quick Look, Delete confirmation, Undo history (10/20/50/100).

**D. Tools**: Default terminal (WT/PowerShell/CMD), Smart Run shortcuts, Context menu integration.

**E. About**: App info (v1.0.0), Update check, Pro upgrade ($14.99), Buy me a Coffee ($3/$5/$10/$50), Links (GitHub, Bug report, Privacy).

**Search**: Keyword-based section filtering across all settings.

### 4.4 DetailsModeView.xaml -- Table View

- Header row with resizable columns (Name/Date/Type/Size)
- GridSplitter for column resizing
- Click-to-sort headers
- SelectionMode="Extended", CanDragItems="True"

### 4.5 IconModeView.xaml -- Grid View

4 icon templates:
- **Small** (16px): Horizontal, 120x22
- **Medium** (48px): Horizontal, 160x48
- **Large** (96px): Vertical, 110x120
- **Extra Large** (256px): Vertical, 240x260

GridView with ItemsWrapGrid, SelectionMode="Extended", AllowDrop="True".

### 4.6 HomeModeView.xaml -- Landing Page

3 sections:
- Devices and Drives (GridView, 220x80 cards with usage progress bar)
- Network Locations (conditional)
- Favorites (GridView, 120x80 cards)

### 4.7 PreviewPanelView.xaml -- Side Preview Panel

Content types:
- **Image**: Uniform stretch, max 400px height
- **Text**: Consolas 11px, scrollable, max 300px, text selectable
- **PDF**: First page thumbnail, max 500px
- **Media**: MediaPlayerElement with transport controls
- **Folder**: Item count display

Metadata: Type, Size, Created, Modified, Dimensions (image), Duration/Artist/Album (media).

### 4.8 LogFlyoutContent.xaml -- Action Log (400x500)
Operation log list with icon, description, timestamp, success/failure indicator.

### 4.9 HelpFlyoutContent.xaml -- Help (380x500)
3 categories of keyboard shortcuts: Navigation, Edit, View.

### 4.10 FileConflictDialog.xaml -- Conflict Resolution
Source vs destination comparison, Replace/KeepBoth/Skip options, "Apply to all" checkbox.

### 4.11 FileOperationProgressControl.xaml -- Progress
InfoBar with ProgressBar, speed, remaining time, file count, cancel button.

---

## 5. Keyboard Shortcuts

### 5.1 Global Shortcuts (Ctrl combinations)

| Shortcut | Action |
|----------|--------|
| Ctrl+L | Focus address bar (edit mode) |
| Ctrl+F | Focus search box |
| Ctrl+C | Copy selected items |
| Ctrl+X | Cut selected items |
| Ctrl+V | Paste from clipboard |
| Ctrl+A | Select all in current column |
| Ctrl+Z | Undo last operation |
| Ctrl+Y | Redo last undone operation |
| Ctrl+Shift+N | Create new folder |
| Ctrl+Shift+E | Toggle split view |
| Ctrl+Shift+P | Toggle preview panel |
| Ctrl+Tab | Switch active pane (split view) |
| Ctrl+1 | Miller Columns mode |
| Ctrl+2 | Details mode |
| Ctrl+3 | Icon mode (last used size) |

### 5.2 File Operation Shortcuts

| Shortcut | Action |
|----------|--------|
| F2 | Inline rename |
| F5 | Refresh current folder |
| Delete | Move to recycle bin |
| Shift+Delete | Permanent delete |

### 5.3 Miller Columns Navigation

| Shortcut | Action |
|----------|--------|
| Right Arrow | Enter selected folder / next column |
| Left Arrow / Backspace | Go to parent column |
| Enter | Open folder / launch file |
| Space | Quick Look preview (if enabled) |
| A-Z, 0-9 | Type-ahead search (800ms buffer) |

### 5.4 Inline Rename

| Shortcut | Action |
|----------|--------|
| Enter | Commit rename |
| Escape | Cancel rename |
| (Lost focus) | Auto-cancel |

### 5.5 Address Bar

| Shortcut | Action |
|----------|--------|
| Enter | Navigate to entered path |
| Escape | Exit edit mode, return to breadcrumb |
| (Type) | Folder autocomplete suggestions |

### 5.6 Search Box

| Shortcut | Action |
|----------|--------|
| Enter | Filter and scroll to match |
| Escape | Exit search, focus Miller Columns |

---

## 6. Mouse Interactions

### 6.1 Click Actions

| Target | Action | Result |
|--------|--------|--------|
| Folder (Miller) | Single click | Select + load next column |
| File (Miller) | Single click | Select (no column change) |
| Folder (Details/Icon) | Double click | Navigate into folder |
| File (any view) | Double click | Launch with default app |
| Breadcrumb segment | Click | Navigate to that path level |
| Sidebar drive | Click | Open drive |
| Sidebar favorite | Click | Navigate to favorite |
| Sidebar Home | Click | Switch to Home view |
| Left/Right pane | Click | Set as active pane |

### 6.2 Right-Click Context Menus

| Target | Menu Items |
|--------|-----------|
| File | Open, Open With, Cut, Copy, Delete, Rename, Copy Path, Open in Explorer, Shell Extensions, Properties |
| Folder | Open, Cut, Copy, Paste, Delete, Rename, Add/Remove Favorites, Copy Path, Open in Explorer, Shell Extensions, Properties |
| Drive | Open, Copy Path, Open in Explorer, Properties |
| Favorite | Remove, Rename |
| Empty area | New Folder, Paste, View (modes), Sort (fields + direction) |

### 6.3 Hover Effects
- Sidebar items: Background opacity change on pointer enter/exit
- Command bar buttons: UnifiedButtonStyle with PointerOver/Pressed states

---

## 7. Drag & Drop

### 7.1 Same-Pane Drag & Drop

**Folder Item Target**:
- DragOver: Validate (no self-drop, no parent-into-child), show highlight
- Drop: Shift=Move, Default=Copy
- DragLeave: Remove highlight

**Column Level**:
- DragOver: Validate source != destination folder
- Drop: Copy/Move to column's folder

### 7.2 Cross-Pane Drag & Drop

```
Left Pane -> Right Pane (or reverse)
  DragOver: Show overlay (opacity 0.05), caption "Copy"/"Move"
  Drop: Detect target pane folder, Shift=Move else Copy
  DragLeave: Hide overlay
```

### 7.3 External Drag & Drop
Files from Windows Explorer/Desktop can be dropped onto:
- Folder items -> Copy/Move into that folder
- Column area -> Copy/Move into column's folder
- Pane area -> Cross-pane processing

### 7.4 Favorites Drag & Drop
Drag any folder to Favorites sidebar area -> `AddToFavorites()`.

### 7.5 Modifier Keys
- **Shift + Drag**: Move operation (delete source)
- **Normal Drag**: Copy operation (keep source)

---

## 8. Navigation Flows

### 8.1 Miller Columns (Auto-Navigation)
```
User clicks folder
  -> FolderViewModel.SelectedChild changes
  -> ExplorerViewModel.FolderVm_PropertyChanged()
  -> 150ms debounce
  -> EnsureChildrenLoadedAsync() (background thread)
  -> Column add/replace
  -> CurrentPath + PathSegments update
  -> Recent folders tracking
```

### 8.2 Details/Icon Mode (Double-Click)
```
User double-clicks folder
  -> MainWindow calls NavigateIntoFolder()
  -> ExplorerViewModel loads children
  -> Column structure update
  -> EnableAutoNavigation=false (single click = select only)
```

### 8.3 Address Bar Navigation
```
Ctrl+L or empty area click
  -> BreadcrumbScroller hidden
  -> AddressBarAutoSuggest shown
  -> Current path as text, auto-selected

User types path:
  -> OnAddressBarTextChanged()
  -> Environment variable expansion (%APPDATA%, %USERPROFILE%, etc.)
  -> Parent directory analysis
  -> Subfolder suggestions (max 10, alphabetical)

Enter or suggestion click:
  -> Validate path
  -> Directory -> navigate
  -> File -> navigate to parent + select file
  -> Return to breadcrumb mode
```

### 8.4 Type-Ahead Search
```
User types A-Z, 0-9 in Miller Columns
  -> Character added to _typeAheadBuffer
  -> 800ms timer reset
  -> Case-insensitive prefix match on current column
  -> Match found -> SelectedChild update + ScrollIntoView
  -> Timer expires -> buffer cleared
```

### 8.5 Navigate Up
Up button or Backspace -> Extract parent path -> Navigate to parent.

---

## 9. File Operations

### 9.1 Operation Types

| Operation | Class | Undo Support |
|-----------|-------|-------------|
| Copy | CopyFileOperation | No |
| Move | MoveFileOperation | Yes (reverse move) |
| Delete | DeleteFileOperation | No (recycle bin) |
| Rename | RenameFileOperation | Yes (reverse rename) |
| New Folder | NewFolderOperation | Yes (delete if empty) |

### 9.2 FileOperationHistory
- Max 50 operations in undo stack
- `ExecuteAsync()` -> push to undo stack, clear redo
- `UndoAsync()` -> pop from undo, push to redo
- `RedoAsync()` -> pop from redo, push to undo
- `HistoryChanged` event for UI binding

### 9.3 Copy Operation Details
- 80KB buffer for file copying
- Progress reporting: bytes, speed, estimated time
- Conflict resolution: Replace, Skip, KeepBoth (auto-rename with " (n)")
- Recursive directory copying
- Cancellation support

### 9.4 Delete Operation
- Recycle bin (default) or permanent delete (Shift+Delete)
- Confirmation dialog (configurable via SettingsService.ConfirmDelete)
- Smart selection after delete (next item at same index, or last item)

### 9.5 Clipboard Operations
```
Ctrl+C: Collect selected items -> _clipboardPaths, _isCutOperation=false
Ctrl+X: Collect selected items -> _clipboardPaths, _isCutOperation=true
Ctrl+V: Paste to active column folder
  -> _isCutOperation ? MoveFileOperation : CopyFileOperation
  -> Clear clipboard on cut
```

### 9.6 Action Logging
All file operations are logged to `ActionLogService`:
- OperationType (Copy/Move/Delete/Rename/NewFolder)
- Description, SourcePaths, DestinationPath
- Success/ErrorMessage, ItemCount, Timestamp

---

## 10. Settings System

### 10.1 Architecture
```
SettingsService (Singleton)
  -> ApplicationData.Current.LocalSettings
  -> SettingChanged event
  -> Typed property accessors (get/set with defaults)

SettingsDialog (ContentDialog)
  -> LoadSettingsToUI() on open
  -> WireEvents() for live save
  -> _isLoading guard for InitializeComponent events
  -> Keyword-based search across sections

MainWindow
  -> _settings.SettingChanged += OnSettingChanged
  -> ApplyTheme() for real-time theme switching
  -> ConfirmDelete gating on HandleDelete()
```

### 10.2 Live Settings Propagation
- Theme changes: `ElementTheme` applied to root FrameworkElement
- Other settings: Read on-demand from SettingsService properties

### 10.3 Settings Dialog Sections
5 navigation sections (General, Appearance, Browsing, Tools, About) with search filtering by keywords in Korean and English.

---

## 11. Preview System

### 11.1 Preview Panel (Sidebar)
- Toggle: Ctrl+Shift+P
- Per-pane independent state
- Width: 280px default, min 200px, user-resizable
- Auto-updates on selection change (200ms debounce)
- CancellationToken for in-flight load cancellation

### 11.2 Quick Look (Space Key Popup)
- Toggle: Space key in Miller Columns (when EnableQuickLook=true)
- ContentDialog-based popup preview
- Max size: 800x600
- Content types:
  - Image: BitmapImage (max 760x500, Uniform stretch)
  - Text: Consolas font, scrollable TextBlock
  - PDF: First page render
  - Folder: Item count + metadata
  - Generic: File icon + name + size + extension + date
- Metadata footer: size, modified date
- `_isQuickLookOpen` flag prevents double-open
- Focus restored to column after close

---

## 12. Focus Management

### 12.1 Column Focus Tracking
```
GetActiveColumnIndex()
  -> FocusManager.GetFocusedElement()
  -> Compare with each Column's ListView
  -> Return matching column index

FocusColumnAsync(int columnIndex)
  -> 50ms delay (UI update wait)
  -> Select first item if none selected
  -> Find ListViewItem container
  -> Set keyboard focus
  -> EnsureColumnVisible() for auto-scroll
```

### 12.2 View Mode Focus
| Mode | Focus Target |
|------|-------------|
| Miller Columns | Last column's selected item |
| Details | Details ListView |
| Icon | Icon GridView |
| Home | Auto-handled |

### 12.3 Pane Focus
- GotFocus events on pane containers set ActivePane
- Ctrl+Tab switches between panes
- Header/empty area clicks set active pane
- PointerPressed on pane area sets active pane

### 12.4 Focus After Operations
- After rename: Focus returns to ListViewItem container (not TextBox)
- After dialog: Restore saved activeIndex
- After delete: Smart selection (next item at same index)
- `_justFinishedRename` flag prevents Enter from triggering file execution

---

## 13. Localization

### 13.1 Supported Languages
- English (en) -- default fallback
- Korean (ko) -- primary UI language
- Japanese (ja)
- System -- auto-detect from OS

### 13.2 String Categories
- Context menu items (Open, Copy, Delete, etc.)
- View modes (Miller Columns, Details, Icons)
- Sort options (Name, Date, Size, Type)
- Dialog text (delete confirmation, new folder name)
- Folder item count format

### 13.3 Runtime Switching
`LocalizationService.Language` setter fires `LanguageChanged` event. Settings dialog shows restart notice for language changes.

---

## 14. Theme & Styling

### 14.1 Theme Modes
- **System**: Follows Windows theme (ElementTheme.Default)
- **Light**: ElementTheme.Light
- **Dark**: ElementTheme.Dark (primary design target)

Live switching via `MainWindow.ApplyTheme()` on `SettingsService.SettingChanged`.

### 14.2 Color System (Dark Theme)

| Category | Token | Value |
|----------|-------|-------|
| Background | SpanBgMica | #202020 |
| Background | SpanBgLayer1 | #2d2d2d |
| Background | SpanBgLayer2 | #383838 |
| Background | SpanBgLayer3 | #404040 |
| Accent | SpanAccent | #60cdff |
| Accent | SpanAccentHover | #7ed8ff |
| Text | SpanTextPrimary | #ffffff |
| Text | SpanTextSecondary | #ababab |
| Text | SpanTextTertiary | #787878 |
| Hover | SpanBgHover | #0FFFFFFF (6%) |
| Selected | SpanBgSelected | #1460CDFF (8% accent) |

### 14.3 Typography
- Primary: Segoe UI Variable
- Monospace: Cascadia Code, Consolas
- Icons: Segoe Fluent Icons (system), RemixIcon (custom font)

### 14.4 Layout Density
- **Compact**: Reduced padding/spacing
- **Comfortable**: Default (balanced)
- **Spacious**: Increased padding/spacing

---

## Appendix A: File Structure Summary

### Models
| File | Purpose |
|------|---------|
| IFileSystemItem.cs | Base interface |
| DriveItem.cs | Drive representation |
| FolderItem.cs | Folder representation |
| FileItem.cs | File representation |
| PathSegment.cs | Breadcrumb segment |
| TabItem.cs | Tab data |
| FavoriteItem.cs | Favorite folder |
| PreviewType.cs | Preview type enum |
| ActionLogEntry.cs | Action log data |
| ViewMode.cs | View mode enum |
| ActivePane.cs | Active pane enum |

### FileOperations
| File | Purpose |
|------|---------|
| IFileOperation.cs | Operation interface |
| CopyFileOperation.cs | File/folder copy |
| MoveFileOperation.cs | File/folder move |
| DeleteFileOperation.cs | Delete (recycle/permanent) |
| RenameFileOperation.cs | Rename |
| NewFolderOperation.cs | New folder creation |
| FileOperationHistory.cs | Undo/redo stack |
| FileOperationProgress.cs | Progress data |
| OperationResult.cs | Operation result |
| ConflictResolution.cs | Conflict resolution enum |

---

## Appendix B: Feature Completion Status

| Feature | Status |
|---------|--------|
| Miller Columns navigation | Done |
| Split view (left/right panes) | Done |
| Details view mode | Done |
| Icon view mode (4 sizes) | Done |
| Home view (drives/favorites) | Done |
| Inline rename (F2) | Done |
| Clipboard operations (Ctrl+C/X/V) | Done |
| Multi-selection (Ctrl/Shift+Click) | Done |
| Undo/Redo (Ctrl+Z/Y) | Done |
| Address bar autocomplete | Done |
| Quick Look (Space key) | Done |
| Preview panel (Ctrl+Shift+P) | Done |
| Same-pane drag & drop | Done |
| Cross-pane drag & drop | Done |
| Favorites sidebar (drag-to-add) | Done |
| Context menus (shell extensions) | Done |
| Sort by Name/Date/Size/Type | Done |
| Type-ahead search | Done |
| Search box filtering (Ctrl+F) | Done |
| File operations (copy/move/delete) | Done |
| File conflict resolution dialog | Done |
| File operation progress display | Done |
| Action log | Done |
| Settings dialog (5 sections) | Done |
| Theme switching (System/Light/Dark) | Done |
| Localization (EN/KO/JA) | Done |
| Toast notifications | Done |
| Help flyout (shortcuts) | Done |
| USB hot-plug detection | Done |
| Keyboard navigation (20+ shortcuts) | Done |
| External drag & drop | Partial |
