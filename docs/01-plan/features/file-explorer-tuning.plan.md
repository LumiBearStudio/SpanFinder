# File Explorer Tuning Plan

**작성일**: 2026-02-13
**작성자**: Agent Team (code-analyzer, ux-researcher, performance-expert, stability-expert)
**목표**: Span Miller Columns 파일 탐색기의 성능과 안정성 확보

---

## 1. 문제 정의

### 1.1 현재 발생하는 주요 문제

#### 문제 1: 파일 표시 실패
- **증상**: 파일이 있는 폴더인데 파일이 안 보이는 현상
- **근본 원인**: `_isLoaded` 플래그가 한 번 true로 설정되면 로딩 실패/취소 시에도 리셋되지 않음
- **발생 위치**: `FolderViewModel.cs:46` (`_isLoaded = true` 조기 설정)
- **사용자 영향**: 중대 - 폴더 내용을 볼 수 없어 탐색 불가

#### 문제 2: Stale UI 상태
- **증상**: 폴더 이동 후 기존 폴더에 하위 항목이 계속 보임
- **근본 원인**: `async void` 이벤트 핸들러의 fire-and-forget 패턴으로 여러 비동기 작업이 동시 실행
- **발생 위치**: `ExplorerViewModel.cs:177` (`FolderVm_PropertyChanged`)
- **사용자 영향**: 중간 - 잠깐 혼란스러운 UI 상태 표시

#### 문제 3: 성능 저하
- **증상**: 위/아래 빠르게 이동 시 끊기고 느려지는 현상
- **근본 원인**: 디바운싱 없이 매 선택마다 디스크 I/O 발생, 동시 Task.Run 실행
- **발생 위치**: `MainWindow.xaml.cs:821` (`OnMillerColumnSelectionChanged`)
- **사용자 영향**: 중대 - 키보드 탐색 시 체감 지연, 반응성 저하

#### 문제 4: Refresh 실패
- **증상**: 삭제 후 해당 탭이 refresh 안 되는 현상
- **근본 원인**: `RefreshCurrentFolderAsync` 미구현 + MainWindow가 ExplorerViewModel 우회하여 직접 조작
- **발생 위치**: `MainViewModel.cs:170` (TODO 상태)
- **사용자 영향**: 중대 - 파일 작업 후 UI가 실제 상태를 반영하지 않음

### 1.2 구조적 취약점

1. **async void의 동시성 미관리**: 재진입 방지 메커니즘 없음
2. **MVVM 패턴 위반**: MainWindow가 Columns 직접 조작 → 메모리 누수
3. **이중 동기화 경로**: TwoWay 바인딩 + SelectionChanged 이벤트 핸들러 중복
4. **상태 플래그 관리 미흡**: `_isLoaded`가 단방향 전환 (true만 가능)
5. **Children 컬렉션 교체 방식**: 새 컬렉션 할당으로 바인딩 끊어질 수 있음

---

## 2. 해결 방안

### 2.1 Mac Finder / Windows 탐색기 Best Practice 적용

#### Mac Finder Column View 패턴
- **Cancel-and-Replace**: 디바운싱 대신 이전 CancellationToken 취소 후 즉시 새 로딩 시작
- **Cell-level Lazy Loading**: 표시 직전에만 데이터 로드
- **컬럼 독립성**: 각 FolderViewModel이 자체 생명주기 관리

#### Windows 파일 탐색기 패턴
- **UI 가상화**: ListView의 ItemsStackPanel 활용
- **점진적 렌더링**: 제목 먼저 → 부가 정보 → 아이콘
- **컨테이너 재활용**: ListViewItem 재사용으로 GC 부담 감소

### 2.2 성능 최적화 방안

#### 선택 변경 디바운싱 (Priority: High, Impact: 5/5)

**구현**: `ExplorerViewModel.FolderVm_PropertyChanged`에 150ms 디바운스 적용

```csharp
private CancellationTokenSource? _selectionDebounce;
private const int SelectionDebounceMs = 150;

private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
    if (sender is not FolderViewModel parentFolder) return;

    // 이전 대기 중인 선택 처리 취소
    _selectionDebounce?.Cancel();
    _selectionDebounce = new CancellationTokenSource();
    var token = _selectionDebounce.Token;

    // File/null 선택 시 즉시 처리 (하위 로딩 없음)
    if (parentFolder.SelectedChild is FileViewModel or null)
    {
        HandleNonFolderSelection(parentFolder);
        return;
    }

    // Folder 선택 시 디바운스 적용
    try
    {
        await Task.Delay(SelectionDebounceMs, token);
        if (token.IsCancellationRequested) return;
        await HandleFolderSelectionAsync(parentFolder, token);
    }
    catch (TaskCanceledException) { }
}
```

**효과**: 빠른 키보드 이동 시 중간 폴더는 스킵하고 마지막 폴더만 로딩 → 디스크 I/O 90% 감소

#### 이중 SelectedChild 설정 제거 (Priority: High, Impact: 3/5)

**구현**: XAML 바인딩을 `Mode=OneWay`로 변경

```xml
<!-- MainWindow.xaml:209 -->
<ListView SelectedItem="{x:Bind SelectedChild, Mode=OneWay}" ... />
```

**효과**: 바인딩과 이벤트 핸들러 간 경합 제거, 단방향 데이터 흐름 확립

#### CancelLoading 누락 수정 (Priority: High, Impact: 2/5)

**구현**: `NavigateTo`, `RemoveColumnsFrom`에서 CancelLoading 호출

```csharp
public async void NavigateTo(FolderItem folder)
{
    foreach (var col in Columns)
    {
        col.PropertyChanged -= FolderVm_PropertyChanged;
        col.CancelLoading(); // 추가
    }
    Columns.Clear();
    // ...
}

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

**효과**: 불필요한 백그라운드 I/O 중단, 메모리 사용량 감소

### 2.3 안정성 강화 방안

#### Single Source of Truth 확립

**원칙**: ViewModel의 `SelectedChild`만이 상태를 결정, UI는 항상 이를 반영

**구현**: SelectionChanged 핸들러에 Guard 조건 추가

```csharp
private bool _isSyncingSelection = false;

private void OnMillerColumnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (_isSyncingSelection) return;

    if (sender is ListView listView && listView.DataContext is FolderViewModel folderVm)
    {
        var newSelection = listView.SelectedItem as FileSystemViewModel;
        if (ReferenceEquals(folderVm.SelectedChild, newSelection)) return;

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

#### 재진입 방지 메커니즘

**구현**: `FolderVm_PropertyChanged`에 재진입 Guard 추가

```csharp
private bool _isProcessingSelection = false;

private async void FolderVm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (_isProcessingSelection) return;

    _isProcessingSelection = true;
    try
    {
        // 기존 로직
    }
    finally
    {
        _isProcessingSelection = false;
    }
}
```

#### RefreshCurrentFolderAsync 구현

**구현**: MainViewModel에 실제 refresh 로직 추가

```csharp
private async Task RefreshCurrentFolderAsync()
{
    if (Explorer?.Columns == null || Explorer.Columns.Count == 0) return;

    var lastColumn = Explorer.Columns[^1];
    var savedName = lastColumn.SelectedChild?.Name;

    await lastColumn.ReloadAsync();

    // 이름 기준으로 이전 선택 복원
    if (savedName != null)
    {
        var restored = lastColumn.Children.FirstOrDefault(c =>
            c.Name.Equals(savedName, StringComparison.OrdinalIgnoreCase));
        lastColumn.SelectedChild = restored;
    }
}
```

#### _isLoaded 플래그 개선

**구현**: 3-state (NotLoaded, Loading, Loaded)로 변경

```csharp
private enum LoadingState { NotLoaded, Loading, Loaded }
private LoadingState _loadState = LoadingState.NotLoaded;

public async Task EnsureChildrenLoadedAsync()
{
    if (_loadState == LoadingState.Loaded) return;
    if (_loadState == LoadingState.Loading) return; // 중복 로딩 방지

    _loadState = LoadingState.Loading;
    try
    {
        // 로딩 로직
        _loadState = LoadingState.Loaded;
    }
    catch
    {
        _loadState = LoadingState.NotLoaded; // 실패 시 재시도 가능
        throw;
    }
}
```

---

## 3. 구현 계획

### 3.1 Phase 1: 긴급 버그 수정 (1-2일)

**목표**: 즉시 사용자 체감 개선, 치명적 버그 제거

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| 선택 변경 디바운싱 150ms | ExplorerViewModel.cs | 2h | 5/5 |
| 이중 설정 제거 (OneWay) | MainWindow.xaml | 30min | 3/5 |
| CancelLoading 누락 수정 | ExplorerViewModel.cs | 1h | 2/5 |
| 재진입 Guard 추가 | ExplorerViewModel.cs, MainWindow.xaml.cs | 1h | 4/5 |
| RefreshCurrentFolderAsync 구현 | MainViewModel.cs | 1.5h | 4/5 |
| SelectedChild = null (삭제 시) | MainWindow.xaml.cs (HandleDelete) | 30min | 3/5 |

**검증 방법**:
- 빠른 키보드 네비게이션 테스트 (위/아래 연속 입력)
- 삭제 후 UI 갱신 확인
- 폴더 빠르게 전환 시 stale 컬럼 확인

### 3.2 Phase 2: 성능 최적화 (2-3일)

**목표**: 렌더링 성능 향상, 메모리 사용 최적화

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| Binding → x:Bind | MainWindow.xaml | 1h | 3/5 |
| FocusColumnAsync 개선 | MainWindow.xaml.cs | 30min | 3/5 |
| Children Clear+Add 패턴 | FolderViewModel.cs | 1h | 2/5 |
| _isLoaded → 3-state | FolderViewModel.cs | 2h | 3/5 |
| DispatcherQueue 생성자 캡처 | FolderViewModel.cs | 30min | 2/5 |

**검증 방법**:
- Visual Studio Performance Profiler로 CPU, 메모리 사용량 측정
- 1000+ 항목 폴더 로딩 시간 측정
- Task Manager로 메모리 누수 확인

### 3.3 Phase 3: UI/UX 개선 (3-5일)

**목표**: 로딩 인디케이터, 에러 처리, 대용량 폴더 지원

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| IsLoading 인디케이터 | MainWindow.xaml, FolderViewModel.cs | 2h | 3/5 |
| IsAccessDenied 에러 표시 | FolderViewModel.cs, MainWindow.xaml | 1.5h | 2/5 |
| 증분 파일 로딩 (ISupportIncrementalLoading) | FolderViewModel.cs | 4h | 4/5 |
| 빈 폴더 플레이스홀더 | MainWindow.xaml | 1h | 2/5 |

**검증 방법**:
- 10,000+ 항목 폴더 테스트
- 권한 없는 폴더 접근 시 에러 메시지 확인
- 로딩 인디케이터 표시 타이밍 (200ms 지연 후)

### 3.4 Phase 4: 리팩토링 및 아키텍처 개선 (선택적)

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| NavigateTo → NavigateToAsync | ExplorerViewModel.cs | 2h | 2/5 |
| 컬럼 관리 API 캡슐화 | ExplorerViewModel.cs | 3h | 3/5 |
| 증분 새로고침 (diff 기반) | FolderViewModel.cs | 4h | 2/5 |

---

## 4. 검증 방법

### 4.1 문제별 재현 시나리오 및 테스트 방법

#### 문제 1: 파일 표시 실패

**재현 시나리오**:
1. 드라이브 선택 후 폴더 A 클릭
2. 폴더 A의 로딩 중 빠르게 폴더 B 클릭
3. 다시 폴더 A 선택 → 빈 상태 확인

**테스트 방법**:
- 자동 테스트: 100개 폴더를 순차 선택 후 역순 선택, 모든 폴더에 파일이 보이는지 확인
- 수동 테스트: C:\Windows, C:\Program Files 등 큰 폴더를 빠르게 왔다갔다 하며 내용 확인

**성공 기준**: 모든 폴더에서 파일/하위 폴더가 정상 표시

#### 문제 2: Stale UI 상태

**재현 시나리오**:
1. A > B > C 폴더 계층 탐색
2. A 컬럼에서 D 폴더 빠르게 선택
3. B, C 컬럼이 즉시 제거되는지 확인

**테스트 방법**:
- 자동 테스트: 폴더 선택 후 Columns.Count 확인
- 수동 테스트: 키보드로 빠른 탐색 시 잔상 컬럼 확인

**성공 기준**: 선택 변경 후 300ms 내 불필요한 컬럼 제거

#### 문제 3: 성능 저하

**재현 시나리오**:
1. 많은 하위 폴더가 있는 디렉토리 열기
2. 키보드 아래 방향키 꾹 누르기 (1초 이상)

**테스트 방법**:
- 성능 측정: Visual Studio Performance Profiler로 CPU 사용률 측정
- 주관적 평가: 키 입력 대비 선택 변경 지연 느낌

**성공 기준**:
- 디스크 I/O: 초당 10회 이하 (기존 대비 90% 감소)
- UI 반응성: 키 입력 후 100ms 내 선택 변경 (시각적 피드백)

#### 문제 4: Refresh 실패

**재현 시나리오**:
1. 파일 선택 후 Delete 키
2. 확인 후 삭제
3. 목록에서 파일이 사라지는지 확인

**테스트 방법**:
- 자동 테스트: 파일 삭제 후 Children.Count 감소 확인
- 수동 테스트: 다양한 파일 작업 (삭제, 복사, 붙여넣기) 후 UI 갱신 확인

**성공 기준**: 모든 파일 작업 후 즉시 UI 반영

### 4.2 성능 측정 기준

| 항목 | 현재 | 목표 | 측정 방법 |
|------|------|------|-----------|
| 빠른 선택 변경 시 디스크 I/O | 초당 20회+ | 초당 2-3회 | Performance Profiler |
| 1000항목 폴더 로딩 시간 | 측정 필요 | 500ms 이하 | Stopwatch |
| 메모리 사용량 (30분 탐색) | 측정 필요 | 증가율 10% 이하 | Task Manager |
| UI 스레드 블로킹 | 측정 필요 | 16ms 이하 (60fps) | Performance Profiler |

### 4.3 사용자 수용 기준

- [ ] 위/아래 빠르게 이동 시 체감 지연 없음
- [ ] 폴더 선택 후 150ms 내 하위 폴더 표시 시작
- [ ] 파일 삭제 후 목록 즉시 갱신
- [ ] 10,000+ 항목 폴더도 부드럽게 스크롤
- [ ] 30분 탐색 후에도 반응성 유지

---

## 5. 위험 및 대응 방안

### 5.1 기술적 위험

**위험 1: 디바운싱 delay가 너무 길어 탐색이 느리게 느껴질 수 있음**
- 대응: 100ms, 150ms, 200ms를 A/B 테스트하여 최적값 결정
- 폴백: 사용자 설정으로 delay 조정 가능하게 구현

**위험 2: x:Bind OneWay로 변경 시 예상치 못한 바인딩 이슈**
- 대응: Phase 1 완료 후 충분한 수동 테스트
- 폴백: 기존 TwoWay 바인딩 복원, 이벤트 핸들러에서만 동기화

**위험 3: 증분 로딩 구현 시 복잡도 증가**
- 대응: Phase 3로 연기, Phase 1-2만으로도 충분한 개선 효과
- 폴백: ISupportIncrementalLoading 대신 간단한 페이징

### 5.2 일정 위험

**위험: Agent Team 분석에 예상보다 많은 시간 소요**
- 현황: ✅ 분석 완료 (4시간 소요)
- 대응: Phase 1 구현에 집중, Phase 2-3은 여유 있게 진행

---

## 6. 성공 메트릭

### 6.1 정량적 메트릭

- **디스크 I/O 감소**: 90% 감소 (초당 20회 → 2-3회)
- **로딩 시간**: 1000항목 폴더 500ms 이하
- **메모리 안정성**: 30분 탐색 후 증가율 10% 이하
- **UI 반응성**: 16ms 이하 (60fps 유지)

### 6.2 정성적 메트릭

- **키보드 탐색 만족도**: Mac Finder 수준의 즉각적 반응
- **시각적 안정성**: Stale 컬럼, 깜박임 제거
- **신뢰성**: 파일 작업 후 항상 UI 갱신

### 6.3 코드 품질 메트릭

- **MVVM 준수**: MainWindow에서 ViewModel 직접 조작 제거
- **메모리 누수 제거**: PropertyChanged 구독 해제 100%
- **테스트 커버리지**: 핵심 시나리오 수동 테스트 100%

---

## 7. 참고 자료

### 분석 보고서
- code-analyzer: 코드베이스 문제점 분석 보고서
- ux-researcher: Mac Finder/Windows 탐색기 Best Practice 연구
- performance-expert: 성능 최적화 전략 설계서
- stability-expert: 상태 동기화 안정성 강화 설계서

### 외부 자료
- [Apple NSBrowser Documentation](https://developer.apple.com/documentation/appkit/nsbrowser)
- [Microsoft ListView Optimization](https://learn.microsoft.com/en-us/windows/uwp/debug-test-perf/optimize-gridview-and-listview)
- [CancellationToken Best Practices](https://devblogs.microsoft.com/premier-developer/recommended-patterns-for-cancellationtoken/)
- [Async Debounce Pattern](https://code.with-madrid.com/en/latest/async-debounce.html)

---

## 8. 다음 단계

1. ✅ **PDCA Plan 작성 완료** (현재 단계)
2. **Design 문서 작성**: 각 Phase별 상세 설계
3. **Phase 1 구현**: 긴급 버그 수정
4. **Gap Analysis**: 설계-구현 일치 검증
5. **Phase 2-3 구현**: 성능 최적화 및 UX 개선
6. **최종 Report**: 완료 보고서 작성

---

**계획 승인 후 즉시 Phase 1 구현을 시작할 수 있습니다.**
