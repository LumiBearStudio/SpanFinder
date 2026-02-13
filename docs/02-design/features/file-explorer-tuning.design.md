# File Explorer Tuning Design Document

> **Summary**: Span Miller Columns 파일 탐색기 성능 및 안정성 개선 설계
>
> **Project**: Span (WinUI 3 File Explorer)
> **Version**: 1.0.0
> **Author**: Agent Team + team-lead
> **Date**: 2026-02-13
> **Status**: Draft
> **Planning Doc**: [file-explorer-tuning.plan.md](../../01-plan/features/file-explorer-tuning.plan.md)

### Pipeline References

| Phase | Document | Status |
|-------|----------|--------|
| Phase 1 | Schema Definition | N/A (Desktop App) |
| Phase 2 | Coding Conventions | ✅ (CLAUDE.md) |
| Phase 3 | Mockup | N/A (기존 UI 개선) |
| Phase 4 | API Spec | N/A (파일 시스템 I/O) |

---

## 1. Overview

### 1.1 Design Goals

1. **성능**: 키보드 빠른 탐색 시 디스크 I/O 90% 감소, 체감 지연 제거
2. **안정성**: UI-ViewModel 상태 완벽 동기화, Stale UI 제거
3. **신뢰성**: 파일 작업 후 100% UI 갱신 보장
4. **유지보수성**: MVVM 패턴 준수, 재진입 방지 메커니즘 명확화

### 1.2 Design Principles

- **Single Source of Truth**: ViewModel만이 상태를 결정, UI는 반영만
- **Cancel-and-Replace**: 디바운싱 + 이전 작업 취소로 중간 로딩 스킵
- **Explicit State Transitions**: async void의 동시성을 Guard로 제어
- **MVVM Isolation**: View는 ViewModel을 직접 조작하지 않음

---

## 2. Architecture

### 2.1 Component Diagram (개선 후)

```
┌─────────────────────────────────────────────────────────────────┐
│                         MainWindow (View)                        │
│  - OnMillerColumnSelectionChanged (Guard 추가)                   │
│  - HandleDelete (MVVM 준수: ExplorerViewModel 위임)              │
└───────────────┬─────────────────────────────────────────────────┘
                │ DataContext
                v
┌─────────────────────────────────────────────────────────────────┐
│                  MainViewModel (Root ViewModel)                  │
│  - RefreshCurrentFolderAsync() [구현 완료]                       │
│  - ExecuteFileOperationAsync() [기존 유지]                       │
└───────────────┬─────────────────────────────────────────────────┘
                │ Explorer Property
                v
┌─────────────────────────────────────────────────────────────────┐
│                  ExplorerViewModel (Miller Engine)               │
│  - FolderVm_PropertyChanged (디바운싱 + 재진입 Guard)            │
│  - _selectionDebounce: CancellationTokenSource                  │
│  - _isProcessingSelection: bool (재진입 방지)                    │
│  - RemoveColumnsFrom (CancelLoading 호출 추가)                   │
└───────────────┬─────────────────────────────────────────────────┘
                │ Columns: ObservableCollection<FolderViewModel>
                v
┌─────────────────────────────────────────────────────────────────┐
│                     FolderViewModel (Column)                     │
│  - LoadingState (3-state enum)                                  │
│  - _cts: CancellationTokenSource (독립적 취소)                  │
│  - EnsureChildrenLoadedAsync (중복 방지 개선)                   │
│  - CancelLoading() [기존 유지]                                  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Data Flow (개선 후)

#### 선택 변경 흐름 (폴더 선택)

```
[사용자 클릭/키보드]
    ↓
[ListView SelectionChanged 이벤트]
    ↓
[OnMillerColumnSelectionChanged]
    ├─ _isSyncingSelection Guard ────► return (순환 방지)
    ├─ ReferenceEquals 체크 ────────► return (동일 선택 무시)
    └─ folderVm.SelectedChild = x
           ↓
    [OnSelectedChildChanged] (CommunityToolkit.Mvvm)
           ↓
    [PropertyChanged 이벤트 전파]
           ↓
    [FolderVm_PropertyChanged]
        ├─ _isProcessingSelection Guard ──► return (재진입 방지)
        ├─ File 또는 null? ──────────────► 즉시 처리 (디바운싱 없음)
        └─ Folder 선택
               ↓
        [_selectionDebounce.Cancel()] ──► 이전 대기 중 작업 취소
               ↓
        [Task.Delay(150ms, token)]
               ↓
        [await selectedFolder.EnsureChildrenLoadedAsync()]
               ↓
        [상태 검증] ──────────────────► Columns 변경? 선택 변경?
               ↓                              └─► return (취소)
        [RemoveColumnsFrom(nextIndex + 1)]
               ↓
        [Columns[nextIndex] = selectedFolder] (교체)
               ↓
        [CurrentPath 갱신]
               ↓
        [UI 자동 갱신] (OneWay 바인딩)
```

#### 삭제 흐름 (개선 후)

```
[Delete 키]
    ↓
[HandleDelete]
    ├─ activeIndex 저장 (다이얼로그 전!)
    ├─ selectedIndex 저장 (스마트 선택)
    └─ 확인 다이얼로그
           ↓ (확인)
    [currentColumn.SelectedChild = null] ──► Stale reference 제거
           ↓
    [ExecuteFileOperationAsync] ──────────► 삭제 실행
           ↓
    [currentColumn.ReloadAsync()]
           ↓
    [스마트 선택: Children[Math.Min(savedIndex, Count-1)]]
           ↓
    [RemoveColumnsFrom(activeIndex + 1)] ──► 하위 컬럼 제거
           ↓
    [FocusColumnAsync(activeIndex)]
```

### 2.3 Dependencies

| Component | Depends On | Purpose |
|-----------|------------|---------|
| MainWindow | MainViewModel | Root ViewModel 접근 |
| MainViewModel | ExplorerViewModel | Miller Columns 엔진 |
| ExplorerViewModel | FolderViewModel | 각 컬럼 관리 |
| FolderViewModel | FileSystemService | 파일 시스템 I/O |
| FolderViewModel | CancellationTokenSource | 비동기 작업 취소 |

---

## 3. Data Model

### 3.1 State Management (ViewModel 상태)

#### ExplorerViewModel 추가 상태

```csharp
public partial class ExplorerViewModel : ObservableObject
{
    // 기존
    public ObservableCollection<FolderViewModel> Columns { get; }
    public ObservableCollection<PathSegment> PathSegments { get; }
    [ObservableProperty] private string _currentPath = string.Empty;

    // 추가: 디바운싱
    private CancellationTokenSource? _selectionDebounce;
    private const int SelectionDebounceMs = 150;

    // 추가: 재진입 방지
    private bool _isProcessingSelection = false;
}
```

#### FolderViewModel 개선 상태

```csharp
public partial class FolderViewModel : FileSystemViewModel
{
    // 기존
    [ObservableProperty] private ObservableCollection<FileSystemViewModel> _children = new();
    [ObservableProperty] private FileSystemViewModel? _selectedChild;
    [ObservableProperty] private bool _isLoading = false;

    // 개선: bool -> enum (3-state)
    private LoadingState _loadState = LoadingState.NotLoaded;

    // 기존 유지
    private CancellationTokenSource? _cts;
    private readonly FileSystemService _fileService;
}

public enum LoadingState
{
    NotLoaded,   // 초기 상태 또는 로딩 실패 후
    Loading,     // 현재 로딩 중
    Loaded       // 로딩 완료
}
```

### 3.2 Guard Flags (동시성 제어)

| Flag | Type | Location | Purpose |
|------|------|----------|---------|
| `_isSyncingSelection` | bool | MainWindow.xaml.cs | SelectionChanged 순환 방지 |
| `_isProcessingSelection` | bool | ExplorerViewModel.cs | FolderVm_PropertyChanged 재진입 방지 |
| `_selectionDebounce` | CancellationTokenSource? | ExplorerViewModel.cs | 빠른 선택 변경 시 중간 작업 취소 |
| `_cts` | CancellationTokenSource? | FolderViewModel.cs | 개별 폴더 로딩 취소 |
| `_loadState` | LoadingState | FolderViewModel.cs | 로딩 상태 추적, 실패 시 재시도 가능 |

---

## 4. Phase별 상세 설계

### 4.1 Phase 1: 긴급 버그 수정

#### 4.1.1 선택 변경 디바운싱 (Priority: High)

**파일**: `src/Span/Span/ViewModels/ExplorerViewModel.cs`

**변경 사항**:

```csharp
// ExplorerViewModel에 추가
private CancellationTokenSource? _selectionDebounce;
private const int SelectionDebounceMs = 150;
private bool _isProcessingSelection = false;

private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
    if (sender is not FolderViewModel parentFolder) return;
    if (_isProcessingSelection) return; // 재진입 방지

    _isProcessingSelection = true;
    try
    {
        int parentIndex = Columns.IndexOf(parentFolder);
        if (parentIndex == -1) return;
        int nextIndex = parentIndex + 1;

        // File 또는 null 선택: 즉시 처리 (디바운싱 불필요)
        if (parentFolder.SelectedChild is FileViewModel fileVm)
        {
            RemoveColumnsFrom(nextIndex);
            CurrentPath = fileVm.Path;
            return;
        }

        if (parentFolder.SelectedChild == null)
        {
            RemoveColumnsFrom(nextIndex);
            CurrentPath = parentFolder.Path;
            return;
        }

        // Folder 선택: 디바운싱 적용
        if (parentFolder.SelectedChild is FolderViewModel selectedFolder)
        {
            // 이전 대기 중인 작업 취소
            _selectionDebounce?.Cancel();
            _selectionDebounce = new CancellationTokenSource();
            var token = _selectionDebounce.Token;

            try
            {
                await Task.Delay(SelectionDebounceMs, token);
                if (token.IsCancellationRequested) return;

                // await 후 상태 유효성 검증
                if (Columns.IndexOf(parentFolder) != parentIndex) return;
                if (parentFolder.SelectedChild != selectedFolder) return;

                await selectedFolder.EnsureChildrenLoadedAsync();
                if (token.IsCancellationRequested) return;

                RemoveColumnsFrom(nextIndex + 1);

                // Replace or Add
                if (nextIndex < Columns.Count)
                {
                    Columns[nextIndex].PropertyChanged -= FolderVm_PropertyChanged;
                    selectedFolder.PropertyChanged += FolderVm_PropertyChanged;
                    Columns[nextIndex] = selectedFolder;
                }
                else
                {
                    AddColumn(selectedFolder);
                }

                CurrentPath = selectedFolder.Path;
            }
            catch (TaskCanceledException) { }
        }
    }
    finally
    {
        _isProcessingSelection = false;
    }
}
```

**예상 효과**: 디스크 I/O 90% 감소, 키보드 탐색 즉각 반응

#### 4.1.2 이중 SelectedChild 설정 제거

**파일**: `src/Span/Span/MainWindow.xaml` (라인 209)

**변경 사항**:

```xml
<!-- 변경 전 -->
<ListView SelectedItem="{x:Bind SelectedChild, Mode=TwoWay}"
          SelectionChanged="OnMillerColumnSelectionChanged">

<!-- 변경 후 -->
<ListView SelectedItem="{x:Bind SelectedChild, Mode=OneWay}"
          SelectionChanged="OnMillerColumnSelectionChanged">
```

**파일**: `src/Span/Span/MainWindow.xaml.cs` (라인 821-828)

```csharp
// 순환 방지 Guard 추가
private bool _isSyncingSelection = false;

private void OnMillerColumnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_isSyncingSelection) return; // 순환 방지

    if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
    {
        var newSelection = listView.SelectedItem as FileSystemViewModel;
        if (ReferenceEquals(folderVm.SelectedChild, newSelection)) return; // 동일 선택 무시

        _isSyncingSelection = true;
        try
        {
            folderVm.SelectedChild = newSelection;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}
```

#### 4.1.3 CancelLoading 누락 수정

**파일**: `src/Span/Span/ViewModels/ExplorerViewModel.cs`

```csharp
// NavigateTo 수정
public async void NavigateTo(FolderItem folder)
{
    foreach (var col in Columns)
    {
        col.PropertyChanged -= FolderVm_PropertyChanged;
        col.CancelLoading(); // 추가
    }
    Columns.Clear();
    // ... 기존 로직
}

// RemoveColumnsFrom 수정
private void RemoveColumnsFrom(int startIndex)
{
    for (int i = Columns.Count - 1; i >= startIndex; i--)
    {
        Columns[i].PropertyChanged -= FolderVm_PropertyChanged;
        Columns[i].CancelLoading(); // 추가
        Columns.RemoveAt(i);
    }
}
```

#### 4.1.4 RefreshCurrentFolderAsync 구현

**파일**: `src/Span/Span/ViewModels/MainViewModel.cs` (라인 170-175)

**변경 사항**:

```csharp
private async Task RefreshCurrentFolderAsync()
{
    if (Explorer?.Columns == null || Explorer.Columns.Count == 0) return;

    // 마지막 활성 컬럼 갱신
    var lastColumn = Explorer.Columns[^1];
    var savedName = lastColumn.SelectedChild?.Name;

    // SelectedChild null로 설정 (Stale reference 방지)
    lastColumn.SelectedChild = null;

    await lastColumn.ReloadAsync();

    // 이전 선택 복원 (이름 기준)
    if (savedName != null)
    {
        var restored = lastColumn.Children.FirstOrDefault(c =>
            c.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase));
        lastColumn.SelectedChild = restored; // null이면 선택 해제
    }
}
```

#### 4.1.5 HandleDelete 개선 (Stale Reference 제거)

**파일**: `src/Span/Span/MainWindow.xaml.cs` (라인 653-708)

**변경 사항**:

```csharp
private async void HandleDelete()
{
    var selected = GetCurrentSelected();
    if (selected == null) return;

    // ★ 다이얼로그 전에 activeIndex 저장
    var columns = ViewModel.Explorer.Columns;
    int activeIndex = GetActiveColumnIndex();
    if (activeIndex < 0) activeIndex = columns.Count - 1;
    if (activeIndex < 0 || activeIndex >= columns.Count) return;

    var currentColumn = columns[activeIndex];
    int selectedIndex = currentColumn.Children.IndexOf(selected);

    // 확인 다이얼로그
    var dialog = new ContentDialog { /* ... */ };
    var result = await dialog.ShowAsync();
    if (result != ContentDialogResult.Primary) return;

    // ★ CRITICAL: Stale reference 제거
    currentColumn.SelectedChild = null;

    // 삭제 실행
    var operation = new DeleteFileOperation(new List<string> { selected.Path }, permanent: false);
    await ViewModel.ExecuteFileOperationAsync(operation);

    // Reload
    await currentColumn.ReloadAsync();

    // ★ 스마트 선택
    if (currentColumn.Children.Count > 0)
    {
        int newIndex = Math.Min(selectedIndex, currentColumn.Children.Count - 1);
        currentColumn.SelectedChild = currentColumn.Children[newIndex];
    }

    // 하위 컬럼 정리
    for (int i = columns.Count - 1; i > activeIndex; i--)
    {
        columns.RemoveAt(i);
    }

    FocusColumnAsync(activeIndex);
}
```

**Note**: `HandlePermanentDelete`도 동일 패턴 적용

### 4.2 Phase 2: 성능 최적화

#### 4.2.1 Binding → x:Bind 교체

**파일**: `src/Span/Span/MainWindow.xaml` (라인 206-208)

**변경 전**:
```xml
<Border BorderBrush="{Binding IsActive, Converter={StaticResource BoolToBrushConverter}}"
        BorderThickness="{Binding IsActive, Converter={StaticResource BoolToThicknessConverter}}" ...>
```

**변경 후**:
```xml
<Border BorderBrush="{x:Bind viewmodels:FolderViewModel.GetActiveBrush(IsActive), Mode=OneWay}"
        BorderThickness="{x:Bind viewmodels:FolderViewModel.GetActiveThickness(IsActive), Mode=OneWay}" ...>
```

**FolderViewModel.cs에 static 헬퍼 추가**:

```csharp
public static Brush GetActiveBrush(bool isActive)
    => isActive
        ? (Brush)Application.Current.Resources["SpanAccentBrush"]
        : (Brush)Application.Current.Resources["SpanBorderSubtleBrush"];

public static Thickness GetActiveThickness(bool isActive)
    => isActive ? new Thickness(0,2,0,0) : new Thickness(0,0,1,0);
```

#### 4.2.2 _isLoaded → LoadingState (3-state)

**파일**: `src/Span/Span/ViewModels/FolderViewModel.cs`

**변경 사항**:

```csharp
// 기존 bool _isLoaded 제거
private enum LoadingState { NotLoaded, Loading, Loaded }
private LoadingState _loadState = LoadingState.NotLoaded;

public async Task EnsureChildrenLoadedAsync()
{
    if (_loadState == LoadingState.Loaded) return;
    if (_loadState == LoadingState.Loading) return; // 중복 방지

    _loadState = LoadingState.Loading;
    IsLoading = true;

    _cts?.Cancel();
    _cts = new CancellationTokenSource();
    var token = _cts.Token;

    try
    {
        var items = await Task.Run(() => LoadItemsFromDisk(), token);

        if (!token.IsCancellationRequested)
        {
            // UI 스레드에서 Children 업데이트
            var queue = DispatcherQueue.GetForCurrentThread();
            queue?.TryEnqueue(() =>
            {
                if (!token.IsCancellationRequested)
                {
                    Children = new ObservableCollection<FileSystemViewModel>(items);
                    _loadState = LoadingState.Loaded; // 성공
                }
            });
        }
    }
    catch (TaskCanceledException)
    {
        _loadState = LoadingState.NotLoaded; // 취소 시 재시도 가능
    }
    catch (Exception)
    {
        _loadState = LoadingState.NotLoaded; // 실패 시 재시도 가능
        throw;
    }
    finally
    {
        IsLoading = false;
    }
}

public async Task ReloadAsync()
{
    _loadState = LoadingState.NotLoaded; // 리셋
    await EnsureChildrenLoadedAsync();
}
```

#### 4.2.3 FocusColumnAsync 개선

**파일**: `src/Span/Span/MainWindow.xaml.cs` (라인 810-840)

**변경 전**:
```csharp
private async void FocusColumnAsync(int columnIndex)
{
    await Task.Delay(50); // 하드코딩된 지연
    // ...
}
```

**변경 후**:
```csharp
private void FocusColumnAsync(int columnIndex)
{
    DispatcherQueue.TryEnqueue(
        Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,
        () =>
        {
            var listView = GetListViewForColumn(columnIndex);
            if (listView == null) return;
            // ... 포커스 로직
        });
}
```

### 4.3 Phase 3: UI/UX 개선

#### 4.3.1 IsLoading 인디케이터

**파일**: `src/Span/Span/MainWindow.xaml`

**추가 XAML** (ListView 내부):

```xml
<Grid>
    <!-- 기존 ListView -->
    <ListView ... />

    <!-- 로딩 오버레이 (200ms 지연 후 표시) -->
    <Grid Background="{ThemeResource SpanBgLayer1Brush}"
          Opacity="0.9"
          Visibility="{x:Bind IsLoadingWithDelay(IsLoading), Mode=OneWay}">
        <StackPanel VerticalAlignment="Center" HorizontalAlignment="Center">
            <ProgressRing IsActive="True" Width="32" Height="32"/>
            <TextBlock Text="로딩 중..." Margin="0,12,0,0"
                       FontSize="12" Foreground="{ThemeResource SpanTextSecondaryBrush}"/>
        </StackPanel>
    </Grid>
</Grid>
```

**FolderViewModel.cs에 IsLoadingWithDelay 로직 추가**:

```csharp
private DispatcherTimer? _loadingDelayTimer;

partial void OnIsLoadingChanged(bool value)
{
    if (value)
    {
        // 200ms 후에 표시
        _loadingDelayTimer = new DispatcherTimer();
        _loadingDelayTimer.Interval = TimeSpan.FromMilliseconds(200);
        _loadingDelayTimer.Tick += (s, e) =>
        {
            IsLoadingVisible = true;
            _loadingDelayTimer.Stop();
        };
        _loadingDelayTimer.Start();
    }
    else
    {
        _loadingDelayTimer?.Stop();
        IsLoadingVisible = false;
    }
}

[ObservableProperty]
private bool _isLoadingVisible = false;
```

#### 4.3.2 IsAccessDenied 에러 표시

**FolderViewModel.cs 추가**:

```csharp
[ObservableProperty]
private bool _isAccessDenied = false;

// EnsureChildrenLoadedAsync 내부
catch (UnauthorizedAccessException)
{
    IsAccessDenied = true;
    _loadState = LoadingState.NotLoaded;
}
```

**MainWindow.xaml 추가**:

```xml
<!-- Children이 비어있고 AccessDenied인 경우 -->
<TextBlock Text="&#xE72E; 접근 권한이 없습니다"
           FontFamily="{StaticResource SymbolThemeFontFamily}"
           Visibility="{x:Bind IsAccessDenied, Mode=OneWay}"
           Foreground="{ThemeResource SystemErrorTextColor}"
           HorizontalAlignment="Center" VerticalAlignment="Center"/>
```

---

## 5. Error Handling

### 5.1 Error Scenarios

| Error | Cause | Handling |
|-------|-------|----------|
| `UnauthorizedAccessException` | 권한 없는 폴더 접근 | `IsAccessDenied = true`, 에러 UI 표시 |
| `DirectoryNotFoundException` | 폴더 삭제됨 | NavigateTo 전 Directory.Exists 체크 |
| `TaskCanceledException` | 빠른 선택 변경으로 취소 | Silent catch, `_loadState = NotLoaded` |
| Stale Reference | 삭제 후 SelectedChild 참조 | 삭제 전 `SelectedChild = null` |
| UI 스레드 블로킹 | await 누락 | 모든 I/O는 Task.Run + DispatcherQueue |

### 5.2 Guard 조건 요약

```csharp
// OnMillerColumnSelectionChanged
if (_isSyncingSelection) return;
if (ReferenceEquals(folderVm.SelectedChild, newSelection)) return;

// FolderVm_PropertyChanged
if (_isProcessingSelection) return;
if (token.IsCancellationRequested) return;
if (Columns.IndexOf(parentFolder) != parentIndex) return;
if (parentFolder.SelectedChild != selectedFolder) return;

// EnsureChildrenLoadedAsync
if (_loadState == LoadingState.Loaded) return;
if (_loadState == LoadingState.Loading) return;
```

---

## 6. Test Plan

### 6.1 Test Scope

| Type | Target | Tool |
|------|--------|------|
| 성능 테스트 | 디스크 I/O, UI 반응성 | Visual Studio Performance Profiler |
| 수동 테스트 | 재현 시나리오 | 실제 탐색 |
| 메모리 테스트 | 누수, 30분 탐색 | Task Manager |
| 스트레스 테스트 | 1000+ 항목 폴더 | 대용량 폴더 준비 |

### 6.2 Test Cases (Phase 1 핵심)

#### TC1: 빠른 키보드 탐색

**시나리오**:
1. 많은 하위 폴더가 있는 디렉토리 열기
2. 키보드 아래 방향키 꾹 누르기 (1초 이상)
3. 마지막 폴더 선택 후 150ms 내 하위 폴더 표시 시작

**예상 결과**: 중간 폴더는 스킵, 마지막 폴더만 로딩, 체감 지연 없음

**측정 메트릭**:
- 디스크 I/O 횟수: 초당 2-3회 (기존 20회+)
- UI 반응 시간: 키 입력 후 100ms 내 선택 변경 (시각적 피드백)

#### TC2: 삭제 후 Refresh

**시나리오**:
1. 파일 선택
2. Delete 키 → 확인
3. 목록에서 파일 사라짐 확인
4. 스마트 선택: 동일 인덱스 또는 마지막 항목 선택됨

**예상 결과**: 즉시 UI 갱신, Stale reference 없음

#### TC3: Stale UI 제거

**시나리오**:
1. A > B > C 폴더 계층 탐색 (3개 컬럼)
2. A 컬럼에서 D 폴더 빠르게 선택
3. 150ms 내 B, C 컬럼 제거됨

**예상 결과**: 잔상 컬럼 없음, 300ms 내 정리 완료

#### TC4: 30분 탐색 메모리 안정성

**시나리오**:
1. 앱 시작 시 메모리 기록
2. 30분간 다양한 폴더 탐색 (빠른 선택, 삭제, 복사 등)
3. 종료 시 메모리 기록

**예상 결과**: 메모리 증가율 10% 이하, GC 후 감소

---

## 7. Implementation Guide

### 7.1 File Structure (수정 파일 목록)

```
src/Span/Span/
├── ViewModels/
│   ├── ExplorerViewModel.cs ─────► Phase 1 (디바운싱, 재진입 Guard)
│   ├── FolderViewModel.cs ───────► Phase 2 (LoadingState, x:Bind 헬퍼)
│   └── MainViewModel.cs ─────────► Phase 1 (RefreshCurrentFolderAsync)
├── MainWindow.xaml.cs ───────────► Phase 1 (Guard, HandleDelete)
├── MainWindow.xaml ──────────────► Phase 1 (OneWay), Phase 2 (x:Bind), Phase 3 (로딩 UI)
└── App.xaml ─────────────────────► 변경 없음
```

### 7.2 Implementation Order

**Phase 1 (긴급 - 1-2일)**:
1. [x] ExplorerViewModel 디바운싱 (2h)
2. [x] OnMillerColumnSelectionChanged Guard (1h)
3. [x] MainWindow.xaml OneWay 바인딩 (30min)
4. [x] CancelLoading 누락 수정 (1h)
5. [x] RefreshCurrentFolderAsync 구현 (1.5h)
6. [x] HandleDelete Stale reference 제거 (30min)
7. [ ] 수동 테스트 (TC1-TC4) (2h)

**Phase 2 (단기 - 2-3일)**:
1. [ ] Binding → x:Bind 교체 (1h)
2. [ ] LoadingState enum 구현 (2h)
3. [ ] FocusColumnAsync DispatcherQueue (30min)
4. [ ] Children Clear+Add 패턴 (1h)
5. [ ] 성능 측정 (Profiler) (2h)

**Phase 3 (장기 - 3-5일)**:
1. [ ] IsLoading 인디케이터 (2h)
2. [ ] IsAccessDenied 에러 표시 (1.5h)
3. [ ] 증분 로딩 (ISupportIncrementalLoading) (4h)
4. [ ] 빈 폴더 플레이스홀더 (1h)

---

## 8. Performance Metrics

### 8.1 측정 기준

| 항목 | 현재 | 목표 | 측정 도구 |
|------|------|------|-----------|
| 디스크 I/O (빠른 탐색) | 초당 20회+ | 초당 2-3회 | Performance Profiler |
| 키 입력 → 선택 반영 | 측정 필요 | 100ms 이하 | Stopwatch |
| 1000항목 폴더 로딩 | 측정 필요 | 500ms 이하 | Stopwatch |
| 메모리 증가 (30분) | 측정 필요 | 10% 이하 | Task Manager |
| UI 스레드 블로킹 | 측정 필요 | 16ms 이하 (60fps) | Performance Profiler |

### 8.2 Profiling 포인트

```csharp
// 디버그 빌드에서 측정
var sw = Stopwatch.StartNew();
await selectedFolder.EnsureChildrenLoadedAsync();
sw.Stop();
Debug.WriteLine($"[Perf] Loading took {sw.ElapsedMilliseconds}ms for {selectedFolder.Name}");
```

---

## 9. Security Considerations

- [ ] UnauthorizedAccessException 처리 (IsAccessDenied UI)
- [ ] 심볼릭 링크 순환 참조 방지 (현재 미처리, Phase 4 고려)
- [ ] 숨김 파일/시스템 파일 필터링 (현재 처리 중)

---

## 10. Version History

| Version | Date | Changes | Author |
|---------|------|---------|--------|
| 0.1 | 2026-02-13 | Initial draft (Phase 1-3 설계) | team-lead + Agent Team |

---

## Appendix A: Code References

### A.1 ExplorerViewModel.cs 현재 상태

- **Line 177-226**: `FolderVm_PropertyChanged` (디바운싱 적용 대상)
- **Line 168-175**: `RemoveColumnsFrom` (CancelLoading 추가 대상)
- **Line 66-78**: `NavigateTo` (CancelLoading 추가 대상)

### A.2 FolderViewModel.cs 현재 상태

- **Line 43-131**: `EnsureChildrenLoadedAsync` (LoadingState 적용 대상)
- **Line 144-148**: `ReloadAsync` (현재 구현 유지)
- **Line 133-139**: `CancelLoading` (현재 구현 유지)

### A.3 MainWindow.xaml.cs 현재 상태

- **Line 821-828**: `OnMillerColumnSelectionChanged` (Guard 추가 대상)
- **Line 653-708**: `HandleDelete` (Stale reference 제거 대상)
- **Line 700-703**: 직접 Columns.RemoveAt 호출 (Phase 4에서 MVVM 준수로 이동 고려)

---

## Appendix B: 디바운싱 값 튜닝 가이드

150ms는 Mac Finder 체감 반응속도 기준입니다. 사용자 피드백에 따라 조정:

| Delay | 장점 | 단점 |
|-------|------|------|
| 50ms | 매우 즉각적 반응 | 디바운싱 효과 미미 |
| 100ms | 즉각적 + 중간 로딩 스킵 | 네트워크 드라이브 시 부족 가능 |
| **150ms** | **균형 (권장)** | - |
| 200ms | 디바운싱 효과 극대화 | 약간 느린 느낌 |
| 300ms | 네트워크 드라이브 최적 | 탐색이 답답함 |

**조정 방법**: `ExplorerViewModel.cs`의 `SelectionDebounceMs` 상수 변경

---

**Design 문서 작성 완료. Phase 1 구현 준비 완료.**
