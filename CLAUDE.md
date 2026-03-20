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
- **뷰 기능 4뷰 필수 확인**: 뷰에 관련된 기능(D&D, 리네임, 키보드 단축키, 컨텍스트 메뉴, 선택, 바인딩 등)을 수정/추가할 때는 반드시 Miller, Details, List, Icon 4개 뷰 모두에 적용되었는지 확인. 한 뷰에서만 동작하고 나머지에서 안 되는 것은 파일 탐색기로서 치명적 결함.

## Common Gotchas

1. **ListViewItem Focus**: After inline rename, focus must return to container, not TextBox, or arrow keys break.
2. **Column Index After Dialog**: Modal dialogs steal focus. Save `activeIndex` before dialog, use saved value after.
3. **Enter After Rename**: `_justFinishedRename` flag prevents rename-commit Enter from opening the file.
4. **PropertyChanged Subscription**: Always unsubscribe before removing columns to prevent memory leaks.
5. **Visual Tree Timing**: Use `DispatcherQueue.TryEnqueue` with `Low` priority when accessing containers after collection changes.
6. **WinUI 3 Title Bar**: Never mix `SetTitleBar()` with `WM_NCHITTEST` override. Use `SetRegionRects(Passthrough, rects)` only for interactive controls. Never call `SetRegionRects(Caption, ...)` manually.
7. **Korean Keyboard Shortcuts**: `Ctrl+`` (VK=192) needs ScanCode=41 fallback. `Ctrl+'` (VK=222) uses ScanCode=40.
8. **Mica Backdrop**: Windows 11 only; app gracefully degrades on Windows 10.
9. **`_isClosed` Guard**: `DispatcherQueue.TryEnqueue` callbacks after window close cause access violations. All deferred UI callbacks must check `_isClosed` before accessing window resources.
10. **`_isCleaningUp` Guard**: During `ExplorerViewModel.Cleanup()`, PropertyChanged callbacks must be suppressed. Check `_isCleaningUp` to avoid re-entrant updates on disposed objects.
11. **`IsSwitchingTab` Flag**: Tab switch triggers `SelectionChanged` on ListViews being swapped. `IsSwitchingTab` suppresses these spurious selection events to prevent navigation side effects.
12. **`FocusColumnAsync` autoSelect 파라미터**: 패인 전환(`FocusActivePane`) 시 `autoSelect: false`로 호출 필수. `true`(기본값)면 `SelectedChild==null`인 컬럼의 첫 항목을 자동 선택하여 컬럼이 연쇄 생성됨.
13. **썸네일 I/O는 Task.Run 필수**: `LoadThumbnailAsync()`는 UI 스레드에서 호출되므로, `File.Exists`/`File.ReadAllBytes` 등 디스크 I/O를 `Task.Run`으로 백그라운드 이동해야 UI 차단 방지.
14. **TwoWay TextBox + Enter 처리**: `x:Bind Mode=TwoWay`의 기본 `UpdateSourceTrigger`는 `LostFocus`. Enter 키로 값을 커밋하는 TextBox(리네임 등)는 반드시 `UpdateSourceTrigger=PropertyChanged` 명시.
15. **분할뷰 뷰 모드 분기**: `ViewModel.CurrentViewMode`는 좌측 패인 전용. 분할뷰에서는 반드시 `(IsSplitViewEnabled && ActivePane == Right) ? RightViewMode : CurrentViewMode` 패턴 사용.
16. **`handledEventsToo` 핸들러의 e.Handled 체크**: `AddHandler(handledEventsToo: true)`로 등록된 global 핸들러는 뷰의 로컬 핸들러가 이미 처리한 키를 재처리할 수 있음. `if (!e.Handled)` 가드 필수.
17. **자세히뷰 ContainerContentChanging 중 Margin 변경 금지**: `ContainerContentChanging`은 measure pass 중 발생. `Border.Margin` 변경 시 부모 Grid 레이아웃 무효화 → `COMException (0x8000FFFF)`. `Border.Width`만 변경 가능.
18. **자세히뷰 헤더-데이터 컬럼 동기화**: 헤더 Grid와 ListView ItemTemplate Grid는 독립적 `*` 계산. splitter 너비는 `RecalcCellTotalWidths()`에서 헤더 ColumnDefinition 누적 합산으로 계산하여 데이터 셀 Width에 포함. 개별 반올림하면 누적 오차 발생.
19. **Network Shortcuts ReadOnly 속성**: `%APPDATA%\Microsoft\Windows\Network Shortcuts` 하위 폴더는 ReadOnly 속성이 걸려 있어 `Directory.Delete` 실패. 삭제 전 `FileAttributes.Normal`로 속성 해제 필수.
20. **네트워크 드라이브 이름**: `DriveInfo.VolumeLabel`은 원격 볼륨 레이블(예: "WORK")을 반환. Windows 탐색기와 동일하게 표시하려면 `WNetGetConnectionW`로 UNC 경로를 가져와 공유 이름 추출.
21. **셸 확장 COM 호스팅 3중 방어**: (1) `SetThreadErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX)`로 에러 다이얼로그 억제 (try/finally 복원 필수) (2) `InvokeCommand`에 `CMIC_MASK_FLAG_NO_UI` 플래그 (3) `GetCommandString` ID > 5000 가드 (NVIDIA 등 AccessViolation 방지). `SetErrorMode`는 프로세스 전역이라 레이스 컨디션 위험 — 반드시 `SetThreadErrorMode` 사용.
23. **{Binding} 걸린 DP에 코드 직접 대입 금지**: WinUI 3에서 `border.BorderBrush = someBrush`처럼 DependencyProperty에 직접 값을 대입하면 해당 속성의 `{Binding}`이 **영구 파괴**됨. 이후 바인딩 소스가 변경되어도 UI 갱신 안 됨. 대신 ViewModel 속성을 토글(false→true)하여 바인딩 재평가 유도.
24. **DependencyObject의 {ThemeResource} 수동 갱신**: `BoolToBrushConverter`처럼 `DependencyObject`(not `FrameworkElement`)에 설정된 `{ThemeResource}`는 테마 변경 시 자동 갱신 안 됨. Visual tree에 속하지 않기 때문. 테마 변경 후 `converter.TrueBrush = newBrush`처럼 수동 갱신 필수.
25. **Sentry 캡처 쓰로틀 필수**: 모든 Sentry 전송 경로(`OnUnhandledException`, `DispatcherHelper.SafeEnqueue`)에 세션당 캡처 수 제한. `CaptureFatalException`(동기 flush)은 `OnDomainUnhandledException`(프로세스 종료)에서만 사용, `e.Handled = true`인 비치명적 예외에는 절대 사용 금지.

## UI State Machine

### Split View Lifecycle

`MainWindow.SplitPreviewManager.cs` — `ToggleSplitView()`:

1. **Enable**: Create `RightExplorer` → initialize with Tab2StartupBehavior → show right pane → `FocusActivePane()`
2. **Disable**: Save right pane state → hide right pane → dispose `RightExplorer` → clear right Miller panels → restore left focus
3. **Invariant**: Split view and inline preview are mutually exclusive. Enabling split view must call `HideInlinePreview()` + set `MillerInlinePreviewEnabled = false`.
4. **Pane focus**: `OnLeftPaneGotFocus/OnRightPaneGotFocus` + `OnLeft/RightPanePointerPressed` update `ViewModel.ActivePane`. `IsDragInProgress` blocks pane switching during drag.

### Preview Panel States

Three mutually exclusive preview modes:

| Mode | Trigger | Location | Applicable Views |
|------|---------|----------|------------------|
| Inline Preview | `Ctrl+Shift+P` | Miller column area (right of columns) | Miller only |
| Side Preview | Preview button | Separate panel beside explorer | Details/List/Icon |
| Split View | Split button | Right pane (independent explorer) | All modes |

- **Inline → Split**: Must hide inline preview first
- **Side → Split**: Side preview persists per-pane independently
- **Selection sync**: Preview subscribes to last column's `SelectedChild.PropertyChanged`

### Tab Switching

`MainViewModel.TabManagement.cs` — `SwitchToTab()`:

1. `SaveActiveTabState()` — persist current Path, ViewMode, SortField, SortDirection
2. Deactivate old tab columns (`IsActive = false`)
3. Set backing fields directly (`_leftExplorer`, `_currentViewMode`) to avoid PropertyChanged cascades
4. Activate new tab + restore split view state
5. `MainWindow`: `SwitchMillerPanel()` → `SwitchDetailsPanel/ListPanel/IconPanel()` → `UpdateViewModeVisibility()` → `SyncAddressBarControls()` → `ResubscribeLeftExplorer()` → `FocusActiveView()`
6. **Critical**: `IsSwitchingTab` flag suppresses unnecessary UI updates during transition

### View Mode Switching

`MainViewModel.ViewMode.cs` — `SwitchViewMode()`:

- Settings/ActionLog → opens as special tab (Explorer=null, singleton, excluded from session save)
- Home → saves current ViewMode to `_viewModeBeforeHome`, no Explorer disposal
- Miller/Details/List/Icon → standard switch with per-pane handling in split view
- **Restoration priority**: `_lastClosedViewMode` > `_viewModeBeforeHome` > saved preference

### Navigation State

`ExplorerViewModel`:

- `NavigateTo(FolderItem)`: Full reset — clear all columns, start fresh hierarchy
- `NavigateToPath(string)`: 4-phase parallel loading — split path segments, create columns concurrently
- `NavigateToSegment(PathSegment)`: Breadcrumb click — truncate columns to segment depth, wrapped in try-catch
- `HandleFolderSelectionAsync()`: Column N selection → remove columns after N → add column N+1 → load children (150ms debounce)
- **History**: `NavigationHistory` stack per ExplorerViewModel, `CanGoBack/CanGoForward` synced to MainViewModel

### Focus Management

`MainWindow.NavigationManager.cs`:

- `GetActiveColumnIndex()`: Walk visual tree to find focused column, fallback to `Columns.Count - 1`
- `FocusColumnAsync(int)`: DispatcherQueue Low priority, retries via TryEnqueue (not Task.Delay)
- `EnsureColumnVisible(int)`: Calculates total column widths (actual rendered, not constant) for scroll offset
- **After dialog**: Always save `activeIndex` before showing modal, use saved value after (dialog steals focus)

## Tricky Areas (Bug Hotspots)

### Grid Layout

- **Star vs Pixel confusion**: Miller column = `Star` (flexible), Preview = `Pixel` (fixed). Mixing both as `Star` causes proportional resize on window resize. See `SplitPreviewManager.cs:ApplyMillerColumnWidth()`.
- **Sizer DPI**: `GridSplitter` position must account for `XamlRoot.RasterizationScale`.

### Context Menu + CJK

- **AccessKey crash**: `MenuFlyoutItem.AccessKey` throws `E_INVALIDARG` (native, uncatchable) when set before visual tree connection. Workaround: store accelerator in `Tag`, use custom `TryInvokeAccessKey()`.
- **CJK bracket strip**: Shell menu text like "보내기(&N)" — stripping `&` leaves "보내기(N)" which may match `WindowsShellExtraTexts` filter. Only apply `ApplyCompact` defensively, never strip CJK brackets.
- **Submenu open**: WinUI 3 has no `MenuFlyoutSubItem.Open()` API. Use `Focus()` + `keybd_event(VK_RIGHT)` simulation.

### Keyboard + IME

- **ScanCode fallback required**: Korean/Japanese IME remaps `VirtualKey` for OEM keys. Always add `e.KeyStatus.ScanCode` fallback in `default` branch: backtick(41), single quote(40), comma(51).
- **Type-ahead dual path**: Latin input via `CharacterReceived`, CJK input via `TextChanged` on hidden TextBox (IME composing events don't fire `CharacterReceived`). 800ms timeout resets buffer.
- **Rename guard**: `IsRenaming` check must be first in `OnGlobalKeyDown` to prevent shortcuts during inline rename.

### Async + Native API Safety

- **StoreContext crash**: `StoreContext.GetDefault()` causes Access Violation (0xC0000005) in non-Store builds. Native crash — uncatchable by try-catch. Must check `Package.Current.SignatureKind == Store` first.
- **async void delegate trap**: `DispatcherQueue.TryEnqueue(async () => {...})` compiles as `async void`. Exceptions escape all try-catch. Always extract to `async Task` method and call `_ = Method()`.
- **async void event handlers**: All `async void` methods must have outermost try-catch. Unhandled exceptions in async void terminate the process.

### Visibility Binding Gaps

- **Toolbar separators**: When hiding toolbar buttons in split mode, adjacent `<Rectangle>` separators must share the same `Visibility` binding. Missing this causes double separator lines.
- **Split view parity**: Every visual element on the left pane must be mirrored to the right pane DataTemplate, including `Loaded/Unloaded` event handlers (e.g., floating path indicator).

### PropertyChanged Subscription Leaks

- **Column replace**: When replacing a column at index N, must `-= PropertyChanged` on old ViewModel before `+=` on new one. Missing this prevents GC of old ViewModels.
- **Explorer swap on tab switch**: `SetLeftExplorer()` must unsubscribe old explorer's `PropertyChanged` before subscribing new one.
- **Preview subscription**: `SubscribePreviewToLastColumn()` / `UnsubscribePreviewSelection()` must pair correctly when columns are added/removed.

### Title Bar Interactive Regions

- **Never**: `SetRegionRects(Caption, ...)` — overwrites system caption button (min/max/close) regions
- **Never**: Mix `SetTitleBar()` with `WM_NCHITTEST` override — IXP layer conflict
- **Always**: `SetRegionRects(Passthrough, rects)` only — for tab controls and other interactive elements
- **Always**: Use `TransformToVisual(null)` (not Content) + `RasterizationScale` for DPI-correct rects
- **When**: Recalculate on `Loaded`, `SizeChanged`, tab add/remove/reorder

## Project Documentation

`docs/` follows PDCA methodology:
- `00-context/` — Requirements and specifications
- `01-plan/features/` — Feature planning (`*.plan.md`)
- `02-design/features/` — Detailed design specs (`*.design.md`)
- `03-analysis/` — Gap analysis and verification
- `04-report/` — Completion reports

Reference documentation:
- `docs/ARCHITECTURE.md` — System architecture, DI graph, data flow diagrams
- `docs/UX-SPECS.md` — UX behavior specifications (9 sections)
- `docs/KNOWN-ISSUES.md` — Bug pattern catalog and workarounds

## Documentation Maintenance

The following 4 documents are the **authoritative reference** for all development work:

1. **`CLAUDE.md`** — Architecture overview, conventions, gotchas, UI state machine
2. **`docs/ARCHITECTURE.md`** — System architecture, DI graph, data flow diagrams
3. **`docs/UX-SPECS.md`** — UX behavior specifications
4. **`docs/KNOWN-ISSUES.md`** — Bug pattern catalog and workarounds

**Rules:**
- All new feature work and bug fixes must reference these documents for context.
- After modifying code that changes architecture, UX behavior, or bug patterns, the corresponding document(s) must be updated in the same work session.
- New bug patterns must be added to `KNOWN-ISSUES.md` with symptoms, cause, fix, and lesson.
- New services, view models, or DI registrations must be reflected in `ARCHITECTURE.md`.
- Changes to keyboard shortcuts, navigation, preview, or other UX flows must update `UX-SPECS.md`.
- New gotchas or guard flags must be added to the `Common Gotchas` section of `CLAUDE.md`.
