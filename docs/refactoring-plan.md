# Span 1.0 리팩토링 계획

## 현황 요약

| 파일 | 라인 수 | 문제 |
|------|---------|------|
| MainWindow.xaml.cs | 8,819 | God class: 223개 메서드, 11개 책임 영역 |
| MainViewModel.cs | 1,882 | 탭+드라이브+즐겨찾기+네비게이션 혼재 |
| ExplorerViewModel.cs | 1,041 | 네비게이션 히스토리가 내부에 섞임 |
| DetailsModeView.xaml.cs | 1,349 | 비즈니스 로직(정렬/필터/그룹) 코드비하인드에 |
| RubberBandSelectionHelper.cs | 643 | DI 미등록 헬퍼 |
| 서비스 38개 | 8,742 총 | 인터페이스 3개뿐, CancellationToken 누락 |
| XAML 전체 | - | AutomationProperties 0개 |

**P0 보안 이슈 3건**: 커맨드 인젝션, FTPS 인증서, SFTP 호스트키

---

## 페이즈 0: P0 보안 수정 (즉시)

### 0-1. ShellService 커맨드 인젝션 차단
**파일**: `Services/ShellService.cs` (82~84줄)

**현재** (취약):
```csharp
"powershell" => ("powershell.exe", $"-NoExit -Command \"cd '{directoryPath}'\""),
"cmd" => ("cmd.exe", $"/K cd /d \"{directoryPath}\""),
_ => ("wt.exe", $"-d \"{directoryPath}\"")
```

**수정**:
```csharp
"powershell" => ("powershell.exe", $"-NoExit -Command \"Set-Location -LiteralPath '{EscapePowerShell(directoryPath)}'\""),
"cmd" => ("cmd.exe", $"/K cd /d \"{EscapeCmd(directoryPath)}\""),
_ => ("wt.exe", $"-d \"{directoryPath.Replace("\"", "")}\"")
```

헬퍼:
```csharp
private static string EscapePowerShell(string s) => s.Replace("'", "''");
private static string EscapeCmd(string s) => s.Replace("\"", "\"\"");
```

### 0-2. FtpProvider FTPS 인증서 검증
**파일**: `Services/FtpProvider.cs` (42, 104줄)

**현재**: `ValidateAnyCertificate = true` (MITM 취약)

**수정**: `ValidateAnyCertificate` 제거, `ValidateCertificate` 콜백 추가
- 첫 연결 시 사용자에게 인증서 핑거프린트 확인 다이얼로그
- 승인 시 ConnectionInfo에 CertThumbprint 저장
- 이후 연결에서 저장값과 비교

### 0-3. SftpProvider 호스트키 검증
**파일**: `Services/SftpProvider.cs`

**수정**: `HostKeyReceived` 이벤트 핸들러 추가
- 첫 연결 시 호스트키 핑거프린트 확인 다이얼로그
- ConnectionInfo에 HostKeyFingerprint 저장
- 이후 연결에서 비교, 불일치 시 경고

### 0-4. ShellService.OpenWithAsync async void 제거
**현재**: `public async void OpenWithAsync(...)` — 예외 삼킴
**수정**: `public async Task OpenWithAsync(...)` + 호출부 await

---

## 페이즈 1: MainWindow.xaml.cs 분할 (핵심)

**목표**: 8,819줄 → MainWindow 2,000줄 이하 + 6개 매니저 클래스

### 추출할 매니저 (partial class 패턴)

| 매니저 | 추출 메서드 수 | 예상 라인 | 담당 |
|--------|---------------|-----------|------|
| `MainWindow.TabManager.cs` | ~16 | ~600 | 탭 생성/닫기/전환/드래그/Tear-off |
| `MainWindow.NavigationManager.cs` | ~18 | ~700 | 경로 이동, 뒤로/앞으로, 브레드크럼 |
| `MainWindow.KeyboardHandler.cs` | ~24 | ~900 | OnGlobalKeyDown 분할, Miller키, 단축키 |
| `MainWindow.FileOperationHandler.cs` | ~22 | ~800 | 복사/이동/삭제/이름변경/클립보드 |
| `MainWindow.DragDropHandler.cs` | ~24 | ~900 | 드래그 시작/오버/드롭, 외부 D&D |
| `MainWindow.ViewModeManager.cs` | ~14 | ~500 | Miller/Details/List/Icon 패널 전환 |
| `MainWindow.SplitPreviewManager.cs` | ~28 | ~1000 | 분할뷰, 인라인 프리뷰, QuickLook |
| `MainWindow.SettingsHandler.cs` | ~19 | ~400 | 테마/밀도/설정 적용 |

**방식**: `partial class MainWindow` — 파일만 분리, 클래스 변경 없음
- 장점: DI 변경 없음, 필드 공유, 기존 동작 100% 유지
- 각 파일에 `#region` 으로 영역 표시
- 공유 필드/프로퍼티는 `MainWindow.xaml.cs` (본체)에 유지

### God 메서드 분할

| 메서드 | 현재 줄 | 분할 계획 |
|--------|---------|-----------|
| `CleanupInlinePreview` | 452 | Cleanup{Image,Audio,Video,Document,Code}Preview 5개로 |
| `OnGlobalKeyDown` | 358 | HandleCtrlShortcut, HandleFunctionKey, HandleNavKey 3개로 |
| `ApplyCustomThemeOverrides` | 157 | 테마별 private 메서드로 |
| `UpdateInlinePreviewColumn` | 134 | Preview{Image,Media,Document} 3개로 |
| `HandleQuickLook` | 134 | Setup/Update/Cleanup 3개로 |
| `InitializeTabMillerPanels` | 116 | 그대로 (tab 초기화는 하나의 책임) |
| `FocusColumnAsync` | 106 | 그대로 (포커스 로직은 하나의 책임) |

---

## 페이즈 2: ViewModel 분할

### 2-1. MainViewModel 분할 (1,882줄 → ~800줄)

| 추출 대상 | 현재 위치 | 새 ViewModel | 예상 라인 |
|-----------|-----------|-------------|-----------|
| 탭 관리 | MainViewModel | TabManagementViewModel | ~400 |
| 드라이브 로딩 | MainViewModel | (MainViewModel에 유지) | ~200 |
| 즐겨찾기 | MainViewModel | FavoriteManagementViewModel | ~300 |
| 파일 연산 | MainViewModel | (MainViewModel에 유지) | ~200 |

### 2-2. ExplorerViewModel 정리 (1,041줄 → ~700줄)

- `FolderVm_PropertyChanged` (175줄) → 명확한 핸들러 메서드로 분할
- `NavigateToPath` (102줄) → ParsePath + DoNavigate 분리
- 네비게이션 히스토리 → `NavigationHistory` 내부 클래스 추출

### 2-3. FolderViewModel 정리 (510줄)

- `EnsureChildrenLoadedAsync` (164줄) → LoadFolder, LoadDrive, LoadNetwork 분리
- 정렬 로직 → SortComparer 추출

---

## 페이즈 3: 서비스 인터페이스 추가

### 인터페이스 추가 대상 (우선순위순)

| 서비스 | 인터페이스 | 이유 |
|--------|-----------|------|
| FileSystemService | IFileSystemService | 핵심 서비스, 단위 테스트 필수 |
| ShellService | IShellService | 외부 프로세스 모킹 |
| IconService | IIconService | UI 테스트 시 스텁 |
| FavoritesService | IFavoritesService | 상태 관리 테스트 |
| SettingsService | ISettingsService | 설정 주입 테스트 |
| PreviewService | IPreviewService | 프리뷰 모킹 |
| ActionLogService | IActionLogService | 로그 검증 |
| SearchService | ISearchService | 검색 테스트 |

### DI 등록 변경
```csharp
// Before
services.AddSingleton<FileSystemService>();
// After
services.AddSingleton<IFileSystemService, FileSystemService>();
```

### SettingsService 도메인 분리
현재 `SettingsService`는 모든 설정을 하나의 클래스에 평탄하게 보관.
인터페이스 수준에서 분리:
- `IAppearanceSettings`: Theme, Density, FontFamily, IconPack
- `IBrowsingSettings`: ShowHiddenFiles, ShowFileExtensions, ShowCheckboxes 등
- `IToolSettings`: DefaultTerminal, ShowContextMenu 등
- 구현은 `SettingsService` 하나로 유지 (다중 인터페이스 구현)

---

## 페이즈 4: Views 정리 + AutomationId

### 4-1. DetailsModeView 비즈니스 로직 이동

**현재**: `DetailsModeView.xaml.cs` (1,349줄)에 정렬/필터/그룹 로직
**수정**:
- 정렬 로직 → `DetailsSortViewModel` 또는 FolderViewModel에 통합
- 그룹 로직 → `DetailsGroupHelper` 추출
- 코드비하인드는 순수 UI 이벤트 핸들러만 유지

### 4-2. 뷰 간 중복 제거

공통 패턴 추출:
- **rename 핸들러**: ListModeView, DetailsModeView, IconModeView에 중복 → `RenameHelper` static
- **drag/drop 핸들러**: 3개 뷰에 동일 패턴 → 베이스 클래스 또는 헬퍼
- **context menu 바인딩**: 동일 패턴 반복 → Attached behavior

### 4-3. AutomationProperties 추가

FlaUI 자동화를 위해 모든 인터랙티브 요소에 AutomationId 추가:

```xml
<!-- 예시 -->
<Button AutomationProperties.AutomationId="BackButton" .../>
<ListView AutomationProperties.AutomationId="MillerColumn_0" .../>
<TextBox AutomationProperties.AutomationId="AddressBar" .../>
```

**네이밍 규칙**: `{ElementType}_{Purpose}` 또는 `{View}_{Element}`
- 버튼: `BackButton`, `ForwardButton`, `RefreshButton`
- 탭: `Tab_{index}`, `NewTabButton`, `CloseTabButton`
- 열: `MillerColumn_{index}`, `DetailsView`, `IconView`
- 입력: `AddressBar`, `SearchBox`, `RenameTextBox`
- 메뉴: `ContextMenu`, `ViewModeMenu`

**적용 범위**:
- `MainWindow.xaml`: 툴바, 사이드바, 상태바 (~50개)
- `DetailsModeView.xaml`: 헤더, 컬럼, 아이템 (~15개)
- `ListModeView.xaml`: 토글, 슬라이더, 그리드 (~10개)
- `IconModeView.xaml`: 슬라이더, 그리드 (~8개)
- 다이얼로그: 각 입력/버튼 (~20개)

---

## 실행 순서 및 예상 일정

```
페이즈 0: 보안 수정 ─────────────── [4개 파일, 단독 실행 가능]
    ↓
페이즈 1: MainWindow 분할 ──────── [partial class, 무중단]
    ↓
페이즈 2: ViewModel 분할 ─────── [새 파일 추출, 기존 동작 유지]
    ↓
페이즈 3: 서비스 인터페이스 ────── [DI 등록 변경, 컴파일 확인 필수]
    ↓
페이즈 4: Views 정리 + AutomationId [FlaUI 준비 완료]
    ↓
FlaUI 자동화 테스트 프로젝트 세팅
```

### 각 페이즈 검증

| 페이즈 | 검증 방법 |
|--------|-----------|
| 0 | `dotnet build` + 수동 터미널 열기 테스트 |
| 1 | `dotnet build` + 모든 기존 기능 동작 확인 |
| 2 | `dotnet build` + 탭/네비게이션/즐겨찾기 동작 확인 |
| 3 | `dotnet build` + DI 해결 확인 |
| 4 | `dotnet build` + Accessibility Insights 도구로 AutomationId 확인 |

---

## 리팩토링 원칙

1. **동작 변경 금지**: 리팩토링은 구조만 바꿈, 기능은 동일
2. **한 페이즈씩 커밋**: 각 페이즈 완료 후 빌드 확인 → 커밋
3. **partial class 우선**: MainWindow는 partial class로 분할 (가장 안전)
4. **인터페이스 추가 시 기존 구현 변경 최소화**: 메서드 시그니처 유지
5. **AutomationId는 마지막**: UI 구조 변경이 끝난 후 추가
