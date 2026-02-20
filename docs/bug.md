# Known Bugs & Issues

## Open Issues

### BUG-001: Sidebar Favorites — Icon rendering incorrect
- **Severity**: Medium
- **Status**: In Progress
- **Description**: TreeView 내 FontIcon의 FontFamily가 제대로 적용되지 않아 사이드바 즐겨찾기 아이콘이 엉뚱한 글리프로 표시됨. TextBlock 전환 시도 중이나 아직 미확인.
- **Root Cause**: WinUI 3 TreeView DataTemplate 내에서 FontIcon.FontFamily 적용 문제. RemixIcons 폰트 글리프(`\uEEA7`)가 FontIcon에서 제대로 렌더링 안 됨.
- **Attempted Fixes**:
  1. Segoe Fluent Icons 폰트 + `\uED41` 글리프 → 빈 사각형
  2. RemixIcons FontIcon → 엉뚱한 글리프
  3. TextBlock + 직접 폰트 경로 → 테스트 필요
- **Files**: `MainWindow.xaml` (TreeView template), `FavoritesService.cs`, `SidebarFolderNode.cs`

### BUG-002: Sidebar Drives — Icon rendering incorrect
- **Severity**: Medium
- **Status**: In Progress
- **Description**: BUG-001과 동일 원인. 드라이브 아이콘도 RemixIcons 전환 후 검증 필요.
- **Files**: `MainWindow.xaml` (drive template), `DriveItem.cs`, `FileSystemService.cs`

### BUG-003: F2 Rename — Cycling issue
- **Severity**: Low
- **Status**: Open
- **Description**: F2를 연속으로 누르면 rename 상태가 토글되는 대신 다음 아이템으로 이동하는 문제.
- **Files**: `MainWindow.xaml.cs` (HandleRename)

### BUG-004: Rename cancel on same-tab focus loss
- **Severity**: Low
- **Status**: Open
- **Description**: 인라인 rename 중 같은 탭 내 다른 영역 클릭 시 rename이 취소되지 않는 경우.
- **Files**: `MainWindow.xaml.cs` (OnRenameTextBoxLostFocus)

## Resolved Issues

_(None yet)_
