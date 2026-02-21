# Known Bugs

## Bug 1: F2 Rename — Windows Explorer 방식 동작 안됨
- **증상**: F2 두 번 눌러도 확장자 포함 선택(전체 선택)으로 전환 안됨
- **기대 동작**: Windows Explorer처럼 F2 cycling (파일명만 → 전체 → 확장자만 → 반복)
- **관련 파일**: `FileSystemViewModel.cs` (BeginRename/CommitRename), `MainWindow.xaml.cs` (HandleRename/FocusRenameTextBox)
- **분석**: BeginRename에서 EditableName = Name (확장자 포함)으로 수정했으나 여전히 동작 안함. TextBox 바인딩 또는 F2 이벤트 전달 경로 추가 디버깅 필요.

## Bug 2: Rename 취소 — 같은 탭 내 포커스 이동 시 취소 안됨
- **증상**: 리네임 중 같은 탭 내 다른 컨트롤로 포커스 이동해도 리네임이 취소되지 않음
- **기대 동작**: ESC 외에도 다른 컨트롤에 포커스가 이동하면 리네임 취소
- **관련 파일**: `MainWindow.xaml.cs` (OnRenameTextBoxLostFocus, CancelAnyActiveRename, OnMillerColumnGotFocus, OnMillerColumnSelectionChanged)
- **분석**: CancelAnyActiveRename()을 SelectionChanged/GotFocus에 추가했으나 여전히 동작 안함. LostFocus 이벤트가 WinUI 3 ListView 내부에서 정상 발화되는지 확인 필요.

## Bug 3: Sidebar 즐겨찾기 아이콘 — 엉뚱한 글리프 표시
- **증상**: 사이드바 즐겨찾기 폴더 아이콘이 빈 사각형 또는 엉뚱한 문자로 표시됨. 밀러 컬럼의 노란 폴더 아이콘과 다름.
- **기대 동작**: 밀러 컬럼과 동일한 RemixIcons 폴더 아이콘(`\uEEA7`) 표시
- **관련 파일**: `MainWindow.xaml` (TreeView ItemTemplate), `FavoritesService.cs`, `SidebarFolderNode.cs`
- **분석**: WinUI 3 TreeView DataTemplate 내에서 FontIcon.FontFamily가 제대로 적용 안 됨. Segoe Fluent Icons → RemixIcons FontIcon → TextBlock 전환까지 시도. TextBlock + 직접 폰트 경로(`/Assets/Fonts/remixicon.ttf#remixicon`) 방식 테스트 필요.
- **시도한 방법**:
  1. Segoe Fluent Icons `\uED41` → 빈 사각형 (해당 글리프 미존재)
  2. RemixIcons FontIcon `\uEEA7` → 엉뚱한 글리프 (TreeView가 FontFamily 오버라이드)
  3. TextBlock + 직접 폰트 경로 → 미확인

## Bug 4: Sidebar 드라이브 아이콘
- **증상**: Bug 3과 동일 원인. 드라이브 아이콘도 RemixIcons 전환 후 검증 필요.
- **관련 파일**: `MainWindow.xaml` (drive template), `DriveItem.cs`, `FileSystemService.cs`

## Bug 5: 밀러 컬럼 경로 하이라이트 — 배경색 미표시
- **증상**: 밀러 컬럼에서 폴더 선택 시 부모 컬럼의 선택된 항목에 연한 배경색(AccentDim)이 표시되지 않음
- **기대 동작**: 현재 경로 상의 부모 컬럼 폴더에 연한 파란 배경(`SpanAccentDimBrush`)으로 경로 시각화
- **관련 파일**: `FileSystemViewModel.cs` (PathBackground, IsOnPath), `ExplorerViewModel.cs` (UpdatePathHighlights), `MainWindow.xaml` (Background="{x:Bind PathBackground}")
- **시도한 방법**:
  1. x:Bind 함수 바인딩 `PathHighlightBrush(IsOnPath)` → WinUI3에서 파라미터 변경 추적 실패
  2. 계산 프로퍼티 + `[NotifyPropertyChangedFor]` → PropertyChanged 발생했으나 UI 미반영
  3. `[ObservableProperty] Brush _pathBackground` 직접 설정 → UI 미반영
- **추정 원인**: WinUI3 ListView DataTemplate 내 x:Bind OneWay가 ObservableCollection 아이템의 PropertyChanged를 정상 구독하지 않을 가능성. 또는 ListView ItemContainerStyle이 DataTemplate 내부 Grid Background를 덮어쓸 가능성. 런타임 디버거(Visual Studio Live Visual Tree)로 실제 바인딩 상태 확인 필요.

## Bug 6: Rubber-band 선택 — 아이템 패딩 영역에서 시작 안 됨 (런타임 검증 필요)
- **증상**: 밀러 컬럼이 아이템으로 가득 차고 스크롤이 있는 경우, 빈 공간이 없어 러버밴드(마퀴) 멀티 선택을 시작할 수 없음
- **기대 동작**: 아이템 행의 텍스트/아이콘이 없는 부위(파일명 오른쪽 빈 영역, Grid 패딩)에서 드래그하면 러버밴드 선택이 시작되어야 함 (Windows 탐색기 동작 참고)
- **수정 적용**: `IsPointerOnItemContent()` — OriginalSource가 TextBlock/FontIcon/Image면 아이템 콘텐츠, Grid/ListViewItem이면 패딩(러버밴드 허용). WinUI TextBlock은 Background 속성이 없어 텍스트 글리프 영역만 히트 테스트됨.
- **검증 필요**: WinUI3에서 stretched TextBlock의 빈 영역이 실제로 hit-test transparent한지 런타임 확인 필요. 만약 안 되면 item template 수정 접근 필요.

## Bug 7: 탭 전환 시 이전 탭 멀티 선택 해제 여부 (Finder 검증 후 결정)
- **증상**: 탭 1,2,3,4가 있을 때 3번 탭에서 멀티 선택 후 4번 탭으로 전환하면, 3번 탭의 멀티 선택이 유지됨
- **기대 동작**: 미정 — macOS Finder의 탭 전환 시 선택 동작을 확인 후 결정
  - Finder가 탭별 선택 유지 → 현재 동작 유지
  - Finder가 탭 전환 시 선택 해제 → 동일하게 구현
- **관련 파일**: `MainWindow.xaml.cs` (탭 전환 핸들러), `FolderViewModel.cs` (SelectedItems)
