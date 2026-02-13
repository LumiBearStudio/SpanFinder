# Plan: 미구현 기능 검토 및 우선순위 (Unimplemented Features Review)

## 1. 현재 구현 상태 분석

### ✅ 완료된 기능 (PDCA 문서 기준)
1. **initial-setup** - WinUI 3 프로젝트 기반 구축
2. **main-shell** - MainWindow 레이아웃, 탭 구조
3. **miller-engine** - Miller Columns 네비게이션 엔진 (90% 완료)
4. **ui-refinement-fluent** - Fluent Design 적용, 아이콘 시스템 (100% 완료)

### ✅ 현재 구현된 세부 기능
- Miller Columns 기본 구조 (ExplorerViewModel, FolderViewModel, FileViewModel)
- 키보드 네비게이션 (←/→/Enter/Backspace)
- 타입-어헤드 검색 (Type-ahead Search)
- 인라인 이름 변경 (F2)
- 클립보드 작업 (Ctrl+C/X/V)
- 새 폴더 생성 (Ctrl+Shift+N)
- 파일/폴더 삭제 (Delete) - 단, 영구 삭제만 지원
- 새로고침 (F5)
- 브레드크럼 주소 표시줄 (Breadcrumb Address Bar)
- 주소창 직접 입력 (Ctrl+L)
- Mica 백드롭 (Windows 11)
- 드라이브 사이드바
- 탭 기본 구조 (TabView)
- RemixIcon 폰트 시스템

## 2. 미구현 기능 전체 목록 (requirements.md 기준)

### 🔴 Priority 1: Phase 3 - File Operations & Safety (긴급 필수)

#### 2.1 Undo/Redo 시스템 ⚠️ **가장 중요!**
**Status**: ❌ 미구현
**Requirements**:
- Ctrl+Z / Ctrl+Y 단축키
- 삭제, 이동, 이름 바꾸기 등 파일 조작에 대한 되돌리기/다시 실행
- 내부 작업 히스토리 스택 관리 (최근 50개)
- 상태바에 "이동 완료 — Ctrl+Z로 되돌리기" 토스트 표시

**현재 문제점**:
- 파일 삭제 시 복구 불가능 (휴지통 미지원)
- 파일 이동/복사 실수 시 되돌릴 방법 없음
- 사용자가 실수로 데이터를 잃을 위험 높음

**구현 필요 사항**:
1. `IFileOperation` 인터페이스 설계
2. `FileOperationHistory` 스택 관리 클래스
3. 각 파일 작업(삭제, 이동, 복사, 이름 변경)에 대한 `IFileOperation` 구현
4. Undo/Redo 명령어 및 키보드 단축키
5. 상태바 토스트 알림

#### 2.2 휴지통 vs 영구삭제
**Status**: ⚠️ 부분 구현 (영구 삭제만 가능)
**Requirements**:
- Delete 키: 휴지통으로 이동 (기본 동작)
- Shift+Delete: 영구 삭제 (확인 다이얼로그)

**현재 상태**:
- Delete 키로 영구 삭제만 가능
- 휴지통 이동 기능 없음

**구현 필요 사항**:
1. `Microsoft.VisualBasic.FileIO.RecycleOption` 사용 또는 Shell API 호출
2. Delete vs Shift+Delete 키 구분 로직
3. 영구 삭제 확인 다이얼로그

#### 2.3 파일 작업 진행률 표시
**Status**: ❌ 미구현
**Requirements**:
- 대용량 복사/이동 시 진행률 표시 (플라이아웃 또는 하단 패널)
- 현재 파일명, 전체 진행률(%), 남은 시간, 속도(MB/s) 표시
- 일시정지/취소 버튼 제공

**현재 상태**:
- 파일 작업이 UI 스레드를 블록할 위험 있음
- 대용량 파일 복사/이동 시 진행 상황 표시 없음

**구현 필요 사항**:
1. 비동기 파일 복사/이동 로직 (`IProgress<T>` 사용)
2. 진행률 ViewModel (`FileOperationProgressViewModel`)
3. 진행률 UI (InfoBar 또는 Custom Flyout)
4. 취소 토큰 및 일시정지 로직

#### 2.4 파일 충돌 처리 다이얼로그
**Status**: ⚠️ 부분 구현 (자동 넘버링만)
**Requirements**:
- 동일 이름 파일 존재 시 다이얼로그 표시:
  - 덮어쓰기 (Replace)
  - 건너뛰기 (Skip)
  - 둘 다 유지 (Keep Both — 자동 넘버링)
  - 나머지 항목에 모두 적용 체크박스

**현재 상태**:
- 붙여넣기 시 자동 넘버링만 지원 (충돌 시 항상 " (n)" 추가)
- 사용자 선택권 없음

**구현 필요 사항**:
1. `FileConflictDialog` ContentDialog 구현
2. 충돌 처리 옵션 열거형
3. "모두 적용" 체크박스 상태 관리

#### 2.5 외부 드래그 앤 드롭
**Status**: ❌ 미구현
**Requirements**:
- Span → 바탕화면/다른 앱: 파일 드래그 아웃 지원
- 바탕화면/다른 앱 → Span: 파일 드래그 인 수용
- 드롭 시 이동(기본) / Ctrl 누르면 복사
- 폴더 위 호버 시 자동 확장

**구현 필요 사항**:
1. `DragItemsStarting` 이벤트 처리 (드래그 아웃)
2. `DragOver`, `Drop` 이벤트 처리 (드래그 인)
3. `DataPackage` 설정 (파일 경로 포함)
4. Ctrl 키 감지를 통한 복사/이동 구분

### 🟡 Priority 2: Phase 2 보완 - Miller Engine 완성도

#### 2.6 다중 선택 (Multi-Select)
**Status**: ❌ 미구현
**Requirements**:
- Ctrl+클릭: 개별 항목 추가 선택
- Shift+클릭: 범위 선택
- Ctrl+A: 현재 컬럼 전체 선택
- 선택 수는 상태바에 실시간 반영

**현재 상태**:
- 단일 선택만 가능
- 다중 파일 작업 불가능

**구현 필요 사항**:
1. `ListView.SelectionMode = "Multiple"`
2. `SelectedItems` 바인딩
3. Ctrl/Shift 키 조합 감지
4. 상태바 선택 항목 수 표시

#### 2.7 정렬 및 컬럼 너비 기억
**Status**: ❌ 미구현
**Requirements**:
- 폴더별 정렬 방식(이름/날짜/크기/종류) 기억
- 밀러 컬럼 각 열의 사용자 조정 너비 기억
- 설정 초기화 옵션 제공

**구현 필요 사항**:
1. 정렬 UI (ComboBox 또는 ContextMenu)
2. 정렬 로직 (`IComparer<IFileSystemItem>`)
3. 설정 저장/로드 (`ApplicationData.LocalSettings`)
4. 컬럼 너비 조절 Thumb 컨트롤

#### 2.8 폴더/파일 크기 계산
**Status**: ❌ 미구현
**Requirements**:
- 상태바에 선택된 항목의 총 크기 실시간 표시
- 폴더 우클릭 속성에서 하위 전체 크기 비동기 계산

**구현 필요 사항**:
1. 재귀 폴더 크기 계산 로직 (비동기)
2. 상태바 크기 표시 ViewModel 바인딩
3. 크기 포맷팅 Converter (KB, MB, GB)

### 🟢 Priority 3: Phase 4 - Window Management

#### 2.9 탭 분리 (Tear-off Tabs) **Killer Feature**
**Status**: ❌ 미구현
**Requirements**:
- 탭을 마우스로 잡고 창 밖으로 드래그하면 새로운 윈도우로 독립
- 반대로, 독립된 윈도우의 탭을 다른 윈도우로 합치기(Docking) 가능

**구현 필요 사항**:
1. `TabView.TabDragStarting`, `TabView.TabDroppedOutside` 이벤트 처리
2. 새 Window 인스턴스 생성 로직
3. ViewModel 데이터 전달/복제
4. 윈도우 간 탭 병합 로직

#### 2.10 분할 뷰 (Split View)
**Status**: ❌ 미구현
**Requirements**:
- 하나의 탭 화면을 좌/우로 2등분
- 좌측 패널 ↔ 우측 패널 간 파일 드래그 앤 드롭 이동/복사 지원

**구현 필요 사항**:
1. `Grid.ColumnDefinitions` 분할 레이아웃
2. `GridSplitter` 크기 조절
3. 각 패널에 독립적인 `ExplorerViewModel` 인스턴스
4. 패널 간 D&D 이벤트 처리

### 🔵 Priority 4: Phase 5 - Polish & UX

#### 2.11 뷰 모드 전환 (List/Grid)
**Status**: ❌ 미구현
**Requirements**:
- 자세히 보기 (List): 파일 크기, 날짜 등 속성 중심의 그리드 뷰
- 아이콘 보기 (Grid): 썸네일 중심의 갤러리 뷰 (이미지/영상 확인용)
- 커맨드 바 또는 단축키로 즉시 전환

**구현 필요 사항**:
1. `GridView` 템플릿 추가
2. 뷰 모드 전환 ViewModel 속성
3. DataTemplateSelector 또는 조건부 Template
4. 썸네일 생성 로직 (이미지/비디오)

#### 2.12 Quick Look (Space 키 프리뷰)
**Status**: ❌ 미구현
**Requirements**:
- 파일 선택 후 Space 키 한 번으로 즉석 프리뷰 오버레이
- 이미지, PDF, 텍스트, 마크다운, 코드 등 빠른 확인
- Esc로 닫기

**구현 필요 사항**:
1. `Popup` 또는 `ContentDialog` 기반 프리뷰 UI
2. 파일 타입별 렌더러 (Image, WebView2 for PDF/Markdown, TextBox for code)
3. Space 키 단축키 처리

#### 2.13 주소창 자동 완성
**Status**: ❌ 미구현
**Requirements**:
- 경로 직접 입력 시 자동 완성(Auto-Complete) 지원
- 입력 중 폴더 후보 드롭다운
- UNC 경로(`\\server\share`), 환경 변수(`%APPDATA%`) 지원

**구현 필요 사항**:
1. `AutoSuggestBox` 컨트롤 사용
2. 폴더 경로 자동 완성 로직
3. 환경 변수 확장

#### 2.14 컨텍스트 메뉴 (Shell 통합)
**Status**: ❌ 미구현
**Requirements**:
- 우클릭 시 윈도우 시스템 쉘 메뉴(Shell Context Menu) 호출
- 알집, 반디집 등 외부 확장 프로그램 호환

**구현 필요 사항**:
1. Shell API (`IContextMenu`) P/Invoke
2. `MenuFlyout` 동적 생성
3. 메뉴 항목 클릭 시 Shell 명령 실행

#### 2.15 미리보기 패널
**Status**: ❌ 미구현
**Requirements**:
- 밀러 컬럼의 가장 우측 패널에 선택된 파일의 메타데이터 및 썸네일 표시
- 분할 뷰에서는 기본 OFF, 커맨드 바 토글(Ctrl+Shift+P)로 활성 패널에만 표시

**구현 필요 사항**:
1. 미리보기 패널 UI (`Grid` 또는 별도 UserControl)
2. 파일 메타데이터 표시 (크기, 날짜, 속성 등)
3. 썸네일 생성 및 표시
4. 토글 명령어 (Ctrl+Shift+P)

## 3. 우선순위별 개발 계획

### Phase 3: File Operations & Safety (최우선)
**목표**: 사용자가 안심하고 파일을 다룰 수 있는 안전 장치 구축

**구현 순서**:
1. **Undo/Redo 시스템** (가장 중요! - 2주)
   - 파일 작업 히스토리 스택
   - 되돌리기/다시 실행 로직
   - 상태바 토스트 알림

2. **휴지통 지원** (1주)
   - Delete 키 → 휴지통 이동
   - Shift+Delete → 영구 삭제 확인

3. **파일 작업 진행률** (1주)
   - 비동기 복사/이동 with IProgress
   - 진행률 UI 및 취소 기능

4. **파일 충돌 처리** (3일)
   - 충돌 다이얼로그
   - 사용자 선택 옵션

5. **외부 D&D** (1주)
   - 드래그 아웃/인 지원
   - DataPackage 처리

**예상 소요 기간**: 5~6주

### Phase 2 보완: Miller Engine 완성도 (중요도 높음)
**목표**: Miller Columns의 핵심 기능 완성

**구현 순서**:
1. **다중 선택** (3일)
2. **정렬 기능** (1주)
3. **폴더 크기 계산** (3일)
4. **컬럼 너비 기억** (3일)

**예상 소요 기간**: 2~3주

### Phase 4: Window Management (중요도 중간)
**목표**: 탭 분리 등 고급 창 관리 기능

**구현 순서**:
1. **탭 분리 (Tear-off)** (2주)
2. **분할 뷰** (1주)

**예상 소요 기간**: 3주

### Phase 5: Polish & UX (추후)
**목표**: 사용자 경험 향상 및 완성도 강화

**구현 순서**:
1. **Quick Look** (1주)
2. **뷰 모드 전환** (1주)
3. **컨텍스트 메뉴** (1주)
4. **미리보기 패널** (3일)
5. **주소창 자동 완성** (3일)

**예상 소요 기간**: 3~4주

## 4. 기술적 고려사항

### 4.1 Undo/Redo 구현 전략
```csharp
// 인터페이스 설계
public interface IFileOperation
{
    Task ExecuteAsync();
    Task UndoAsync();
    string Description { get; }
}

// 구체 클래스 예시
public class DeleteFileOperation : IFileOperation
{
    private readonly string _sourcePath;
    private readonly string _recycledPath;

    public async Task ExecuteAsync()
    {
        // Move to recycle bin
        await RecycleBinService.MoveToRecycleBinAsync(_sourcePath);
    }

    public async Task UndoAsync()
    {
        // Restore from recycle bin
        await RecycleBinService.RestoreAsync(_recycledPath);
    }
}

// 히스토리 관리
public class FileOperationHistory
{
    private Stack<IFileOperation> _undoStack = new(50);
    private Stack<IFileOperation> _redoStack = new(50);

    public async Task ExecuteAsync(IFileOperation operation)
    {
        await operation.ExecuteAsync();
        _undoStack.Push(operation);
        _redoStack.Clear();
    }

    public async Task UndoAsync()
    {
        if (_undoStack.Count > 0)
        {
            var operation = _undoStack.Pop();
            await operation.UndoAsync();
            _redoStack.Push(operation);
        }
    }
}
```

### 4.2 파일 작업 진행률 패턴
```csharp
public async Task CopyFilesWithProgressAsync(
    List<string> sources,
    string destination,
    IProgress<FileOperationProgress> progress,
    CancellationToken cancellationToken)
{
    long totalBytes = sources.Sum(s => new FileInfo(s).Length);
    long copiedBytes = 0;

    foreach (var source in sources)
    {
        var fileInfo = new FileInfo(source);
        // Copy with progress
        await CopyFileAsync(source, destination,
            new Progress<long>(bytes =>
            {
                copiedBytes += bytes;
                progress.Report(new FileOperationProgress
                {
                    CurrentFile = fileInfo.Name,
                    TotalBytes = totalBytes,
                    CopiedBytes = copiedBytes,
                    Percentage = (int)(copiedBytes * 100 / totalBytes)
                });
            }),
            cancellationToken);
    }
}
```

### 4.3 다중 선택 ViewModel 패턴
```csharp
public partial class FolderViewModel : ObservableObject
{
    [ObservableProperty]
    private ObservableCollection<FileSystemViewModel> _selectedItems = new();

    partial void OnSelectedItemsChanged(ObservableCollection<FileSystemViewModel> value)
    {
        // Update status bar
        MainViewModel.Instance.StatusBarText = $"{value.Count} items selected";

        // Calculate total size
        long totalSize = value.Sum(item => item.Size);
        MainViewModel.Instance.TotalSizeText = FormatBytes(totalSize);
    }
}
```

## 5. 다음 단계 권장사항

### 즉시 착수 가능한 작업
1. **Undo/Redo 시스템 설계 문서 작성**
   - `/pdca design file-operations-safety`
   - `IFileOperation` 인터페이스 상세 설계
   - 각 파일 작업별 구현 계획

2. **다중 선택 기능 구현** (빠른 가치 제공)
   - `/pdca design multi-select`
   - ListView SelectionMode 변경
   - 상태바 연동

### 중장기 계획
- Phase 3 완료 후 Phase 4 진행
- Phase 5는 MVP 출시 후 사용자 피드백 반영하여 우선순위 조정

## 6. 성공 지표

### Phase 3 완료 기준
- [ ] Ctrl+Z로 파일 삭제/이동/이름 변경 되돌리기 가능
- [ ] Delete 키로 휴지통 이동, Shift+Delete로 영구 삭제
- [ ] 100MB 이상 파일 복사 시 진행률 표시
- [ ] 파일 이름 충돌 시 사용자 선택 다이얼로그 표시
- [ ] 외부 앱과 D&D 양방향 지원

### Phase 2 보완 완료 기준
- [ ] Ctrl+클릭으로 다중 선택 가능
- [ ] 선택된 파일 수와 총 크기가 상태바에 표시
- [ ] 폴더별 정렬 방식 기억 및 적용
- [ ] 컬럼 너비 조절 및 저장

## 7. 리스크 및 대응

### 리스크 1: Undo/Redo 복잡도
**문제**: 파일 시스템 작업의 되돌리기는 복잡하고 예외 케이스 많음
**대응**:
- 초기에는 단순 작업(삭제, 이름 변경)만 지원
- 복사/이동은 2단계로 추후 추가

### 리스크 2: 외부 D&D Shell 통합
**문제**: WinUI 3의 D&D API가 제한적일 수 있음
**대응**:
- Win32 API P/Invoke 준비
- 대체 구현 방안 조사 (OLE D&D)

### 리스크 3: 성능 이슈 (대용량 파일)
**문제**: 파일 작업 진행률 계산이 UI 블록할 수 있음
**대응**:
- 모든 I/O를 비동기로 처리
- 진행률 업데이트는 throttling 적용 (100ms 간격)

## 8. 참고 자료

- [WinUI 3 File Picker](https://learn.microsoft.com/en-us/windows/apps/develop/ui-input/file-pickers)
- [IProgress<T> Pattern](https://learn.microsoft.com/en-us/dotnet/api/system.iprogress-1)
- [Command Pattern for Undo/Redo](https://refactoring.guru/design-patterns/command)
- [Windows Shell Context Menu](https://learn.microsoft.com/en-us/windows/win32/shell/context-menu)
