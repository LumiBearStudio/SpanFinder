# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Span is a high-performance Miller Columns file explorer for Windows, inspired by macOS Finder. It provides intuitive hierarchical navigation with zero-lag performance using WinUI 3 and modern Windows design principles.

## Tech Stack

- **Framework**: WinUI 3 (Windows App SDK 1.8)
- **Language**: C# (.NET 8)
- **Target**: net8.0-windows10.0.19041.0 (minimum: 10.0.17763.0)
- **Architecture**: MVVM with CommunityToolkit.Mvvm
- **DI Container**: Microsoft.Extensions.DependencyInjection
- **Platforms**: x86, x64, ARM64

## Build & Run Commands

### Build the project
```bash
dotnet build src/Span/Span/Span.csproj
```

### Run the application
```bash
dotnet run --project src/Span/Span/Span.csproj
```

### Build for specific platform
```bash
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

### Clean build artifacts
```bash
dotnet clean src/Span/Span/Span.csproj
```

### Restore NuGet packages
```bash
dotnet restore src/Span/Span/Span.csproj
```

## Architecture

### MVVM Pattern

The application follows strict MVVM separation:

- **Models** (`Models/`): Data structures representing file system items
  - `IFileSystemItem`: Base interface for all file system items
  - `DriveItem`, `FolderItem`, `FileItem`: Concrete implementations
  - `PathSegment`: Represents breadcrumb segments in address bar
  - `TabItem`: Tab navigation data

- **ViewModels** (`ViewModels/`): Business logic and UI state
  - `MainViewModel`: Root view model, manages tabs and drives
  - `ExplorerViewModel`: Orchestrates Miller Columns navigation
  - `FolderViewModel`: Represents a folder column with its children
  - `FileViewModel`: Represents a file item
  - `FileSystemViewModel`: Base class for FolderViewModel and FileViewModel

- **Services** (`Services/`): Business logic abstraction
  - `FileSystemService`: Async file system I/O operations
  - `IconService`: RemixIcon font loading and management

- **Views** (XAML): UI layer bound to ViewModels
  - `MainWindow.xaml`: Main application shell
  - `App.xaml`: Application-level resources and DI setup

### Miller Columns Engine

The core navigation is implemented through the **ExplorerViewModel**:

1. **Column Management**: `ObservableCollection<FolderViewModel> Columns`
   - Each column represents a folder in the hierarchy
   - Columns are added/removed dynamically based on selection
   - `PropertyChanged` events trigger column updates

2. **Selection Propagation**:
   - When a folder is selected in column N, it appears as column N+1
   - When a file is selected, all columns after N are removed
   - Path updates trigger breadcrumb regeneration

3. **Navigation Methods**:
   - `NavigateTo(FolderItem)`: Full reset to new folder
   - `NavigateToPath(string)`: Navigate by path string
   - `NavigateToSegment(PathSegment)`: Breadcrumb click handling

### Dependency Injection Setup

Services are registered in `App.xaml.cs::ConfigureServices()`:

```csharp
services.AddSingleton<FileSystemService>();
services.AddSingleton<IconService>();
services.AddTransient<MainViewModel>();
```

Access via `App.Current.Services.GetRequiredService<T>()`.

### Keyboard Navigation

Implemented in `MainWindow.xaml.cs` with two-layer event handling:

1. **Global shortcuts** (`OnGlobalKeyDown`):
   - `Ctrl+L`: Focus address bar
   - `Ctrl+F`: Focus search
   - `Ctrl+C/X/V`: Clipboard operations
   - `Ctrl+Shift+N`: New folder
   - `F5`: Refresh, `F2`: Rename, `Delete`: Delete

2. **Miller-specific** (`OnMillerKeyDown`):
   - `←/→`: Navigate between columns
   - `Enter`: Open folder/file
   - `Backspace`: Go back
   - Type-ahead search: typing filters items

### Focus Management

Active column tracking uses:
- `GetActiveColumnIndex()`: Finds focused column by walking visual tree
- `FocusColumnAsync(int)`: Sets keyboard focus to specific column
- `EnsureColumnVisible(int)`: Auto-scrolls to keep focused column visible

### Inline Rename

Rename flow:
1. `F2` triggers `HandleRename()` → `FileSystemViewModel.BeginRename()`
2. TextBox becomes visible via `IsRenaming` binding
3. `Enter` → `CommitRename()`, `Esc` → `CancelRename()`
4. Focus returns to ListViewItem container
5. `_justFinishedRename` flag prevents immediate file execution

### Clipboard Operations

Clipboard state maintained in `MainWindow`:
- `_clipboardPaths`: List of source paths
- `_isCutOperation`: Distinguishes cut from copy
- Paste destination: current active column's path
- Conflict resolution: auto-append " (n)" to duplicate names

### Breadcrumb Address Bar

Dual-mode UI:
- **Breadcrumb mode** (default): Clickable path segments
- **Edit mode** (`Ctrl+L` or click): Full path TextBox
- `PathSegments` auto-generated from `CurrentPath`
- `NavigateToSegment()` handles in-place column truncation

## Key Implementation Details

### Async Loading with Cancellation

`FolderViewModel.LoadChildrenAsync()`:
- Uses `CancellationTokenSource` to cancel pending loads
- Prevents race conditions when rapidly navigating
- `CancelLoading()` called on window close to clean up

### Column Replace Pattern

When navigating to subfolder:
- **Replace** existing column at index if present (no flicker)
- **Add** new column if beyond current range
- Always unsubscribe old ViewModel's PropertyChanged before replacing

### Type-Ahead Search

- Buffer accumulates characters within 800ms window
- Case-insensitive prefix matching: `Name.StartsWith(buffer)`
- Auto-scrolls matched item into view
- Timer resets buffer on timeout

### Visual Tree Helpers

Utility methods in `MainWindow.xaml.cs`:
- `FindChild<T>(DependencyObject)`: Recursive descendant search
- `IsDescendant(parent, child)`: Ancestry check for focus tracking
- `GetListViewForColumn(int)`: Resolve ItemsControl container to ListView

## Project Documentation (PDCA)

The `docs/` folder follows Plan-Do-Check-Act methodology:

- `00-context/`: Requirements and specifications
- `01-plan/features/`: Feature planning documents (*.plan.md)
- `02-design/features/`: Detailed design specs (*.design.md)
- `03-analysis/`: Gap analysis and verification (*.analysis.md)
- `04-report/`: Completion reports (*.report.md)

## Important Conventions

### File Naming
- ViewModels: `{Name}ViewModel.cs`
- Models: `{Name}Item.cs` or `{Name}.cs`
- Converters: `{Purpose}Converter.cs`
- XAML: Match code-behind class name

### Async Patterns
- Always use `async/await` for I/O operations
- Service methods return `Task<T>`, never `void`
- UI event handlers can be `async void` only

### ObservableProperty
- Use `[ObservableProperty]` attribute for simple properties
- Use `partial void On{PropertyName}Changed()` for side effects
- Prefer `NotifyPropertyChangedFor` for computed dependencies

### XAML Bindings
- ViewModels are set as DataContext
- Use `x:Bind` where possible for compile-time checking
- `Mode=OneWay` is default, use `TwoWay` for editable fields

## Common Gotchas

1. **ListViewItem Focus**: After inline rename, focus must return to container, not TextBox, or arrow keys won't work.

2. **Column Index After Dialog**: Modal dialogs steal focus. Save `activeIndex` before showing dialog, use saved value after.

3. **Enter After Rename**: Use `_justFinishedRename` flag to prevent rename commit Enter from triggering file execution.

4. **PropertyChanged Subscription**: Always unsubscribe before removing columns to prevent memory leaks.

5. **Path Comparison**: Use `StringComparison.OrdinalIgnoreCase` for all path comparisons on Windows.

6. **Visual Tree Timing**: Use `DispatcherQueue.TryEnqueue` with `Low` priority when accessing containers after collection changes.

## Development Notes

- Mica backdrop requires Windows 11; app gracefully degrades on Windows 10
- RemixIcon font loaded via `IconService.LoadAsync()` in `App.OnLaunched`
- Hidden files/folders are filtered in `FileSystemService.GetItemsAsync()`
- UnauthorizedAccessException silently handled for protected directories
