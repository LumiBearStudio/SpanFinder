# SPAN Finder — Known Issues & Bug Pattern Catalog

> 과거 발생한 버그의 패턴을 카탈로그화하여, 유사 버그 재발 시 빠르게 원인을 특정하기 위한 참조 문서.
> 최종 갱신: 2026-03-19

---

## 수정 완료된 버그 패턴

### 1. 분할뷰 미리보기 이중 표시 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | 분할 뷰 토글 시 좌/우 양쪽에 미리보기가 동시 표시됨 |
| **원인** | `ToggleSplitView()`에서 분할 뷰 활성화 시 좌측 인라인 미리보기를 비활성화하지 않음 |
| **수정** | `SplitPreviewManager.cs` — 분할 뷰 활성화 시 `HideInlinePreview()` + `MillerInlinePreviewEnabled = false` |
| **교훈** | 상호 배타적 UI 상태 토글 시 **반대쪽 먼저 정리** 후 새 상태 활성화 |

### 2. Grid 레이아웃 크기 변동 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 인라인 미리보기 너비가 창 리사이즈 시 비례 변동 |
| **원인** | 밀러 컬럼과 미리보기 모두 `Star` 단위 사용 |
| **수정** | `SplitPreviewManager.cs` — 밀러=`Star`(가변), 미리보기=`Pixel`(고정)으로 분리 |
| **교훈** | Grid 레이아웃: **고정=Pixel, 가변=Star** 명확히 분리 |

### 3. 컨텍스트 메뉴 AccessKey CJK 문제 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | CJK 로케일에서 메뉴 항목 사라짐 + AccessKey 미동작 |
| **원인** | (1) CJK 괄호 strip으로 필터 매칭 방해, (2) `AccessKey` 속성이 visual tree 미연결 시 native crash |
| **수정** | CJK 괄호 strip 제거, `AccessKey` 대신 `Tag` + 커스텀 `TryInvokeAccessKey()` |
| **교훈** | WinUI 3 `AccessKey`는 visual tree 연결 전 설정 시 crash — `Tag` + 커스텀 핸들러가 안전 |

### 4. 한국어 키보드 단축키 미동작 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 한국어 IME에서 Ctrl+\`, Ctrl+', Ctrl+, 미동작 |
| **원인** | IME 활성 시 `VirtualKey` 매핑이 영문과 다름 |
| **수정** | `KeyboardHandler.cs` — `default` 분기에 ScanCode fallback: backtick(41), quote(40), comma(51) |
| **교훈** | 다국어 키보드는 `VirtualKey` + `ScanCode` 이중 검사 필수 |

### 5. StoreContext 네이티브 크래시 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | 비-Store 환경에서 Access Violation (0xC0000005) |
| **원인** | `StoreContext.GetDefault()` — Store 서명 없으면 네이티브 크래시, try-catch 불가 |
| **수정** | `MainWindow.xaml.cs` — `IsStoreInstalled()` 가드: `Package.Current.SignatureKind == Store` 사전 체크 |
| **교훈** | Windows Store API는 **SignatureKind 사전 체크 필수**. 네이티브 크래시는 try-catch 보호 불가 |

### 6. async void 델리게이트 크래시 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | `DispatcherQueue.TryEnqueue(async () => {...})` 내부 예외로 JIT 디버거 크래시 |
| **원인** | `async () => {}` 람다가 `async void`로 컴파일, 예외가 모든 try-catch 우회 |
| **수정** | `async Task` 메서드로 분리 후 `_ = MethodAsync()` 호출 |
| **교훈** | `DispatcherQueue.TryEnqueue`에 async 람다 금지. 항상 `async Task` 메서드로 분리 |

### 7. NavigateToSegment 미보호 예외 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 브레드크럼 클릭 시 간헐적 크래시 |
| **원인** | `async void` 메서드에 try-catch 없음 |
| **수정** | `ExplorerViewModel.cs` — try-catch + DebugLogger 추가 |
| **교훈** | 모든 `async void` 메서드에 **최외곽 try-catch 필수** |

### 8. PropertyChanged 다중 구독 메모리 누수 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 탭/컬럼 전환 반복 시 메모리 증가 |
| **원인** | 컬럼 Replace 시 이전 ViewModel PropertyChanged 구독 해제 누락 |
| **수정** | 모든 구독 지점에 `-= before +=` 패턴 적용 |
| **교훈** | 이벤트 구독: **항상 `-=` 후 `+=`**. 특히 컬렉션 항목 교체 시 |

### 9. 리네임 후 Enter 파일 실행 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | F2 이름 변경 완료(Enter) 직후 파일이 열림 |
| **원인** | 동일 Enter 이벤트가 리네임 완료 → 파일 열기로 이중 처리 |
| **수정** | `_justFinishedRename` 플래그로 Enter 이중 처리 방지 |
| **교훈** | 동일 키가 여러 핸들러를 통과할 때 **상태 플래그로 전파 차단** |

### 10. 플로팅 경로 인디케이터 분할뷰 미동작 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 우측 패인에 경로 인디케이터(파란 세로 바) 미표시 |
| **원인** | 좌측 패인에만 `Loaded/Unloaded` 이벤트 핸들러 연결 |
| **수정** | 우측 패인 DataTemplate에도 동일 이벤트 핸들러 추가 |
| **교훈** | 분할뷰: **좌측에 적용된 모든 시각 요소가 우측에도 동일하게 복제되었는지 체크리스트 확인** |

### 11. ContinueWith fire-and-forget (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 간헐적 타이밍 버그, 예외 무시 |
| **원인** | `Task.ContinueWith` 패턴이 예외를 삼키고 UI 스레드 마샬링 불안정 |
| **수정** | `async/await`로 전환 + `DispatcherQueue` null 체크 |
| **교훈** | `ContinueWith`보다 `async/await` 선호. fire-and-forget 시 try-catch 필수 |

### 12. 뷰 리프레시 깜빡임 (P2)

| 항목 | 내용 |
|------|------|
| **증상** | F5 새로고침 시 전체 목록 깜빡임 + 스크롤/선택 초기화 |
| **원인** | `Children.Clear()` 후 전체 재추가 |
| **수정** | `FolderViewModel.SyncChildren()` — diff 기반 증분 업데이트 (50% 임계값 fallback) |
| **교훈** | 컬렉션 갱신: **diff 기반 증분 업데이트** 기본, 변경 50% 초과 시만 전체 교체 |

### 13. 분할모드 툴바 이중 분리선 (P2)

| 항목 | 내용 |
|------|------|
| **증상** | 분할모드에서 버튼 숨길 때 인접 분리선(Rectangle) 2개 연속 표시 |
| **원인** | 숨김 버튼 옆 분리선이 동일 Visibility 바인딩을 공유하지 않음 |
| **수정** | `MainWindow.xaml` — 분리선에 `IsNotSplitVisible()` Visibility 바인딩 추가 |
| **교훈** | 툴바 버튼 숨길 때 **인접 분리선도 동일 Visibility 바인딩 필수** |

### 14. 비-Miller 뷰 D&D Drop 미동작 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | Details/List/Icon 뷰에서 드래그 시 커서 표시는 되지만 Drop 이벤트 자체가 발생하지 않음 |
| **원인** | `ViewDragDropHelper.SetupDragData()`에서 `RequestedOperation = Copy`만 설정. DragOver에서 `AcceptedOperation = Move` 설정 시 `Move ⊄ {Copy}`이므로 WinUI OLE 레이어가 Drop 차단 |
| **수정** | `ViewDragDropHelper.cs` — `RequestedOperation = Copy \| Move \| Link` (Miller와 동일) |
| **교훈** | WinUI 3 D&D에서 **`AcceptedOperation`은 반드시 `RequestedOperation`의 부분집합**이어야 함. 위반 시 Drop 이벤트 자체가 발생하지 않음 |

### 15. 비-Miller 뷰 D&D 폴더 대상 미감지 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | 폴더 위에 드롭해도 해당 폴더가 아닌 현재 폴더(root)로 이동됨 |
| **원인** | (1) `FindElementsInHostCoordinates`가 WinUI 3 드래그 중 작동하지 않음 (2) `ListViewItem`만 체크하여 GridView의 `GridViewItem` 무시 |
| **수정** | `ViewDragDropHelper.cs` — `ItemsPanelRoot` 컨테이너 순회 + `TransformToVisual` bounds 비교 방식으로 전환, `SelectorItem` 공통 타입 사용 |
| **교훈** | (1) WinUI 3 드래그 중 `FindElementsInHostCoordinates` 사용 불가 — **컨테이너 bounds 직접 비교** 필요 (2) ListView/GridView 공통 코드는 `ListViewItem`이 아닌 **`SelectorItem`** 사용 |

### 16. ListViewRight 컨텍스트 메뉴 미초기화 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 분할뷰 우측 List 뷰에서 우클릭 컨텍스트 메뉴 미표시 |
| **원인** | `MainWindow.xaml.cs` 초기화에서 `ListViewRight.ContextMenuService/Host/OwnerHwnd` 누락 (DetailsViewRight, IconViewRight만 초기화) |
| **수정** | `MainWindow.xaml.cs` — `ListViewRight` 초기화 3줄 추가 |
| **교훈** | 분할뷰 우측 패널에 **새 뷰 추가 시 ContextMenuService/Host/OwnerHwnd 초기화 체크리스트 확인** |

### 17. 분할뷰 좌/우클릭 교차 시 컬럼 연쇄 생성 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 분할뷰에서 좌클릭/우클릭을 번갈아 패인 전환하면 하위 컬럼이 하나씩 계속 추가됨 |
| **원인** | (1) `FocusColumnAsync`가 패인 전환(`FocusActivePane`) 시 `SelectedChild==null`인 마지막 컬럼의 첫 항목을 자동 선택 → 폴더면 새 컬럼 생성 → 다음 패인 전환에서 반복 (2) 우클릭도 `FocusActivePane()`을 호출하여 이중 트리거 |
| **수정** | (1) `FocusColumnAsync`에 `autoSelect` 파라미터 추가, `FocusActivePane`에서 `autoSelect: false` 전달 (2) `OnLeft/RightPanePointerPressed`에서 `IsRightButtonPressed` 시 `FocusActivePane` 호출 생략 |
| **교훈** | 포커스 이동과 선택(네비게이션)은 **분리된 관심사**. 패인 전환 시 포커스만 이동하고 자동 선택은 사용자 의도 동작(키보드 탐색, 탭 전환 등)에서만 수행 |

### 18. 썸네일 로딩 UI 스레드 차단 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 이미지 파일이 있는 폴더 진입 시 2~3초 멈춤. 썸네일 간 긴 간격 |
| **원인** | `LoadThumbnailAsync()`가 UI 스레드에서 호출되어 `File.Exists()`, `File.OpenRead()`, `CopyToAsync()` 등 디스크 I/O가 UI 스레드 차단 |
| **수정** | `FileViewModel.cs` — `File.Exists` + `File.ReadAllBytes`를 `Task.Run`으로 백그라운드 이동. UI 스레드에서는 `SetSourceAsync`(WinUI 필수)만 실행 |
| **교훈** | `async Task` 메서드도 **첫 await까지는 호출자 스레드에서 동기 실행**. 파일 I/O는 반드시 `Task.Run`으로 명시적 백그라운드 이동 |

### 22. Move 후 동일 폴더 중복 리프레시 (P2)

| 항목 | 내용 |
|------|------|
| **증상** | 파일 1개 이동에 4.6초 소요 (10,002개 폴더에서) |
| **원인** | `RefreshSourceColumnsForMove`(소스 폴더)와 `RefreshCurrentFolderAsync`(대상 폴더)가 동일 컬럼을 2회 연속 리로드 |
| **수정** | `RefreshSourceColumnsForMove`가 리프레시한 컬럼 인덱스 반환 → `RefreshCurrentFolderAsync`에서 중복 스킵 |
| **교훈** | 파일 작업 완료 후 리프레시 시 **이미 갱신된 컬럼 추적** 필수. 대용량 폴더에서 중복 리프레시는 치명적 |

### 23. 소규모 파일 작업 시 불필요한 진행 팝업 (P2)

| 항목 | 내용 |
|------|------|
| **증상** | 파일 1개 이동/복사에도 진행 팝업이 2초간 표시되어 느려 보임 |
| **원인** | `RemoveCompletedOperation`에서 `Task.Delay(2000)` 고정 지연 + 모든 작업에 진행 팝업 표시 |
| **수정** | (1) 소규모 작업(파일 ≤10개 AND ≤50MB): 진행 팝업 미표시, 토스트만 (2) 대규모/고용량: 진행 팝업 표시 + 완료 후 지연 1초→300ms 단축 |
| **교훈** | 파일 작업 UX는 **규모에 따라 분기** — 소규모는 토스트, 대규모는 진행 팝업+일시정지/취소 |

---

## 미수정 / 주의 필요

### R1. FileOperationHandler 빈 catch — 수정 완료

- **위치**: `MainWindow.FileOperationHandler.cs` — HandlePasteAsShortcut 클립보드 접근
- **상태**: 모든 catch 블록에 `DebugLogger.Log()` 적용 완료

### R2. Undo 삭제 복원 — 구현됨 (제한적)

- **위치**: `Services/FileOperations/DeleteFileOperation.cs:UndoAsync()`
- **구현**: Shell.Application COM으로 Recycle Bin 항목 검색 및 복원
- **제한사항**: 휴지통이 비어있거나 영구 삭제(Shift+Delete)된 경우 복원 불가
- **상태**: 구현 완료. 복원 실패 시 에러 메시지 표시

### R3. QuickLook 미리보기 긴 파일명 처리 — 수정 완료

- **증상**: QuickLook 미리보기에서 긴 파일명이 부적절하게 잘림
- **수정**: (1) 타이틀 — `MiddleEllipsis()` 메서드 추가 (확장자 보존, 중간 `…` 삽입, 50자 제한) (2) Info-Only 파일명 — `MaxLines` 제거, `TextWrapping=Wrap` + `IsTextSelectionEnabled=True`
- **상태**: 수정 완료

### R4. 자세히뷰 컬럼 리사이즈 시 데이터 행 1px 떨림 — 미수정

- **증상**: GridSplitter로 컬럼 크기 조절 시 데이터 행이 1px 좌우로 흔들림. 첫 드래그 시 데이터 위치 점프.
- **원인**: 헤더 Grid(12컬럼)와 데이터 ItemTemplate Grid(7컬럼)가 독립적인 `*` 계산 → splitter 열 포함 여부에 따라 누적 오차 발생. DPI 스케일링 시 소수점 반올림 차이 증폭.
- **시도한 수정들**:
  - Splitter 열 콜백 등록 + ActualWidth 동기화 → 부분 개선
  - `Math.Floor` → `SnapToPixel` (DPI 인식 반올림) → 부분 개선
  - 디바운스 (`_columnWidthUpdatePending`) → 드래그 중 흔들림 감소
  - Margin → Width-only 방식 (MeasureOverride COMException 방지)
  - 절대 오프셋 계산 (`GetHeaderColumnOffset`) → 현재 상태
- **근본 해법**: WinUI 3에는 WPF의 `GridView` (테이블 뷰)가 없음. CommunityToolkit `DataGrid` 도입 또는 커스텀 Panel 구현이 필요하나 대규모 리팩토링 필요.
- **현재 상태**: 기능적으로 동작하지만 미세 떨림 존재. 차후 개선 예정.
- **관련 코드**: `DetailsModeView.xaml.cs` — `RecalcCellTotalWidths()`, `ApplyCellWidths()`, `OnColumnWidthChanged()`, `GetHeaderColumnOffset()`

### R5. WinUI 3 Title Bar 규칙 (가이드)

- **Never**: `SetRegionRects(Caption, ...)` 수동 호출 — 캡션 버튼 영역 덮어씀
- **Never**: `SetTitleBar()` + `WM_NCHITTEST` 혼용 — IXP 레이어 충돌
- **Always**: `SetRegionRects(Passthrough, rects)` only + `TransformToVisual(null)` + `RasterizationScale`

### 19. 비-Miller 뷰 인라인 리네임 미동작 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | Details/List/Icon 뷰에서 F2 리네임 시 이름이 변경되지 않음 |
| **원인** | 리네임 TextBox의 `x:Bind EditableName, Mode=TwoWay`에 `UpdateSourceTrigger=PropertyChanged` 누락. 기본값 `LostFocus`이므로 Enter 시 `EditableName`이 아직 갱신 안 됨 → `newName == Name` → 스킵 |
| **수정** | `DetailsModeView.xaml`, `ListModeView.xaml`, `IconModeView.xaml` — 모든 리네임 TextBox에 `UpdateSourceTrigger=PropertyChanged` 추가 |
| **교훈** | WinUI `x:Bind TwoWay`의 기본 `UpdateSourceTrigger`는 `LostFocus`. 키 이벤트(Enter)로 값을 읽는 TextBox는 반드시 `PropertyChanged` 명시 |

### 20. 분할뷰 HandleRename/Sort 뷰 모드 디스패치 오류 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 분할뷰에서 좌=Miller, 우=Details일 때 우측 패인에서 F2/정렬이 Miller로 디스패치됨 |
| **원인** | `HandleRename()`, `GetActiveSortColumn()` 등이 `ViewModel.CurrentViewMode`(좌측 패인)만 확인하고 `RightViewMode` 미확인 |
| **수정** | `FileOperationHandler.cs` 3곳에 `(IsSplitViewEnabled && ActivePane == Right) ? RightViewMode : CurrentViewMode` 패턴 적용 |
| **교훈** | 뷰 모드 분기 시 **항상 활성 패인의 뷰 모드** 사용. `GetCurrentSelected()`처럼 패인 인식 패턴 통일 |

### 21. List뷰 F2 이중 호출로 확장자 전체 선택 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | List뷰에서 F2 리네임 시 파일명만이 아닌 확장자까지 전체 선택됨 |
| **원인** | `OnGlobalKeyDown`이 `handledEventsToo=true`로 등록 → `OnListKeyDown`이 F2 처리 후 global handler도 재호출 → `_renameSelectionCycle`이 0→1로 진행되어 "전체 선택" 모드 적용 |
| **수정** | `KeyboardHandler.cs` F2 분기에 `if (!e.Handled)` 가드 추가 |
| **교훈** | `handledEventsToo=true` 핸들러에서는 **`e.Handled` 체크 필수**. 뷰가 이미 처리한 키를 global handler가 재처리하면 상태 머신이 꼬임 |

### 24. 인라인 미리보기 리사이즈 불가 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | Miller Columns 인라인 미리보기 너비를 드래그로 조절할 수 없음 |
| **원인** | `InlinePreviewSplitterCol`에 스플리터 컨트롤이 없었음 (ColumnDefinition만 존재, 드래그 핸들 미배치) |
| **수정** | `MainWindow.xaml` — `InlinePreviewSplitter` Border 추가 (ManipulationDelta, 사이드바 스플리터와 동일 패턴) |
| **교훈** | ColumnDefinition만으로는 리사이즈 불가. 반드시 **드래그 가능한 UI 컨트롤**(Border/GridSplitter)을 배치해야 함 |

### 25. 네트워크 바로가기 삭제 권한 오류 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 네트워크 위치 "연결 끊기" 시 `Access denied` 오류로 삭제 실패 |
| **원인** | Network Shortcuts 폴더에 ReadOnly 속성 설정됨 → `Directory.Delete` 거부 |
| **수정** | `DeleteNetworkShortcutFolder()` — `FileAttributes.Normal`로 속성 해제 후 삭제 |
| **교훈** | Windows 시스템 폴더 삭제 시 **ReadOnly/System 속성 해제** 선행 필수 |

### 26. 네트워크 드라이브 이름 불일치 (P2)

| 항목 | 내용 |
|------|------|
| **증상** | 네트워크 드라이브 이름이 Windows 탐색기와 다름 (VolumeLabel vs 공유 이름) |
| **원인** | `DriveInfo.VolumeLabel`은 원격 볼륨 레이블을 반환하지만, 탐색기는 UNC 공유 이름을 표시 |
| **수정** | `WNetGetConnectionW` P/Invoke로 UNC 경로 추출 → 공유 이름 기반 표시 |
| **교훈** | 네트워크 드라이브 표시에는 `DriveInfo.VolumeLabel` 대신 **`WNetGetConnection`** 사용 |

### 27. 이미 로드된 폴더 재선택 시 외부 변경 미반영 (P0)

| 항목 | 내용 |
|------|------|
| **증상** | 폴더 A 클릭 → 다른 폴더로 이동 → 외부에서 A 하위에 새 파일/폴더 생성 → A 재클릭 → 새 항목 안 보임 |
| **원인** | `HandleFolderSelectionAsync` → `EnsureChildrenLoadedAsync()`에서 `_isLoaded = true`면 즉시 return. 이전에 로드된 FolderViewModel 인스턴스가 재사용되어 디스크 재로드 없이 캐시된 Children 표시 |
| **수정** | `ExplorerViewModel.cs` — `IsAlreadyLoaded`인 폴더 재선택 시 `ReloadAsync()` 호출 (캐시 무효화 + 디스크 재로드). `SyncChildren` diff 기반이라 스크롤/선택 보존, ProgressRing 미표시 |
| **교훈** | 밀러 컬럼에서 폴더 재선택은 **항상 디스크 상태와 동기화** 필요. `_isLoaded` 플래그는 초기 로드 최적화용이지 캐시 유효성 보장이 아님. 디바운스(`_selectionDebounce`)가 빠른 키보드 탐색 시 불필요한 리로드 방지 |

### 28. 셸 확장 프로그램 활성화 시 시스템 오류 팝업 (P1) — GitHub Issue #1


| 항목 | 내용 |
|------|------|
| **증상** | 설정 → 도구 → 셸 확장 프로그램 활성화 후 우클릭 시 시스템 오류 팝업 표시 + 컨텍스트 메뉴 ~5초 지연 |
| **원인** | (1) 문제 있는 타사 셸 확장(백신, 클라우드 동기화 등)이 `QueryContextMenu()` COM 호출 중 크래시/행 → Windows OS가 WER 오류 다이얼로그 표시 (2) `InvokeCommand`에 에러 UI 억제 플래그 없음 (3) `GetCommandString`에서 높은 ID(>5000)로 `AccessViolation` 발생 가능 (NVIDIA 등) |
| **수정** | (1) `ShellContextMenu.cs` — `CreateSession()` 내 `QueryContextMenu` + `EnumerateMenuItems` 구간을 `SetThreadErrorMode(SEM_FAILCRITICALERRORS \| SEM_NOGPFAULTERRORBOX \| SEM_NOOPENFILEERRORBOX)`로 감싸기 (스레드 단위, try/finally로 복원) (2) `InvokeCommand` 2곳(`ShowNativeMenu`, `Session.InvokeCommand`)에 `CMIC_MASK_FLAG_NO_UI` 플래그 추가 (3) `GetCommandString` 호출에 `(mii.wID - FIRST_CMD) < 5000` 가드 추가 (4) P/Invoke 선언: `NativeMethods.cs`에 `SetThreadErrorMode` + `SEM_*` 상수 추가 |
| **참고** | MS 공식 권장: `SetThreadErrorMode`는 `SetErrorMode`보다 안전 (스레드 범위, 레이스 컨디션 없음). `CMIC_MASK_FLAG_NO_UI`는 MS CMINVOKECOMMANDINFO 공식 문서에 명시된 플래그. ID > 5000 가드는 Files App + RX-Explorer 두 프로젝트에서 공통 적용 |
| **교훈** | 셸 확장 COM 호스팅 시 (1) `SetThreadErrorMode`로 에러 다이얼로그 억제 (2) `CMIC_MASK_FLAG_NO_UI`로 InvokeCommand 에러 UI 억제 (3) `GetCommandString` ID 범위 가드 — 세 겹 방어 필수 |

### 29. Sentry 크래시 리포팅 중복 전송 + UI 차단 (P1)

| 항목 | 내용 |
|------|------|
| **증상** | 비치명적 예외 발생 시 Sentry 이벤트 2중 전송 + UI 스레드 3초 블로킹 |
| **원인** | (1) `OnUnhandledException`에서 `CaptureException`(비동기) + `CaptureFatalException`(동기 3초 flush) 모두 호출 → 중복 + UI 차단 (2) Sentry 캡처에 쓰로틀 없음 → 무제한 전송 (3) `CaptureFatalException`이 `e.Handled = true`인 비치명적 에러에도 사용 |
| **수정** | (1) `App.xaml.cs` — `OnUnhandledException`에서 `CaptureFatalException` 제거, `CaptureException`만 사용 (2) `_sentryCaptureCount` + `MaxSentryCapturesPerSession = 5` 쓰로틀 추가 (3) `DispatcherHelper.cs` — `HandleException`에도 `MaxSentryCaptures = 10` 세션 쓰로틀 추가 |
| **교훈** | (1) `CaptureFatalException`(동기 flush)은 프로세스 종료 직전(`OnDomainUnhandledException`)에서만 사용 (2) 비치명적 경로(`e.Handled = true`)에는 비동기 `CaptureException`만 (3) 모든 Sentry 전송 경로에 세션 쓰로틀 필수 |

---

## 버그 패턴 Quick Reference

| 패턴 | 핵심 규칙 | 파일 |
|------|-----------|------|
| 상호 배타적 UI | 토글 시 반대쪽 먼저 정리 | SplitPreviewManager.cs |
| Grid 레이아웃 | 고정=Pixel, 가변=Star | SplitPreviewManager.cs |
| CJK AccessKey | AccessKey 미사용, Tag+커스텀 핸들러 | ContextMenuService.cs |
| 다국어 키보드 | VirtualKey + ScanCode 이중 검사 | KeyboardHandler.cs |
| Store API | SignatureKind 사전 체크 | MainWindow.xaml.cs |
| async void | 최외곽 try-catch 필수 | 전체 |
| 이벤트 구독 | `-=` before `+=` | 전체 |
| 키 이벤트 전파 | 상태 플래그로 이중 처리 방지 | KeyboardHandler.cs |
| 컬렉션 갱신 | diff 증분 업데이트 우선 | FolderViewModel.cs |
| 분할뷰 복제 | 좌/우 동일 기능 체크리스트 | MainWindow.xaml |
| Visibility 바인딩 | 숨김 버튼 + 인접 분리선 동기화 | MainWindow.xaml |
| D&D RequestedOperation | AcceptedOperation ⊂ RequestedOperation 필수 | ViewDragDropHelper.cs |
| D&D 드래그 중 hit-test | FindElementsInHostCoordinates 불가, bounds 비교 사용 | ViewDragDropHelper.cs |
| ListView/GridView 공통 | ListViewItem이 아닌 SelectorItem 사용 | ViewDragDropHelper.cs |
| 분할뷰 우측 초기화 | ContextMenu/Host/OwnerHwnd 누락 체크 | MainWindow.xaml.cs |
| 패인 전환 vs 선택 | 포커스 이동 ≠ 자동 선택, autoSelect 파라미터 분리 | NavigationManager.cs |
| UI 스레드 I/O | 파일 I/O는 Task.Run 필수, async 첫 await까지 동기 | FileViewModel.cs |
| UpdateSourceTrigger | TwoWay + Enter 처리 시 PropertyChanged 필수 | DetailsModeView 등 |
| 분할뷰 뷰 모드 분기 | CurrentViewMode 아닌 활성 패인 뷰 모드 사용 | FileOperationHandler.cs |
| handledEventsToo 이중 처리 | e.Handled 체크로 뷰 처리 완료 키 스킵 | KeyboardHandler.cs |
| Measure 중 Margin 변경 금지 | ContainerContentChanging에서 Width만 변경 | DetailsModeView.xaml.cs |
| 헤더-데이터 컬럼 동기화 | 개별 반올림 X, 누적 합산으로 총 너비 계산 | DetailsModeView.xaml.cs |
| 폴더 재선택 시 디스크 동기화 | IsAlreadyLoaded → ReloadAsync (SyncChildren diff) | ExplorerViewModel.cs |
| 셸 확장 COM 방어 | SetThreadErrorMode + CMIC_MASK_FLAG_NO_UI + ID 가드 | ShellContextMenu.cs |
| Sentry 전송 쓰로틀 | 모든 전송 경로에 세션당 캡처 수 제한 | App.xaml.cs, DispatcherHelper.cs |
