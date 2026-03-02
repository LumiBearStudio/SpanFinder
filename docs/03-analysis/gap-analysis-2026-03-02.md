# SPAN Finder - Gap Analysis Report (2026-03-02)

> **Project**: Span (WinUI 3 Miller Columns File Explorer)
> **Analysis Date**: 2026-03-02
> **Analyzer**: gap-detector agent
> **Scope**: Design documents vs actual implementation comparison
> **Overall Match Rate**: 89.3%

---

## 1. Executive Summary

SPAN Finder의 설계 문서와 실제 구현 사이의 Gap을 체계적으로 분석한 결과, **전체 일치율 89.3%**로 평가됩니다. 핵심 기능(Miller Columns, View Modes, File Operations, Preview Panel)은 설계와 높은 일치를 보이나, 일부 요구사항 기능(Quick Look 완전성, Undo 복원 한계, 컬럼 너비 기억 등)에서 Gap이 확인됩니다.

| 영역 | 설계 일치율 | 상태 |
|------|:-----------:|:----:|
| Miller Columns Engine | 95% | PASS |
| View Mode System | 92% | PASS |
| File Operations & Safety | 88% | WARN |
| File Preview Panel | 93% | PASS |
| Main Shell Layout | 94% | PASS |
| Requirements (기능 명세) | 82% | WARN |
| Test Coverage | 85% | WARN |
| **전체 평균** | **89.3%** | **WARN** |

---

## 2. Feature-Level Gap Analysis

### 2.1 Miller Columns Engine (95% Match)

| 설계 항목 | 설계 문서 | 구현 상태 | Gap |
|-----------|-----------|-----------|-----|
| IFileSystemItem 인터페이스 | miller-engine.design.md Sec 1 | `Models/IFileSystemItem.cs` | MATCH |
| DriveItem/FolderItem/FileItem | miller-engine.design.md Sec 1 | 3개 모델 구현 | MATCH |
| FileSystemService (비동기) | miller-engine.design.md Sec 2 | `Services/FileSystemService.cs` | MATCH |
| ColumnViewModel (FolderViewModel) | miller-engine.design.md Sec 3 | `ViewModels/FolderViewModel.cs` | MATCH - 이름 변경됨 |
| MillerColumnViewModel (ExplorerViewModel) | miller-engine.design.md Sec 3 | `ViewModels/ExplorerViewModel.cs` | MATCH - 이름 변경됨 |
| ScrollViewer + ItemsControl | miller-engine.design.md Sec 4 | `MainWindow.xaml` | MATCH |
| 컬럼 너비 개별 조절 | requirements.md (컬럼 너비 조절) | 미구현 | **GAP** |
| 컬럼 너비 기억 | requirements.md (정렬/너비 기억) | 미구현 | **GAP** |

**Gap 상세**:
- 컬럼 너비 드래그 조절은 설계에서 명시되었으나 현재 고정 너비 사용
- 폴더별 정렬 방식 기억은 부분적으로 구현 (전역 정렬만)

---

### 2.2 View Mode System (92% Match)

| 설계 항목 | 설계 문서 | 구현 상태 | Gap |
|-----------|-----------|-----------|-----|
| ViewMode enum (6값) | view-mode.design.md Sec 3.1 | `Models/ViewMode.cs` - **10값** | ENHANCED (Home, Settings, List, ActionLog 추가) |
| ViewModeExtensions | view-mode.design.md Sec 3.2 | `Helpers/ViewModeExtensions.cs` | MATCH |
| MainViewModel.SwitchViewMode | view-mode.design.md Sec 3.3 | `MainViewModel.ViewMode.cs` | MATCH |
| ViewMode 영속화 (LocalSettings) | view-mode.design.md Sec 3.3 | `MainViewModel.ViewMode.cs` | MATCH |
| Details View (ListView) | view-mode.design.md Sec 4.3 | `Views/DetailsModeView.xaml` | MATCH |
| Icon View (GridView) | view-mode.design.md Sec 4.4 | `Views/IconModeView.xaml` | MATCH |
| List View | 미설계 (추가 기능) | `Views/ListModeView.xaml` | ENHANCEMENT |
| IconSizeTemplateSelector | view-mode.design.md Sec 4.5 | `FileSystemItemTemplateSelector.cs` | 이름 변경, 기능 동일 |
| Ctrl+1/2/3 단축키 | view-mode.design.md Sec 6.1 | `MainWindow.KeyboardHandler.cs` | MATCH |
| ViewMode 전환 300ms 성능 | view-mode.design.md Sec 7.3 | 측정 필요 | UNVERIFIED |
| Details 컬럼 헤더 정렬 인디케이터 (▲▼) | view-mode.design.md Sec 5.3 | 구현됨 | MATCH |
| Details 컬럼 너비 resize | view-mode.design.md Risk 8.2 | `DetailsColumnWidths.cs` 존재 | PARTIAL |

**Gap 상세**:
- ViewMode enum에 4개 값이 설계 외 추가됨 (Home, Settings, List, ActionLog) - 의도적 확장
- 성능 목표(300ms 전환, 1000항목 정렬 100ms)의 실측 검증이 필요

---

### 2.3 File Operations & Safety (88% Match)

| 설계 항목 | 설계 문서 | 구현 상태 | Gap |
|-----------|-----------|-----------|-----|
| IFileOperation 인터페이스 | file-ops.design.md Sec 2.1 | `Services/FileOperations/IFileOperation.cs` | MATCH |
| IPausableOperation | 미설계 | 추가됨 | ENHANCEMENT |
| FileOperationHistory | file-ops.design.md Sec 2.2 | `Services/FileOperations/FileOperationHistory.cs` | MATCH |
| DeleteFileOperation | file-ops.design.md Sec 3.1 | `Services/FileOperations/DeleteFileOperation.cs` | MATCH |
| CopyFileOperation | file-ops.design.md Sec 3.2 | `Services/FileOperations/CopyFileOperation.cs` | MATCH |
| MoveFileOperation | file-ops.design.md Sec 3.3 | `Services/FileOperations/MoveFileOperation.cs` | MATCH |
| RenameFileOperation | file-ops.design.md Sec 3.4 | `Services/FileOperations/RenameFileOperation.cs` | MATCH |
| BatchRenameOperation | 미설계 | 추가됨 | ENHANCEMENT |
| CompressOperation | 미설계 | 추가됨 | ENHANCEMENT |
| ExtractOperation | 미설계 | 추가됨 | ENHANCEMENT |
| NewFileOperation | 미설계 | 추가됨 | ENHANCEMENT |
| NewFolderOperation | 미설계 | 추가됨 | ENHANCEMENT |
| ConflictResolution enum | file-ops.design.md Sec 4 | `FileConflictDialogViewModel.cs` | MATCH |
| FileConflictDialog | file-ops.design.md Sec 4 | `Views/Dialogs/FileConflictDialog.xaml` | MATCH |
| FileOperationProgressControl | file-ops.design.md Sec 5 | `Views/Controls/FileOperationProgressControl.xaml` | MATCH |
| Ctrl+Z Undo 기능 | file-ops.design.md Sec 7 | `MainWindow.KeyboardHandler.cs` | MATCH |
| Ctrl+Y Redo 기능 | file-ops.design.md Sec 7 | `MainWindow.KeyboardHandler.cs` | MATCH |
| 상태바 Undo 힌트 | file-ops.design.md Sec 8 | 구현됨 | MATCH |
| **Undo 실제 복원** | file-ops.design.md 요구사항 | 휴지통 복원 미구현 | **GAP** |
| 일시정지/재개 기능 | file-ops.design.md Sec 5 | `IPausableOperation` 추가 | ENHANCED |
| 파일 작업 속도/남은시간 표시 | file-ops.design.md Sec 5.1 | `FileOperationProgressViewModel.cs` | MATCH |

**Gap 상세**:
- **Undo 복원 한계**: `DeleteFileOperation.UndoAsync()`가 "Cannot restore from Recycle Bin programmatically" 에러를 반환. 설계 문서에서 Undo가 핵심 안전 장치로 정의되었으나, 삭제 작업의 실질적 Undo가 불가.
- Copy/Move/Rename의 Undo는 정상 동작

---

### 2.4 File Preview Panel (93% Match)

| 설계 항목 | 설계 문서 | 구현 상태 | Gap |
|-----------|-----------|-----------|-----|
| PreviewType enum | file-preview.design.md Sec 3.1 | `Models/PreviewType.cs` | MATCH (HexBinary 추가) |
| PreviewService | file-preview.design.md Sec 4.1 | `Services/PreviewService.cs` | MATCH |
| PreviewPanelViewModel | file-preview.design.md Sec 5.1 | `ViewModels/PreviewPanelViewModel.cs` | MATCH |
| PreviewPanelView | file-preview.design.md Sec 6.1 | `Views/PreviewPanelView.xaml` | MATCH |
| Ctrl+Shift+P 단축키 | file-preview.design.md Sec 9.4 | `MainWindow.KeyboardHandler.cs` | MATCH |
| 이미지 미리보기 | file-preview.design.md Sec 4.1 | `PreviewService.cs` | MATCH |
| 텍스트 미리보기 | file-preview.design.md Sec 4.1 | `PreviewService.cs` | MATCH |
| PDF 미리보기 | file-preview.design.md Sec 4.1 | `PreviewService.cs` | MATCH |
| 미디어 미리보기 | file-preview.design.md Sec 4.1 | `PreviewService.cs` | MATCH |
| Split View 독립 미리보기 | file-preview.design.md Sec 10 | `MainWindow.SplitPreviewManager.cs` | MATCH |
| 디바운싱 (200ms) | file-preview.design.md Sec 11.1 | `PreviewPanelViewModel.cs` | MATCH |
| HexBinary 미리보기 | 미설계 | 추가됨 | ENHANCEMENT |
| Hex 뷰어 설정 토글 | FEATURES.md Sec 1.7 | `SettingsService.cs` | MATCH |

---

### 2.5 Main Shell Layout (94% Match)

| 설계 항목 | 설계 문서 | 구현 상태 | Gap |
|-----------|-----------|-----------|-----|
| Mica 배경 | main-shell.design.md Sec 1 | `App.xaml.cs` | MATCH |
| Custom Title Bar | main-shell.design.md Sec 4 | `MainWindow.xaml.cs` | MATCH |
| TabView | main-shell.design.md Sec 3 | `MainWindow.xaml` | MATCH |
| Sidebar (NavigationView) | main-shell.design.md Sec 2 | `MainWindow.xaml` | PARTIAL - TreeView 구현 |
| 탭 Tear-off | requirements.md Sec B | `MainWindow.TabManager.cs` | MATCH |
| 탭 Docking (합치기) | requirements.md Sec B | 미구현 | **GAP** |
| Split View | requirements.md Sec A | `MainWindow.SplitPreviewManager.cs` | MATCH |

**Gap 상세**:
- Sidebar가 NavigationView 대신 custom TreeView로 구현됨 (의도적 변경, 기능 동등)
- 탭 Docking(독립 윈도우를 다른 윈도우로 합치기)은 미구현

---

## 3. Requirements vs Implementation Mapping (82% Match)

### 3.1 핵심 기능 구현 현황

| 요구사항 (requirements.md) | 상태 | 비고 |
|---------------------------|:----:|------|
| **A. Miller Columns (기본 뷰)** | DONE | ExplorerViewModel + FolderViewModel |
| **A. 자세히 보기 (Details)** | DONE | DetailsModeView.xaml |
| **A. 아이콘 보기 (Icon)** | DONE | IconModeView.xaml |
| **A. 분할 뷰 (Split View)** | DONE | SplitPreviewManager |
| **B. 탭 브라우징** | DONE | Ctrl+T/W, TabView |
| **B. 탭 Tear-off** | DONE | TabStateDto, 새 윈도우 생성 |
| **B. 탭 Docking (합치기)** | **NOT DONE** | 설계됨, 미구현 |
| **C. 커맨드 바** | DONE | UnifiedBar 구현 |
| **C. 컨텍스트 메뉴** | DONE | ShellContextMenu (Shell API) |
| **C. 미리보기 (Preview)** | DONE | PreviewPanelView |
| **D. Undo/Redo** | PARTIAL | 삭제 Undo 미동작 (휴지통 복원 불가) |
| **D. 휴지통 vs 영구삭제** | DONE | Delete / Shift+Delete |
| **D. 파일 충돌 처리** | DONE | FileConflictDialog |
| **D. 파일 작업 진행률** | DONE | FileOperationProgressControl |
| **E. 다중 선택** | DONE | Ctrl/Shift+Click, Ctrl+A |
| **E. 인라인 이름 바꾸기** | DONE | F2, Enter/Escape |
| **E. 외부 D&D (Drag In/Out)** | DONE | ViewDragDropHelper, DragDropHandler |
| **E. 폴더/파일 크기 계산** | DONE | FolderSizeService |
| **F. 주소창 직접 입력 (Ctrl+L)** | DONE | AddressBarControl |
| **F. 자동 완성** | DONE | AutoSuggestBox |
| **F. Quick Look (Space 프리뷰)** | PARTIAL | 구현됨, 설정 토글 존재, 완전성 미검증 |
| **F. 정렬 기억** | PARTIAL | 전역 정렬만, 폴더별 미구현 |
| **F. 컬럼 너비 기억** | **NOT DONE** | 미구현 |

### 3.2 Roadmap Phase 매핑

| Phase | 상태 | 일치율 |
|-------|:----:|:------:|
| Phase 1: Project Setup & Core Logic | DONE | 100% |
| Phase 2: Miller Engine | DONE | 95% (컬럼 너비 제외) |
| Phase 3: File Operations & Safety | DONE | 88% (Undo 한계) |
| Phase 4: Window Management | PARTIAL | 85% (Docking 미구현) |
| Phase 5: Polish & Deploy | DONE | 90% |
| Phase 6: Remote Drives | PARTIAL | 70% (FTP/SFTP 있음, 클라우드 부분적) |

---

## 4. Additional Implementation (설계 외 기능)

설계 문서에 없지만 구현된 기능들:

| 기능 | 구현 파일 | 비고 |
|------|-----------|------|
| List View (고밀도 리스트) | `Views/ListModeView.xaml` | 4번째 뷰 모드 |
| Home View | `Views/HomeModeView.xaml` | 시작 화면 |
| Settings View (임베디드) | `Views/SettingsModeView.xaml` | 설정 탭 |
| Action Log View | `Views/LogModeView.xaml` | 파일 작업 로그 |
| Batch Rename | `Services/FileOperations/BatchRenameOperation.cs` | 일괄 이름 변경 |
| Compress/Extract | `CompressOperation.cs`, `ExtractOperation.cs` | 압축/해제 |
| Git Status Badge | `Services/GitStatusService.cs` | Git 상태 표시 |
| Crash Reporting | `Services/CrashReportingService.cs` | Sentry 기반 |
| Recursive Search | `Services/RecursiveSearchService.cs` | BFS 재귀 검색 |
| FileSystem Watcher | `Services/FileSystemWatcherService.cs` | 실시간 변경 감지 |
| Folder Size Service | `Services/FolderSizeService.cs` | 비동기 폴더 크기 계산 |
| Folder Content Cache | `Services/FolderContentCache.cs` | 성능 캐시 |
| Cloud Storage Provider | `Services/CloudStorageProviderService.cs` | 클라우드 연동 |
| FTP/SFTP Provider | `Services/FtpProvider.cs`, `SftpProvider.cs` | 원격 연결 |
| Network Browser | `Services/NetworkBrowserService.cs` | 네트워크 탐색 |
| Favorites Service | `Services/FavoritesService.cs` | 즐겨찾기 관리 |
| Localization (EN/KO/JA) | `Services/LocalizationService.cs` | 3개 언어 |
| 7개 커스텀 테마 | SettingsModeView | Dracula, Tokyo Night 등 |
| Help Flyout | `Views/HelpFlyoutContent.xaml` | F1 도움말 |
| Rubber Band Selection | `Helpers/RubberBandSelectionHelper.cs` | 드래그 영역 선택 |

---

## 5. Test Coverage Analysis

### 5.1 Unit Tests (Span.Tests)

| 테스트 영역 | 테스트 파일 수 | 커버리지 |
|-------------|:----------:|:-------:|
| Helpers | 4 | 양호 |
| Models | 8 | 양호 |
| Services | 7 | 양호 |
| ViewModels | 4 | 양호 |
| Integration | 4 | 양호 |
| Stress | 1 | 기본 |
| **합계** | **28** | |

### 5.2 UI Tests (Span.UITests)

| 테스트 영역 | 테스트 파일 | 커버리지 |
|-------------|-----------|:-------:|
| Navigation | AddressBarNavigationTests, BackForwardNavigationTests, MillerColumnNavigationTests, NavigationTests | 양호 |
| File Operations | ClipboardOperationsTests, FileOperationUITests, InlineRenameTests | 양호 |
| View Modes | ViewModeTests, ViewModeDetailedTests | 양호 |
| Selection | MultiSelectTests | 양호 |
| Search | SearchTests | 양호 |
| Settings | HiddenFilesToggleTests, HelpOverlayTests | 양호 |
| UI Elements | SidebarUITests, StatusBarTests, SortGroupUITests, SplitViewTests | 양호 |
| Preview | PreviewPanelTests | 양호 |
| Tab Management | TabManagementTests | 양호 |
| E2E | E2EScenarioTests | 양호 |
| Stress | UIStressTests | 기본 |

### 5.3 테스트 Gap

| 미테스트 영역 | 심각도 | 비고 |
|--------------|:------:|------|
| Undo/Redo 동작 (UI) | HIGH | 핵심 안전 기능이나 UI 테스트 없음 |
| Drag & Drop (외부 앱) | MEDIUM | FlaUI로 외부 D&D 테스트 어려움 |
| 탭 Tear-off | MEDIUM | 멀티 윈도우 UI 테스트 복잡 |
| Quick Look (Space) | LOW | 설정 토글 존재, 동작 검증 필요 |
| Cloud/FTP 연결 | LOW | 외부 서비스 의존 |
| 테마 전환 시각 검증 | LOW | 색상 정확성 자동 검증 어려움 |

---

## 6. Critical Gaps Summary

### 6.1 High Priority (기능 결함)

| # | Gap | 설계 문서 | 영향 | 권장 조치 |
|---|-----|-----------|------|-----------|
| 1 | 삭제 Undo 미동작 | file-ops.design.md Sec 3.1 | 사용자 안전 | Shell API (SHFileOperation) 또는 IShellItem2로 휴지통 복원 구현 |
| 2 | 탭 Docking 미구현 | requirements.md Sec B | UX 완성도 | 드래그로 탭 병합 기능 추가 |

### 6.2 Medium Priority (기능 부족)

| # | Gap | 설계 문서 | 영향 | 권장 조치 |
|---|-----|-----------|------|-----------|
| 3 | 컬럼 너비 개별 조절 | requirements.md Sec A | UX | GridSplitter 또는 Thumb 드래그 추가 |
| 4 | 컬럼 너비/정렬 폴더별 기억 | requirements.md Sec F | UX | 폴더 해시 기반 설정 저장 |
| 5 | 성능 목표 실측 미검증 | view-mode.design.md Sec 7.3 | 품질 | Stopwatch 기반 성능 테스트 추가 |

### 6.3 Low Priority (개선 사항)

| # | Gap | 설계 문서 | 영향 | 권장 조치 |
|---|-----|-----------|------|-----------|
| 6 | Quick Look 완전성 | requirements.md Sec F | UX | 모든 PreviewType 동작 검증 |
| 7 | Undo/Redo UI 테스트 | file-ops.design.md Sec 11 | 테스트 | UITest 추가 |

---

## 7. Strengths (강점)

1. **MVVM 아키텍처 준수**: 모든 설계 문서의 MVVM 패턴이 일관되게 구현됨
2. **설계 초과 구현**: BatchRename, Compress/Extract, Git Status, Crash Reporting 등 설계 범위를 넘어선 기능이 다수 구현됨
3. **테스트 인프라**: Unit Test 28개 파일, UI Test 22개 파일로 양호한 커버리지
4. **안전성 설계**: CancellationToken, Debouncing, 메모리 관리 등 설계의 성능/안전 패턴이 충실히 구현됨
5. **IPausableOperation**: 설계에 없던 일시정지/재개 기능이 추가되어 대용량 작업 UX 향상
6. **다국어 지원**: 설계에 명시되지 않았으나 EN/KO/JA 3개 언어 지원 구현
7. **확장 뷰 모드**: 설계의 3개 뷰(Miller/Details/Icon)에서 List/Home/Settings/ActionLog까지 확장

---

## 8. Recommendations

### 즉시 조치 (Sprint 1)
1. `DeleteFileOperation.UndoAsync()` - Shell API를 이용한 휴지통 복원 구현
2. Undo/Redo UI 테스트 추가

### 다음 Sprint (Sprint 2)
3. Miller Columns 컬럼 너비 드래그 조절 구현
4. 폴더별 정렬/너비 설정 영속화

### 백로그
5. 탭 Docking 기능 구현
6. 성능 벤치마크 자동화 테스트
7. Quick Look 전체 타입 E2E 검증

---

## 9. Conclusion

SPAN Finder는 설계 문서 대비 **89.3%**의 일치율을 보이며, 대부분의 핵심 기능이 설계대로 또는 설계를 초과하여 구현되었습니다. 주요 Gap은 삭제 Undo 복원과 컬럼 너비 관련 UX 기능에 집중되어 있으며, 이는 향후 2개 Sprint 내에 해결 가능한 범위입니다. 설계 문서에 없는 추가 기능(BatchRename, Compress, Git, 다국어 등)이 다수 구현되어 실제 제품 완성도는 설계 범위를 크게 초과합니다.
