# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SPAN Finder is a high-performance Miller Columns file explorer for Windows, inspired by macOS Finder. Built with WinUI 3 (Windows App SDK 1.8), C# (.NET 8), targeting net8.0-windows10.0.19041.0 (minimum: 10.0.17763.0). Supports x86, x64, ARM64.

## Build & Test Commands

```bash
# Build (x64)
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Run unit tests
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64

# Run a single unit test
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64 --filter "FullyQualifiedName~ClassName.MethodName"

# Run UI tests (requires app running, x64 only)
dotnet test src/Span/Span.UITests/Span.UITests.csproj -p:Platform=x64

# Build MSIX for Store
build-msix.bat

# IMPORTANT: WinUI 3 apps CANNOT be launched via `dotnet run`
# Must use Visual Studio F5 (MSIX packaging required)
```

## Architecture

### MVVM with CommunityToolkit.Mvvm

- **Models** (`Models/`): `IFileSystemItem` → `DriveItem`, `FolderItem`, `FileItem`. Also `TabItem`, `PathSegment`, `ConnectionInfo`, `SearchQuery`.
- **ViewModels** (`ViewModels/`): `MainViewModel` (tabs, drives) → `ExplorerViewModel` (Miller Columns engine) → `FolderViewModel` (column with children) / `FileViewModel`. Base class: `FileSystemViewModel`.
- **Services** (`Services/`): 40+ service classes. Core: `FileSystemService`, `IconService`, `SettingsService`, `LocalizationService`. File operations in `Services/FileOperations/` (Copy, Move, Delete, Rename, Compress, Extract, BatchRename).
- **Views** (`Views/`): `DetailsModeView`, `ListModeView`, `IconModeView`, `HomeModeView`, `SettingsModeView`, `PreviewPanelView`. Custom controls in `Views/Controls/`, dialogs in `Views/Dialogs/`.
- **Helpers** (`Helpers/`): Converters, icon helpers (Remix/Phosphor/Tabler), `NaturalStringComparer`, `SearchQueryParser`, `DebugLogger`, P/Invoke in `NativeMethods.cs`.

### DI Setup

Services registered in `App.xaml.cs::ConfigureServices()`. Access via `App.Current.Services.GetRequiredService<T>()`.

### MainWindow Partial Class Structure

`MainWindow.xaml.cs` (4500+ lines) is split into 8 partial files for maintainability:
- `MainWindow.xaml.cs` — Core window logic
- `MainWindow.DragDropHandler.cs` — Drag & drop operations
- `MainWindow.FileOperationHandler.cs` — File operation handling
- `MainWindow.KeyboardHandler.cs` — Keyboard shortcuts & input
- `MainWindow.NavigationManager.cs` — Navigation logic
- `MainWindow.SettingsHandler.cs` — Settings management
- `MainWindow.SplitPreviewManager.cs` — Split view & preview panel
- `MainWindow.TabManager.cs` — Tab management

Similarly, `MainViewModel` is split into partials: `MainViewModel.cs`, `MainViewModel.FileOperations.cs`, `MainViewModel.TabManagement.cs`, `MainViewModel.ViewMode.cs`.

### Miller Columns Engine

Core navigation in `ExplorerViewModel`:

1. **Column Management**: `ObservableCollection<FolderViewModel> Columns`. Each column = a folder in the hierarchy. Columns added/removed dynamically on selection.
2. **Selection Propagation**: Folder selected in column N → column N+1 appears. File selected → columns after N removed. Path updates trigger breadcrumb regeneration.
3. **Column Replace Pattern**: Replace existing column at index (no flicker), add if beyond range. Always unsubscribe old ViewModel's PropertyChanged before replacing.
4. **Navigation**: `NavigateTo(FolderItem)` (full reset), `NavigateToPath(string)` (by path), `NavigateToSegment(PathSegment)` (breadcrumb click).

### Multi-Tab & Multi-Window

- Per-tab Miller/Details/Icon panels use Show/Hide pattern (dictionaries keyed by tab ID)
- Tab tear-off: `TabStateDto` serializes state, new `MainWindow` created with `_pendingTearOff`
- Multi-window: `App.RegisterWindow/UnregisterWindow` tracks windows, last close exits app
- Settings opens as a special tab (Explorer=null, max 1, excluded from session save)

### File System Routing

`FileSystemRouter` dispatches to the correct provider based on path:
- `LocalFileSystemProvider` — Local file system
- `FtpProvider` — FTP/FTPS (FluentFTP)
- `SftpProvider` — SFTP (SSH.NET)
- `IFileSystemProvider` — Provider interface for extensibility

### Keyboard Navigation

Two-layer event handling in `MainWindow.KeyboardHandler.cs`:
1. **Global shortcuts** (`OnGlobalKeyDown`): `Ctrl+L` (address bar), `Ctrl+F` (search), `Ctrl+C/X/V` (clipboard), `Ctrl+Shift+N` (new folder), `F5` (refresh), `F2` (rename), `Delete`
2. **Miller-specific** (`OnMillerKeyDown`): `←/→` (columns), `Enter` (open), `Backspace` (back), type-ahead search (800ms buffer, case-insensitive prefix match)

### Focus Management

- `GetActiveColumnIndex()`: Finds focused column by walking visual tree
- `FocusColumnAsync(int)`: Sets keyboard focus to specific column
- `EnsureColumnVisible(int)`: Auto-scrolls to keep focused column visible

## Testing

### Unit Tests (`Span.Tests/`)

- MSTest + Moq. Source files linked directly from main project (avoids WinUI module initializer).
- Files referencing WinUI types are excluded. `IconService` provided by `Stubs/IconServiceStub.cs`.
- Test structure mirrors main project: `Models/`, `ViewModels/`, `Services/`, `Integration/`, `Stress/`, `Helpers/`.

### UI Tests (`Span.UITests/`)

- FlaUI.UIA3 for UI automation. x64 only. Requires the app to be running.
- `SpanAppFixture.cs` manages app lifecycle for tests.
- 24 test classes covering navigation, keyboard shortcuts, file operations, split view, tabs, etc.
- Helper methods: `FindById()`, `WaitForElement()`, `FindByIdOrThrow()` (in fixture).

## Key Implementation Patterns

### Async Loading with Cancellation

`FolderViewModel.LoadChildrenAsync()` uses `CancellationTokenSource` to cancel pending loads. Prevents race conditions during rapid navigation. `CancelLoading()` called on window close.

### Inline Rename Flow

1. `F2` → `HandleRename()` → `FileSystemViewModel.BeginRename()`
2. TextBox visible via `IsRenaming` binding
3. `Enter` → `CommitRename()`, `Esc` → `CancelRename()`
4. Focus returns to ListViewItem container
5. `_justFinishedRename` flag prevents immediate file execution

### Breadcrumb Address Bar

Dual-mode: breadcrumb (default) with clickable `PathSegments` / edit mode (`Ctrl+L`). `NavigateToSegment()` handles in-place column truncation.

### Visual Tree Helpers

In `MainWindow.xaml.cs`: `FindChild<T>()` (recursive descendant search), `IsDescendant()` (ancestry check), `GetListViewForColumn(int)` (resolve container to ListView).

## Conventions

- ViewModels: `{Name}ViewModel.cs` / Models: `{Name}Item.cs` / Converters: `{Purpose}Converter.cs`
- Use `[ObservableProperty]` + `partial void On{Name}Changed()` for side effects
- Use `x:Bind` (compile-time) over `Binding`. `Mode=OneWay` default, `TwoWay` for editable fields.
- Service methods return `Task<T>`, never `void`. Only UI event handlers can be `async void`.
- Path comparison: always `StringComparison.OrdinalIgnoreCase`
- Event handlers: `-= before +=` pattern to prevent accumulation

## Common Gotchas

1. **ListViewItem Focus**: After inline rename, focus must return to container, not TextBox, or arrow keys break.
2. **Column Index After Dialog**: Modal dialogs steal focus. Save `activeIndex` before dialog, use saved value after.
3. **Enter After Rename**: `_justFinishedRename` flag prevents rename-commit Enter from opening the file.
4. **PropertyChanged Subscription**: Always unsubscribe before removing columns to prevent memory leaks.
5. **Visual Tree Timing**: Use `DispatcherQueue.TryEnqueue` with `Low` priority when accessing containers after collection changes.
6. **WinUI 3 Title Bar**: Never mix `SetTitleBar()` with `WM_NCHITTEST` override. Use `SetRegionRects(Passthrough, rects)` only for interactive controls. Never call `SetRegionRects(Caption, ...)` manually.
7. **Korean Keyboard Shortcuts**: `Ctrl+`` (VK=192) needs ScanCode=41 fallback. `Ctrl+'` (VK=222) uses ScanCode=40.
8. **Mica Backdrop**: Windows 11 only; app gracefully degrades on Windows 10.

## Project Documentation

`docs/` follows PDCA methodology:
- `00-context/` — Requirements and specifications
- `01-plan/features/` — Feature planning (`*.plan.md`)
- `02-design/features/` — Detailed design specs (`*.design.md`)
- `03-analysis/` — Gap analysis and verification
- `04-report/` — Completion reports
