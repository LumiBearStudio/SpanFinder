# Known Bugs

## Bug 1: F2 Rename — Windows Explorer 방식 동작 안됨
- **상태**: 수정 적용 (런타임 검증 필요)
- **원인**: WinUI 3 TextBox에서 `Focus()` 호출 시 선택 영역이 리셋되어 직후 `Select()` 호출이 무시됨. 또한 `GetListViewForColumn()`이 null 반환 시 조용히 실패.
- **수정 내용**:
  1. `ApplyRenameSelection()` — Select()를 DispatcherQueue로 지연 실행하여 Focus()와 충돌 방지
  2. `FocusRenameTextBox()` — ListView/Container 못 찾으면 재시도 로직 추가
  3. Container 미로드 시 ScrollIntoView + 재시도 로직 추가
- **관련 파일**: `MainWindow.xaml.cs` (FocusRenameTextBox, FocusRenameTextBoxCore, ApplyRenameSelection)

## Bug 2: Rename 취소 — 같은 탭 내 포커스 이동 시 취소 안됨
- **상태**: 수정 적용 (런타임 검증 필요)
- **원인**: SelectionChanged/GotFocus 이벤트가 LostFocus보다 먼저 실행되어 `IsRenaming=false` 설정 → TextBox Collapsed → LostFocus 미발생 또는 가드 조건에서 조기 반환
- **수정 내용**:
  1. `OnRenameTextBoxLostFocus()` — IsRenaming 가드 조건 완화 (IsRenaming이 이미 false여도 정리 작업 수행)
  2. `CancelAnyActiveRename()` — 취소 성공 시 `_justFinishedRename = true` 플래그 설정
- **관련 파일**: `MainWindow.xaml.cs` (OnRenameTextBoxLostFocus, CancelAnyActiveRename)

## Bug 3: Sidebar 즐겨찾기 아이콘 — ✅ 수정됨
- **상태**: 수정 완료
- **수정 내용**: StaticResource RemixIcons + IconService.FolderGlyph 사용으로 해결

## Bug 4: Sidebar 드라이브 아이콘 — DriveType별 구분 아이콘
- **상태**: 수정 적용 (런타임 검증 필요)
- **원인**: 모든 드라이브에 동일한 DriveGlyph 사용
- **수정 내용**:
  1. `IconService` — `GetDriveGlyph(driveType)` 메서드 추가, `RemovableGlyph`, `CdRomGlyph` 속성 추가
  2. `FileSystemService` — `CreateDriveItem()`에서 DriveType별 아이콘 분기 (`Fixed`=HDD, `Removable`=USB, `Network`=Globe, `CDRom`=Disc)
  3. 드라이브 이름도 타입별 라벨 ("Local Disk", "USB Drive", "Network Drive", "CD/DVD Drive")
  4. CDRom 드라이브도 목록에 포함
- **관련 파일**: `Services/IconService.cs`, `Services/FileSystemService.cs`

## Bug 5: 밀러 컬럼 경로 하이라이트 — 배경색 미표시
- **상태**: 수정 적용 (런타임 검증 필요)
- **원인**: PathBackground(반투명)가 ListView ItemContainerStyle의 Selected 배경과 겹쳐 시각적으로 구분 불가. x:Bind OneWay 바인딩 자체는 동작하지만 차이가 미미.
- **수정 내용**:
  1. FolderTemplate에 **좌측 accent bar (3px Border)** 추가 — IsOnPath 바인딩으로 경로 상 폴더에만 표시
  2. SpanAccentDimColor 투명도 증가 (#40 → #59, 25% → 35%)
  3. Grid ColumnDefinition 5열로 확장 (indicator bar + icon + text + count + chevron)
- **관련 파일**: `MainWindow.xaml` (FolderTemplate ×2), `App.xaml` (SpanAccentDimColor)

## ~~Bug 6: Rubber-band 선택 — 아이템 패딩 영역에서 시작 안 됨~~ ✅ 수정됨
- **상태**: 수정 완료
- **설명**: 마우스 드래그로 사각형을 그려 여러 파일을 한번에 선택하는 기능 (러버밴드/마퀴 선택)
- **수정 내용**: `RubberBandSelectionHelper.cs`에서 IsPointerOnItemContent() 구현으로 콘텐츠 vs 패딩 구분

## Bug 7: 탭 전환 시 이전 탭 멀티 선택 해제 여부 — 미해결
- **상태**: 미해결 (정책 결정 필요)
- **증상**: 탭 전환 후 이전 탭의 멀티 선택이 유지됨
- **참고**: macOS Finder와 Windows Explorer 모두 탭별 선택을 유지하는 것이 일반적. 현재 동작이 올바를 수 있음.
- **관련 파일**: `MainWindow.xaml.cs` (탭 전환 핸들러), `FolderViewModel.cs` (SelectedItems)
