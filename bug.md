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
