# Span 1.0 - 전체 기능 리스트 (테스트용)

> 작성일: 2026-02-23
> 목적: 체크리스트 및 FlaUI 자동화 테스트 기반 자료

---

## A. 탭 및 윈도우 관리 (Tab & Window)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| A01 | 새 탭 생성 | Ctrl+T, 홈 모드로 열림 | MainWindow.xaml.cs |
| A02 | 탭 닫기 | Ctrl+W, 마지막 탭 보호 | MainViewModel.cs |
| A03 | 탭 전환 | 클릭으로 전환, 상태 저장/복원 | MainViewModel.cs |
| A04 | 탭 복제 | 우클릭 > 복제, 경로/뷰모드 유지 | MainViewModel.cs |
| A05 | 다른 탭 닫기 | 우클릭 > 다른 탭 닫기 | MainViewModel.cs |
| A06 | 오른쪽 탭 닫기 | 우클릭 > 오른쪽 탭 닫기 | MainViewModel.cs |
| A07 | 탭 분리 (Tear-off) | 드래그로 새 윈도우 생성 | MainWindow.xaml.cs |
| A08 | 새 창 | Ctrl+N | MainWindow.xaml.cs |
| A09 | 다중 윈도우 관리 | RegisterWindow/UnregisterWindow | App.xaml.cs |
| A10 | 세션 저장/복원 | 탭 상태 JSON 직렬화, 시작 시 복원 | MainViewModel.cs |
| A11 | 탭 헤더 업데이트 | 현재 폴더명 반영, 아이콘 표시 | MainViewModel.cs |

---

## B. 네비게이션 (Navigation)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| B01 | 뒤로 | Alt+Left, 히스토리 기반 | ExplorerViewModel.cs |
| B02 | 앞으로 | Alt+Right, 히스토리 기반 | ExplorerViewModel.cs |
| B03 | 위로 | 부모 폴더 이동 | ExplorerViewModel.cs |
| B04 | 히스토리 드롭다운 | 뒤로/앞으로 버튼 우클릭 | MainWindow.xaml.cs |
| B05 | 브레드크럼 경로 | 클릭 가능한 경로 세그먼트 | ExplorerViewModel.cs |
| B06 | 브레드크럼 드롭다운 | 하위 폴더 빠른 이동 | MainWindow.xaml |
| B07 | 주소 표시줄 편집 | Ctrl+L, 전체 경로 입력 | MainWindow.xaml.cs |
| B08 | 주소 자동완성 | AutoSuggestBox 기반 | MainWindow.xaml |
| B09 | 폴더 열기 (더블클릭) | 파일 실행, 폴더 진입 | MainWindow.xaml.cs |
| B10 | 폴더 열기 (Enter) | 파일 실행, 폴더 진입 | ListModeView.xaml.cs |
| B11 | Backspace 상위 이동 | 부모 폴더로 이동 | MainWindow.xaml.cs |
| B12 | 드라이브 열기 | 홈에서 드라이브 클릭 | MainViewModel.cs |
| B13 | 즐겨찾기 네비게이션 | 즐겨찾기 클릭으로 이동 | MainViewModel.cs |
| B14 | 새 탭에서 열기 | 폴더 우클릭 > 새 탭에서 열기 | MainWindow.xaml.cs |
| B15 | UNC 경로 탐색 | \\server\share 경로 지원 | ExplorerViewModel.cs |
| B16 | 원격 경로 탐색 | sftp://, ftp:// 프로토콜 | FileSystemRouter.cs |
| B17 | 새로고침 | F5 | MainWindow.xaml.cs |

---

## C. 뷰 모드 (View Modes)

### C1. Miller Columns (Ctrl+1)
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C1-01 | 계층 컬럼 표시 | 폴더 선택 시 다음 컬럼 생성 | ExplorerViewModel.cs |
| C1-02 | 수평 스크롤 | 컬럼 추가 시 자동 스크롤 | MainWindow.xaml.cs |
| C1-03 | 컬럼 너비 조정 | 드래그로 개별 너비 조정 | MainWindow.xaml.cs |
| C1-04 | 컬럼 너비 통일 | Ctrl+Shift+= (220px) | MainWindow.xaml.cs |
| C1-05 | 컬럼 자동 맞춤 | Ctrl+Shift+- (내용 기반) | MainWindow.xaml.cs |
| C1-06 | 단일/더블클릭 설정 | MillerClickBehavior 설정 | SettingsService.cs |
| C1-07 | 선택 전파 | 파일 선택 시 뒤 컬럼 제거 | ExplorerViewModel.cs |
| C1-08 | Type-ahead 검색 | 문자 입력으로 필터링 (800ms) | MainWindow.xaml.cs |

### C2. Details (Ctrl+2)
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C2-01 | 테이블 형식 표시 | 아이콘, 이름, 날짜, 유형, 크기 | DetailsModeView.xaml |
| C2-02 | 컬럼 헤더 정렬 | Name, Date, Type, Size 클릭 | DetailsModeView.xaml.cs |
| C2-03 | 컬럼 너비 조정 | GridSplitter | DetailsModeView.xaml |
| C2-04 | 그룹핑 | GroupStyle 지원 | DetailsModeView.xaml |

### C3. List (Ctrl+3)
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C3-01 | 세로 흐름 레이아웃 | 위→아래 채우고 다음 열 | ListModeView.xaml |
| C3-02 | Size 토글 | 파일 크기 표시/숨김 | ListModeView.xaml.cs |
| C3-03 | Date 토글 | 수정 날짜 표시/숨김 | ListModeView.xaml.cs |
| C3-04 | 열 너비 슬라이더 | 150~500px 조절 | ListModeView.xaml.cs |
| C3-05 | 설정 저장/복원 | Size/Date/Width 영속화 | SettingsService.cs |
| C3-06 | Enter 폴더 진입 | AddHandler로 키 캡처 | ListModeView.xaml.cs |
| C3-07 | F2 이름 변경 | 인라인 rename (F2 cycling) | ListModeView.xaml.cs |
| C3-08 | .. 상위 디렉토리 | 첫 번째 항목으로 표시 | ListModeView.xaml.cs |
| C3-09 | 방향키 네비게이션 | 상하좌우 이동 | ListModeView.xaml.cs |

### C4. Icon (Ctrl+4)
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C4-01 | Small (16px) | 수평 배치, 아이콘+텍스트 | IconModeView.xaml |
| C4-02 | Medium (48px) | 수평 배치, 기본값 | IconModeView.xaml |
| C4-03 | Large (96px) | 수직 배치, 아이콘 위 텍스트 | IconModeView.xaml |
| C4-04 | ExtraLarge (256px) | 수직 배치, 큰 썸네일 | IconModeView.xaml |
| C4-05 | Ctrl+마우스 휠 | 뷰 모드 순환 | MainWindow.xaml.cs |

### C5. Home
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C5-01 | 로컬 드라이브 표시 | 진행률/용량 카드 | HomeModeView.xaml |
| C5-02 | 네트워크 드라이브 | 매핑된 드라이브 표시 | HomeModeView.xaml |
| C5-03 | 즐겨찾기 그리드 | 즐겨찾기 폴더 표시 | HomeModeView.xaml |
| C5-04 | 드라이브 우클릭 | 열기, 새 탭, 해제 메뉴 | HomeModeView.xaml.cs |

### C6. Settings (Ctrl+,)
| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| C6-01 | General | 언어, 시작 동작, 즐겨찾기 트리, 트레이 | SettingsModeView.xaml |
| C6-02 | Appearance | 테마, 밀도, 아이콘팩, 폰트 | SettingsModeView.xaml |
| C6-03 | Browsing | 숨김파일, 확장자, 확인삭제 | SettingsModeView.xaml |
| C6-04 | Tools | QuickLook, 에디터, 터미널 | SettingsModeView.xaml |
| C6-05 | About | 버전, 라이선스 | SettingsModeView.xaml |

---

## D. 파일 작업 (File Operations)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| D01 | 복사 | Ctrl+C → Ctrl+V | MainWindow.xaml.cs, CopyFileOperation.cs |
| D02 | 잘라내기 | Ctrl+X → Ctrl+V | MainWindow.xaml.cs, MoveFileOperation.cs |
| D03 | 삭제 (휴지통) | Delete | DeleteFileOperation.cs |
| D04 | 영구 삭제 | Shift+Delete | DeleteFileOperation.cs |
| D05 | 이름 변경 | F2, 인라인 TextBox | FileSystemViewModel.cs |
| D06 | 새 폴더 | Ctrl+Shift+N | NewFolderOperation.cs |
| D07 | 복제 | Ctrl+D | MainWindow.xaml.cs |
| D08 | Undo | Ctrl+Z | MainViewModel.cs |
| D09 | Redo | Ctrl+Y | MainViewModel.cs |
| D10 | 배치 이름 변경 | 찾기/바꾸기, 접두사/접미사, 번호매기기 | BatchRenameOperation.cs |
| D11 | 파일 충돌 해결 | Replace/KeepBoth/Skip, ApplyToAll | FileConflictDialog.xaml |
| D12 | 작업 진행 표시 | 진행률, 속도, 남은시간 | FileOperationProgressControl.xaml |
| D13 | 작업 일시정지/재개 | ManualResetEventSlim 기반 | FileOperationManager.cs |
| D14 | 작업 취소 | CancellationToken 기반 | FileOperationManager.cs |
| D15 | 다중 동시 작업 | 복사/이동 병렬 실행 | FileOperationManager.cs |
| D16 | 압축 (ZIP) | CompressOperation | CompressOperation.cs |
| D17 | 압축 해제 | ExtractOperation | ExtractOperation.cs |

---

## E. 선택 및 드래그 (Selection & Drag)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| E01 | 전체 선택 | Ctrl+A | MainWindow.xaml.cs |
| E02 | 선택 해제 | Ctrl+Shift+A | MainWindow.xaml.cs |
| E03 | 선택 반전 | Ctrl+I | MainWindow.xaml.cs |
| E04 | Extended 선택 | Shift+클릭, Ctrl+클릭 | GridView/ListView |
| E05 | 러버밴드 선택 | 마우스 드래그 영역 선택 | RubberBandSelectionHelper.cs |
| E06 | 파일 D&D (내부) | 패널 간 교차 드래그 | MainWindow.xaml.cs |
| E07 | 파일 D&D (외부) | 탐색기 ↔ Span 간 드래그 | MainWindow.xaml.cs |
| E08 | D&D 드롭 오버레이 | 좌/우 패널 시각적 표시 | MainWindow.xaml |

---

## F. 컨텍스트 메뉴 (Context Menu)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| F01 | 아이템 우클릭 | 열기, 복사, 삭제, 이름변경 등 | ContextMenuService.cs |
| F02 | 빈 영역 우클릭 | 새 폴더, 붙여넣기 | MainWindow.xaml.cs |
| F03 | 드라이브 우클릭 | 열기, 새 탭, 해제 | HomeModeView.xaml.cs |
| F04 | 탭 우클릭 | 닫기, 복제, 다른 탭 닫기 | MainWindow.xaml.cs |
| F05 | 셸 확장 메뉴 | Windows 네이티브 메뉴 통합 | ContextMenuService.cs |
| F06 | 메뉴 지역화 | 한/영/일 번역 | LocalizationService.cs |

---

## G. 분할 뷰 및 미리보기 (Split & Preview)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| G01 | 분할 뷰 토글 | Ctrl+Shift+E | MainViewModel.cs |
| G02 | 좌/우 독립 탐색 | 독립적 주소표시줄, 네비게이션 | MainWindow.xaml |
| G03 | 패널 전환 | Ctrl+Tab | MainWindow.xaml.cs |
| G04 | 미리보기 패널 | Ctrl+Shift+P, 우측 패널 | PreviewPanelView.xaml |
| G05 | 이미지 미리보기 | jpg, png, bmp, gif, webp 등 | PreviewService.cs |
| G06 | 텍스트 미리보기 | txt, cs, json, md, py 등 (50K자) | PreviewService.cs |
| G07 | PDF 미리보기 | 첫 페이지 렌더링 | PreviewService.cs |
| G08 | 미디어 미리보기 | mp4, mp3 등 재생 컨트롤 | PreviewService.cs |
| G09 | Quick Look | Space 키 팝업 미리보기 | MainWindow.xaml.cs |
| G10 | 메타데이터 표시 | 크기, 날짜, 해상도, 아티스트 등 | PreviewPanelViewModel.cs |

---

## H. 테마 및 외관 (Theme & Appearance)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| H01 | Light/Dark/System 테마 | 자동 감지 | SettingsService.cs |
| H02 | Dracula 테마 | 보라색 다크 | App.xaml |
| H03 | Tokyo Night 테마 | 네이비 + 시안 | App.xaml |
| H04 | Catppuccin 테마 | 따뜻한 파스텔 | App.xaml |
| H05 | Gruvbox 테마 | 레트로 따뜻함 | App.xaml |
| H06 | Compact 밀도 | 항목 높이 축소 | SettingsService.cs |
| H07 | Comfortable 밀도 | 기본, 균형 | SettingsService.cs |
| H08 | Spacious 밀도 | 항목 높이 확대 | SettingsService.cs |
| H09 | 아이콘 팩 전환 | Remix, Phosphor, Tabler | IconService.cs |
| H10 | 폰트 선택 | Segoe UI Variable, Cascadia Code 등 | SettingsService.cs |
| H11 | Mica 배경 | Windows 11 자동, Win10 폴백 | MainWindow.xaml.cs |

---

## I. 키보드 단축키 (Keyboard Shortcuts)

### 네비게이션
| ID | 단축키 | 기능 |
|----|--------|------|
| I01 | ← → | Miller 컬럼 이동 |
| I02 | ↑ ↓ | 항목 이동 |
| I03 | Enter | 폴더 열기 / 파일 실행 |
| I04 | Backspace | 상위 폴더 |
| I05 | Alt+← | 뒤로 |
| I06 | Alt+→ | 앞으로 |
| I07 | Ctrl+L | 주소 표시줄 포커스 |
| I08 | Ctrl+F | 검색 포커스 |
| I09 | Space | Quick Look |

### 편집
| ID | 단축키 | 기능 |
|----|--------|------|
| I10 | Ctrl+C | 복사 |
| I11 | Ctrl+X | 잘라내기 |
| I12 | Ctrl+V | 붙여넣기 |
| I13 | Ctrl+D | 복제 |
| I14 | F2 | 이름 변경 |
| I15 | Delete | 삭제 (휴지통) |
| I16 | Shift+Del | 영구 삭제 |
| I17 | Ctrl+Shift+N | 새 폴더 |
| I18 | Ctrl+Z | 실행 취소 |
| I19 | Ctrl+Y | 다시 실행 |

### 선택
| ID | 단축키 | 기능 |
|----|--------|------|
| I20 | Ctrl+A | 전체 선택 |
| I21 | Ctrl+Shift+A | 선택 해제 |
| I22 | Ctrl+I | 선택 반전 |

### 뷰
| ID | 단축키 | 기능 |
|----|--------|------|
| I23 | Ctrl+1 | Miller Columns |
| I24 | Ctrl+2 | Details |
| I25 | Ctrl+3 | List |
| I26 | Ctrl+4 | Icon |
| I27 | Ctrl+Shift+E | 분할 뷰 |
| I28 | Ctrl+Shift+P | 미리보기 패널 |
| I29 | Ctrl+Tab | 패널 전환 |
| I30 | Ctrl+Shift+= | 컬럼 너비 통일 |
| I31 | Ctrl+Shift+- | 컬럼 자동 맞춤 |
| I32 | F5 | 새로고침 |

### 창/탭
| ID | 단축키 | 기능 |
|----|--------|------|
| I33 | Ctrl+T | 새 탭 |
| I34 | Ctrl+W | 탭 닫기 |
| I35 | Ctrl+N | 새 창 |
| I36 | Ctrl+` | 터미널 |
| I37 | Ctrl+' | 터미널 (한국어 키보드) |
| I38 | Ctrl+, | 설정 |
| I39 | Alt+Enter | 속성 |
| I40 | F1 | 도움말 |

---

## J. 네트워크 및 원격 연결 (Network & Remote)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| J01 | FTP 연결 | FTP/FTPS 프로토콜 | FtpProvider.cs |
| J02 | SFTP 연결 | SSH 기반 파일 전송 | SftpProvider.cs |
| J03 | SMB 연결 | UNC 경로 공유 폴더 | ConnectionManagerService.cs |
| J04 | 연결 저장/관리 | connections.json 저장 | ConnectionManagerService.cs |
| J05 | 자격증명 암호화 | DPAPI 암호화 저장 | ConnectionManagerService.cs |
| J06 | 네트워크 브라우저 | 로컬 네트워크 컴퓨터 검색 | NetworkBrowserService.cs |
| J07 | 공유 폴더 검색 | 서버 공유 목록 조회 | NetworkBrowserService.cs |
| J08 | 원격 파일 복사 | 로컬 ↔ 원격 양방향 | CopyFileOperation.cs |
| J09 | 재연결 로직 | 끊긴 연결 자동 복구 | FtpProvider.cs, SftpProvider.cs |

---

## K. 서비스 기능 (Services)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| K01 | 폴더 크기 계산 | 백그라운드 재귀 계산, 캐싱 | FolderSizeService.cs |
| K02 | 파일 시스템 감시 | 변경 감지 자동 새로고침 | FileSystemWatcherService.cs |
| K03 | 클라우드 상태 표시 | OneDrive 동기화 상태 뱃지 | CloudSyncService.cs |
| K04 | 폴더 내용 캐시 | 500항목 LRU, LastWriteTime 검증 | FolderContentCache.cs |
| K05 | 썸네일 로딩 | 비동기 배치 로드, 20MB 스킵 | FileViewModel.cs |
| K06 | 즐겨찾기 관리 | 추가/제거/기본값 | FavoritesService.cs |
| K07 | 작업 로그 | 작업 히스토리 기록 (1000개) | ActionLogService.cs |
| K08 | 셸 통합 | 연결프로그램, 속성, 탐색기 열기 | ShellService.cs |
| K09 | 터미널 열기 | WT/PowerShell/CMD 선택 | ShellService.cs |
| K10 | 지역화 | 영/한/일 런타임 전환 | LocalizationService.cs |
| K11 | 자연 정렬 | "file1, file2, file10" 순서 | NaturalStringComparer.cs |
| K12 | 검색 파서 | 필터/와일드카드 검색 문법 | SearchQueryParser.cs |

---

## L. 안정성 테스트 (Stability)

| ID | 테스트 항목 | 우선순위 | 관련 파일 |
|----|------------|---------|-----------|
| L01 | async void 이벤트 핸들러 예외 전파 | P0 | MainWindow.xaml.cs |
| L02 | 드래그-드롭 중 네트워크 I/O 실패 | P0 | MainWindow.xaml.cs |
| L03 | 빠른 폴더 전환 시 취소 토큰 동작 | P1 | FolderViewModel.cs |
| L04 | 탭 10개+ 빠른 열기/닫기 메모리 누수 | P1 | ExplorerViewModel.cs |
| L05 | 이벤트 핸들러 구독/해제 정합성 | P1 | ExplorerViewModel.cs |
| L06 | CancellationTokenSource Dispose | P1 | FolderViewModel.cs |
| L07 | 파일 작업 진행 중 창 닫기 | P1 | FileOperationManager.cs |
| L08 | 여러 폴더 동시 로딩 (멀티 탭) 경쟁 | P1 | FolderContentCache.cs |
| L09 | FileSystemWatcher 경로 변경 중 이벤트 | P2 | FileSystemWatcherService.cs |
| L10 | FTP/SFTP 연결 끊김 복구 | P2 | FtpProvider.cs, SftpProvider.cs |
| L11 | 폴더 크기 계산 중 빠른 종료 | P2 | FolderSizeService.cs |
| L12 | DispatcherQueue disposed UI 접근 | P2 | MainWindow.xaml.cs |
| L13 | 빈 catch 블록 예외 누락 (63개) | P2 | 다수 파일 |
| L14 | ConfigureAwait 미사용 동기화 컨텍스트 | P3 | 전체 |

---

## M. 성능 테스트 (Performance)

| ID | 테스트 항목 | 우선순위 | 관련 파일 |
|----|------------|---------|-----------|
| M01 | 100,000개 파일 폴더 열기 응답시간 | P0 | FileSystemService.cs |
| M02 | 10,000개 파일 동시 선택 UI 프리징 | P1 | FolderViewModel.cs |
| M03 | 1000개 이미지 폴더 썸네일 메모리 | P1 | FileViewModel.cs |
| M04 | 대용량 폴더 스크롤 프레임률 | P1 | GridView/ListView |
| M05 | 10,000개 작은 파일 복사 진행률 버벅임 | P1 | CopyFileOperation.cs |
| M06 | Miller Columns 50+ 레벨 깊이 | P2 | ExplorerViewModel.cs |
| M07 | 캐시 500개 초과 eviction 성능 | P2 | FolderContentCache.cs |
| M08 | 아이콘 캐시 메모리 장시간 실행 | P2 | IconService.cs |
| M09 | FTP/SFTP 대용량 파일 전송 속도 | P2 | FtpProvider.cs |
| M10 | 앱 시작 시간 (콜드 스타트) | P2 | App.xaml.cs |
| M11 | 드라이브 로딩 타임아웃 (500ms) | P2 | FileSystemService.cs |
| M12 | 폴더 크기 계산 1TB 파티션 | P3 | FolderSizeService.cs |
| M13 | 대규모 LINQ 정렬 (10,000+) | P3 | FolderViewModel.cs |

---

## N. 보안 테스트 (Security)

| ID | 테스트 항목 | 우선순위 | 관련 파일 |
|----|------------|---------|-----------|
| N01 | **OpenTerminal 명령 주입** | **P0 치명** | ShellService.cs:82-85 |
| N02 | **FTPS 인증서 검증 없음** (ValidateAnyCertificate=true) | **P0 치명** | FtpProvider.cs:41 |
| N03 | 주소표시줄 `..` 경로 조작 | P1 | ExplorerViewModel.cs |
| N04 | 드래그-드롭 경로 트래버설 | P1 | MainWindow.xaml.cs |
| N05 | 클립보드 외부 데이터 검증 | P1 | MainWindow.xaml.cs |
| N06 | System32 등 보호 폴더 접근 처리 | P2 | FileSystemService.cs |
| N07 | 260자 이상 긴 경로 처리 | P2 | 전체 |
| N08 | 순환 심볼릭 링크 무한 루프 | P2 | FolderSizeService.cs |
| N09 | SSH 키 파일 권한 검증 | P2 | SftpProvider.cs |
| N10 | 자격증명 메모리 평문 노출 | P2 | FtpProvider.cs |
| N11 | 유니코드/특수문자 파일명 | P3 | FileSystemViewModel.cs |
| N12 | null/제어문자 경로 입력 | P3 | ExplorerViewModel.cs |

---

## O. 기타 UI (Misc UI)

| ID | 기능 | 설명 | 핵심 파일 |
|----|------|------|-----------|
| O01 | 도움말 Flyout | F1, 키보드 단축키 2열 표시 | HelpFlyoutContent.xaml |
| O02 | 작업 로그 Flyout | 작업 기록, 타임스탬프, 상태 | LogFlyoutContent.xaml |
| O03 | 상태 표시줄 | 항목 수, 선택 수, 디스크 공간 | MainViewModel.cs |
| O04 | 툴팁 | 파일 정보 리치 툴팁 | FileSystemViewModel.cs |
| O05 | DPI 스케일링 | 고DPI 모니터 호환 | MainWindow.xaml.cs |
| O06 | 한국어 키보드 호환 | VK 코드 fallback 패턴 | MainWindow.xaml.cs |

---

## 통계 요약

| 카테고리 | 항목 수 |
|---------|---------|
| A. 탭/윈도우 | 11 |
| B. 네비게이션 | 17 |
| C. 뷰 모드 | 30 |
| D. 파일 작업 | 17 |
| E. 선택/드래그 | 8 |
| F. 컨텍스트 메뉴 | 6 |
| G. 분할뷰/미리보기 | 10 |
| H. 테마/외관 | 11 |
| I. 키보드 단축키 | 40 |
| J. 네트워크/원격 | 9 |
| K. 서비스 | 12 |
| L. 안정성 | 14 |
| M. 성능 | 13 |
| N. 보안 | 12 |
| O. 기타 UI | 6 |
| **합계** | **216** |
