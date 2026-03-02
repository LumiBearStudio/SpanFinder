# SPAN Finder — High-Performance Miller Columns Explorer for Windows

> "Expand your view, Span your files."

SPAN Finder는 Windows에서 가장 빠르고 안정적인 Miller Columns 파일 탐색기입니다. macOS Finder의 직관적인 계층 탐색 경험을 Windows 파워 유저와 개발자에게 제공합니다.

## Highlights

- **Miller Columns**: macOS Finder 스타일의 좌우 계층 탐색 + Details/List/Icon 4종 뷰 모드
- **Zero Lag**: 14,000+ 파일 폴더도 1회 배치 업데이트, 비동기 I/O, 폴더 캐시로 즉시 로드
- **Native WinUI 3**: Mica 배경, Fluent Design, Windows 11 최적화 (Windows 10 호환)
- **Split View**: 좌우 듀얼 패널 독립 탐색, 패널간 드래그 앤 드롭
- **Multi-Tab**: 탭별 독립 히스토리, Tear-Off(새 창 분리), 세션 자동 복원
- **Full Shell Integration**: Windows Shell 네이티브 컨텍스트 메뉴 100% 지원
- **Remote Access**: FTP/FTPS, SFTP, SMB 네트워크 파일 탐색
- **Power Search**: 재귀 검색 + 와일드카드 + `kind:image size:>1MB date:thisweek` 고급 쿼리
- **Stability First**: async void 전수 보호, 이벤트 누수 방지, Sentry 크래시 리포팅

## Tech Stack

| 항목 | 스택 |
|------|------|
| **Framework** | WinUI 3 (Windows App SDK 1.8) |
| **Language** | C# (.NET 8) |
| **Architecture** | MVVM (CommunityToolkit.Mvvm) |
| **DI** | Microsoft.Extensions.DependencyInjection |
| **Target** | net8.0-windows10.0.19041.0 (min: 10.0.17763.0) |
| **Platforms** | x86, x64, ARM64 |
| **Crash Reporting** | Sentry (Privacy-safe path scrubbing) |

## Implemented Features (120+)

### Core Navigation
- Miller Columns + Details + List + Icon + Home 5종 뷰 모드
- 주소 표시줄 (Breadcrumb + 편집 모드), Back/Forward 히스토리 드롭다운
- Type-Ahead 검색, 실시간 필터 바 (Ctrl+Shift+F, 와일드카드 지원)

### File Operations
- 복사/이동/삭제: 진행률, 일시정지/재개/취소, 병렬 작업
- 배치 이름 변경: 찾기/바꾸기(정규식), 접두사/접미사, 번호 매기기 + 실시간 미리보기
- ZIP 압축/해제, 파일 복제, 바로가기 생성
- 실행 취소/다시 실행 (최대 50개 히스토리)

### Multi-Tab & Multi-Window
- 탭별 독립 히스토리/뷰 모드, 탭 Tear-Off(새 창 분리)
- 세션 자동 저장/복원, 멀티 윈도우

### Drag & Drop
- 내부/외부 드래그 앤 드롭, 분할 뷰 간 이동/복사
- Spring-loaded 폴더 (호버 시 자동 열림)
- StorageItems 지연 로딩 (외부 앱 호환)

### Preview Panel
- 이미지, 비디오, 오디오, PDF, 텍스트(30+ 확장자), 폰트, Hex 덤프
- 원격 파일(FTP/SFTP) 미리보기 지원
- Quick Look (Space 키) 모달

### Sidebar
- 로컬/네트워크/클라우드 드라이브, USB 핫플러그 감지
- 즐겨찾기 (드래그 리오더, Quick Access 동기화)
- 최근 폴더, 저장된 원격 연결

### Cloud & Git Integration
- Cloud Files API 기반 동기화 상태 배지 (OneDrive, iCloud, Dropbox)
- Git 파일 상태 배지 (Modified/Added/Deleted/Untracked), 브랜치 정보

### Search
- 재귀 검색 (BFS, Channel 기반 비동기, 최대 10,000 결과)
- 고급 쿼리: `kind:`, `size:`, `date:`, `ext:` 필터 + 와일드카드

### Settings & Localization
- 7종 테마 (Light/Dark/System/Dracula/Tokyo Night/Catppuccin/Gruvbox)
- 3종 아이콘 팩 (Remix/Phosphor/Tabler)
- 9개 언어 (EN, KO, JA, ZH-CN, ZH-TW, DE, ES, FR, PT-BR)
- 80+ 키보드 단축키

### Remote File Systems
- FTP/FTPS (FluentFTP, TOFU 인증서), SFTP (SSH.NET, 키 인증)
- SMB 네트워크 브라우저 (WNetEnumResource, NetShareEnum)

### Performance & Reliability
- 14,000+ 파일 Miller Columns 스크롤 지터 제거
- 딥 패스 네비게이션 병렬 컬럼 로딩
- async void 전수 try-catch 보호 (14개 메서드)
- 이벤트 핸들러 누적 방지 (-= before += 패턴)
- DispatcherQueue 스레드 안전성 확보
- Sentry 크래시 리포팅 (비동기 초기화, 경로 스크러빙)

## Project Structure

```
src/Span/Span/
  App.xaml.cs                          # DI, 앱 라이프사이클
  MainWindow.xaml.cs                   # 메인 윈도우 (partial class)
  MainWindow.*.cs                      # 기능별 핸들러 (8개 partial)
  Models/                              # 데이터 모델 (IFileSystemItem, DriveItem 등)
  ViewModels/                          # MVVM ViewModel (Explorer, Folder, File 등)
  Views/                               # XAML 뷰 (Details, Icon, List, Home, Settings 등)
  Services/                            # 비즈니스 로직 (40+ 서비스)
  Services/FileOperations/             # 파일 작업 (Copy, Move, Delete, Compress 등)
  Helpers/                             # 유틸리티 (DebugLogger, NaturalSort 등)
  Converters/                          # XAML 바인딩 컨버터
docs/
  00-context/                          # 요구사항, 기획
  01-plan/features/                    # 기능별 실행 계획 (11개)
  02-design/features/                  # 상세 설계 (10개)
  03-analysis/                         # Gap 분석, 테스트 분석
  04-report/                           # 완료 보고서
```

## Build & Run

```bash
# Build
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# WinUI 3 앱은 MSIX 패키징 필요 — Visual Studio F5로 실행
# dotnet run은 WinUI 3에서 지원되지 않음
```

## Status

- **Current**: v1.0 배포 준비 중 (안정화 + 성능 최적화 단계)
- **Features**: 120+ 구현 완료
- **Stability**: Critical/High 안정성 이슈 18건 중 15건 해결 완료
- **Next**: Sentry 기반 실사용 안정성 모니터링 → v1.0 릴리스

## License

TBD
