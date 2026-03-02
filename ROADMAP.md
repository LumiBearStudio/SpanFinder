# SPAN Finder Roadmap

> Windows용 고성능 Miller Columns 파일 탐색기
> 최종 업데이트: 2026-03-02

---

## Current Status: v1.0 배포 준비

**구현 완료: 120+ 기능** | **안정화: 15/18 이슈 해결** | **9개 언어 지원**

---

## v1.0 구현 완료 기능 요약

### Core (완료)
- [x] Miller Columns 엔진 (다단 계층 탐색, 컬럼 폭 조절, 자동 스크롤)
- [x] Details / List / Icon / Home 뷰 모드 (4종 + Home/Settings 특수 모드)
- [x] 주소 표시줄 (Breadcrumb + 편집 모드 + 자동완성)
- [x] Back/Forward 히스토리 (최대 50개, 드롭다운 목록)
- [x] 80+ 키보드 단축키 (글로벌 + Miller 전용 + 뷰 모드별)

### File Operations (완료)
- [x] 복사/이동/삭제 (진행률, 일시정지/재개/취소, 병렬 실행)
- [x] 배치 이름 변경 (찾기/바꾸기 + 정규식, 접두사/접미사, 번호 매기기)
- [x] 인라인 이름 변경 (F2 사이클: 파일명→전체→확장자)
- [x] ZIP 압축/해제, 파일 복제, 바로가기 생성
- [x] 실행 취소/다시 실행 (최대 50개)
- [x] 충돌 해결 (자동 접미사 " (n)")

### Navigation & Search (완료)
- [x] Type-Ahead 검색 (800ms 버퍼)
- [x] 재귀 검색 (BFS, Channel 기반, 최대 10,000 결과)
- [x] 와일드카드 검색 (*.exe, test?.doc)
- [x] 고급 쿼리 (kind:, size:, date:, ext: 필터)
- [x] 실시간 필터 바 (Ctrl+Shift+F, 와일드카드)

### Multi-Tab & Window (완료)
- [x] 다중 탭 (독립 히스토리/뷰 모드)
- [x] 탭 Tear-Off (새 창 분리)
- [x] 세션 자동 저장/복원
- [x] 멀티 윈도우 (App.RegisterWindow 관리)

### Sidebar (완료)
- [x] 로컬/네트워크/클라우드 드라이브 (자동 감지)
- [x] USB 핫플러그 (WM_DEVICECHANGE)
- [x] 즐겨찾기 (Quick Access 동기화, 드래그 리오더)
- [x] 최근 폴더 (최대 20개)
- [x] 저장된 원격 연결

### Drag & Drop (완료)
- [x] 내부/외부 드래그 앤 드롭
- [x] 분할 뷰 간 이동/복사
- [x] Spring-loaded 폴더 (800ms 호버 자동 열림)
- [x] StorageItems 지연 로딩 (외부 앱 호환)

### Split View & Preview (완료)
- [x] 듀얼 패널 독립 탐색/뷰 모드
- [x] 미리보기 패널 (이미지, 비디오, 오디오, PDF, 텍스트, 폰트, Hex)
- [x] Quick Look (Space 키 모달)
- [x] FTP/SFTP 원격 파일 미리보기

### Shell Integration (완료)
- [x] Windows Shell 네이티브 컨텍스트 메뉴 100% 지원
- [x] Shell Verb 다국어 번역 (KO, JA, DE, ES, FR, PT)
- [x] 파일 속성 다이얼로그 (ShellExecuteEx)
- [x] 터미널 열기 (PowerShell/CMD/사용자 정의)

### Cloud & Git (완료)
- [x] Cloud Files API 동기화 상태 배지 (OneDrive, iCloud, Dropbox)
- [x] 클라우드 스토리지 자동 감지 (SyncRootManager + NavigationPane + 직접)
- [x] Git 파일 상태 배지 (M/A/D/?)
- [x] Git 브랜치 정보 + 최근 커밋 5개

### Remote File Systems (완료)
- [x] FTP/FTPS (FluentFTP, TOFU 인증서)
- [x] SFTP (SSH.NET, 키/비밀번호 인증)
- [x] SMB 네트워크 브라우저 (WNetEnumResource)

### Settings & Localization (완료)
- [x] 7종 테마 (Light/Dark/System/Dracula/Tokyo Night/Catppuccin/Gruvbox)
- [x] 3종 아이콘 팩 (Remix/Phosphor/Tabler)
- [x] 레이아웃 밀도 (Compact/Comfortable/Spacious)
- [x] 9개 언어 (EN, KO, JA, ZH-CN, ZH-TW, DE, ES, FR, PT-BR)
- [x] 설정 자동 저장/복원 + 손상 감지 복구

### File System Monitoring (완료)
- [x] FileSystemWatcher 실시간 변경 감지 (300ms 디바운스)
- [x] 폴더 크기 백그라운드 계산 (8단계 깊이, 캐시)
- [x] 폴더 컨텐츠 캐시 (LRU 500개)

### Reliability (완료)
- [x] Sentry 크래시 리포팅 (비동기 초기화, 경로 스크러빙)
- [x] async void 전수 try-catch 보호 (14개 메서드)
- [x] 이벤트 핸들러 누적 방지 (-= before += 패턴)
- [x] DispatcherQueue 스레드 안전성 확보
- [x] 파일 I/O 빈 catch 블록 로깅 추가
- [x] CopyDirectoryRecursive 에러 처리 강화
- [x] Dead code 정리

---

## v1.0 안정화 작업 내역 (2026-03-02)

### Critical (3/3 완료)
| # | 이슈 | 파일 | 상태 |
|---|------|------|------|
| C1 | async void NavigateIntoFolder 크래시 | ExplorerViewModel.cs | **해결** |
| C2 | 빈 catch 블록으로 파일 작업 실패 무시 | FileOperationHandler.cs | **해결** |
| C3 | PropertyChanged 이벤트 다중 구독 메모리 누수 | SplitPreviewManager.cs | **해결** |

### High (5/5 완료)
| # | 이슈 | 파일 | 상태 |
|---|------|------|------|
| H1 | ContinueWith(ThreadPool) 포커스 유실 | KeyboardHandler.cs | **해결** |
| H2 | fire-and-forget Task.Delay 예외 무시 | FileOperationManager.cs | **해결** |
| H3 | Columns 컬렉션 수정 중 반복 보호 | ExplorerViewModel.cs | **기존 가드 확인** |
| H4 | LoadError 이벤트 다중 구독 | ExplorerViewModel.cs | **해결** |
| H5 | CopyDirectory 재귀 에러 처리 | FileOperationHandler.cs | **해결** |

### Medium (4/6 완료)
| # | 이슈 | 파일 | 상태 |
|---|------|------|------|
| M1 | DispatcherQueue null 체크 | FolderViewModel.cs | **해결** |
| M2 | ScrollViewer 이벤트 정리 | TabManager.cs | 확인 필요 |
| M3 | CloudStorage 빈 catch 로깅 | CloudStorageProviderService.cs | **해결** |
| M4 | Long path (>260자) 보호 | FileOperationHandler.cs | **부분 적용** |

### 추가 안정화 (세션 2)
| 작업 | 범위 | 상태 |
|------|------|------|
| async void try-catch 보호 | 14개 메서드 (7개 파일) | **완료** |
| 파일 I/O 빈 catch 로깅 | 5곳 (RecursiveSearch, DragDrop 등) | **완료** |
| CopyDirectory dead code 삭제 | FileOperationHandler.cs | **완료** |
| CreateDirectory try-catch | FileOperationHandler, LocalFileSystemProvider | **완료** |

### 추가 안정화 (세션 3) — 2차 전체 감사
| 작업 | 범위 | 상태 |
|------|------|------|
| FolderVm_PropertyChanged async void 보호 | ExplorerViewModel.cs | **완료** |
| HandleNewFolder/Refresh/Delete/PermanentDelete 보호 | FileOperationHandler.cs (4개 메서드) | **완료** |
| ObservableCollection UI 스레드 안전화 | FileOperationManager.cs | **완료** |
| Timer 콜백 DispatcherQueue 마샬링 | MainViewModel.FileOperations.cs (ShowToast) | **완료** |
| Git 프로세스 stdout/stderr 데드락 수정 | GitStatusService.cs | **완료** |
| STA 스레드 세마포어 스로틀링 | ShellContextMenu.cs | **완료** |
| CollectionChanged 핸들러 누수 수정 | FileOperationProgressControl.xaml.cs | **완료** |
| Dictionary → ConcurrentDictionary | FileSystemRouter.cs | **완료** |
| FocusColumnAsync try-catch 보호 | NavigationManager.cs | **완료** |
| HandlePaste/PasteAsShortcut try-catch 보호 | FileOperationHandler.cs | **완료** |
| 사이드바 이벤트 핸들러 보호 (4개) | MainWindow.xaml.cs | **완료** |
| View Loaded 빈 catch 로깅 (8개) | Home/Icon/Details/ListModeView | **완료** |

---

## v1.1+ 미래 로드맵

### Phase 1: 파일 관리 고급 기능

#### 파일 태깅 / 컬러 레이블
- 파일/폴더에 사용자 정의 태그 부착 (업무, 개인, 중요 등)
- 7가지 컬러 레이블 (빨강, 주황, 노랑, 초록, 파랑, 보라, 회색)
- 태그 기반 필터링 및 검색
- NTFS Alternate Data Streams에 메타데이터 저장
- 사이드바에 태그별 가상 폴더

#### 중복 파일 찾기
- 선택된 폴더 내 중복 파일 탐지
- 해시 기반 비교 (MD5/SHA-256)
- 크기 → 부분 해시 → 전체 해시 3단계 최적화
- 중복 그룹별 시각적 표시
- 일괄 삭제 (원본 보존 옵션)

#### 파일 해시 계산 (MD5/SHA)
- 컨텍스트 메뉴에서 해시 계산
- MD5, SHA-1, SHA-256, SHA-512 지원
- 클립보드 복사 + 해시 비교 다이얼로그

### Phase 2: 커스터마이징

#### 커스텀 단축키
- 모든 액션에 대한 단축키 재매핑
- JSON 기반 키바인딩 설정 파일
- 충돌 감지 및 경고

#### 설정 내보내기/가져오기
- JSON 형식으로 전체 설정 내보내기
- 즐겨찾기, 단축키, 테마, 레이아웃 포함
- 다른 PC로 설정 마이그레이션

### Phase 3: 확장성

#### 플러그인 시스템
- C# 기반 플러그인 API
- 커스텀 컨텍스트 메뉴, 미리보기 핸들러, 컬럼 확장
- 샌드박스 실행 환경

#### Windows Search 인덱스 연동
- Windows Search API를 통한 빠른 파일 검색
- 인덱싱된 속성 기반 고급 쿼리

### Phase 4: 접근성 및 품질

#### 접근성 (스크린 리더)
- UI Automation 패턴 완전 구현
- 키보드 전용 조작 완전 지원
- 고대비 모드 최적화

#### 성능 최적화
- 가상화 스크롤 (10만+ 파일)
- 대용량 폴더 스트리밍 로드
- 메모리 프로파일링 + 시작 시간 최적화

---

## Priority Matrix

| 기능 | 난이도 | 영향도 | 우선순위 |
|------|--------|--------|----------|
| 파일 태깅/컬러 레이블 | ★★★★ | ★★★★★ | P1 |
| 중복 파일 찾기 | ★★★ | ★★★★ | P1 |
| 커스텀 단축키 | ★★★ | ★★★★ | P1 |
| 파일 해시 | ★★ | ★★★ | P2 |
| 설정 내보내기/가져오기 | ★★ | ★★★ | P2 |
| Windows Search 연동 | ★★★★ | ★★★★ | P2 |
| 접근성 | ★★★★ | ★★★★★ | P2 |
| 플러그인 시스템 | ★★★★★ | ★★★★★ | P3 |
| 성능 최적화 | ★★★ | ★★★★ | Ongoing |
