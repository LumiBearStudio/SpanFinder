# Span Features — Test Reference Document

> 테스트케이스 작성 시 참고용 기능 명세서
> Last Updated: 2026-02-25

---

## 1. 설정 (Settings) 관련

### 1.1 설정 네비게이션 구조

| 메뉴 | Tag | 설명 |
|------|-----|------|
| 일반 | General | 언어, 시작 동작, 즐겨찾기 트리, 시스템 트레이, 창 위치 |
| 모양 | Appearance | 테마, 밀도, 아이콘 팩, 폰트 |
| 탐색 | Browsing | 숨김 파일, 확장자, 체크박스, 클릭 동작, 썸네일, 퀵룩, 삭제 확인, 실행취소 |
| 도구 | Tools | 셸 확장, Copilot 메뉴, 컨텍스트 메뉴 |
| 고급 | Advanced | 터미널, 개발자 메뉴, Git 연동, 핵스 프리뷰 |
| 정보 | About | 앱 정보, 관련 링크, Buy me a coffee, 저작권 |
| 오픈소스 | OpenSource | 서드파티 라이브러리 및 아이콘 폰트 라이선스 |

### 1.2 테마 선택

- **지원 테마**: System, Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox
- **즉시 적용**: 모든 테마 선택 시 재시작 없이 즉시 반영
- **다크 테마 수정** (2026-02-25): 커스텀 테마(Dracula 등)에서 Dark로 전환 시 즉시 적용되도록 수정
  - 구현: `MainWindow.SettingsHandler.cs` — 반대 테마로 토글 후 목표 테마 설정하여 ThemeResource 재평가 유도
- **Pro 테마 제거** (2026-02-25): Midnight Gold 등 Pro 전용 테마 삭제

**테스트 포인트:**
- [ ] System → Light → Dark → 각 커스텀 테마 전환 시 즉시 반영 확인
- [ ] Dracula → Dark 전환 시 즉시 적용 여부 (이전 버그)
- [ ] Dark → Dracula → Dark 반복 전환 시 정상 동작

### 1.3 Span Pro 제거 (2026-02-25)

- NavSpanPro 네비게이션 항목 삭제
- ProSection 전체 삭제 (업그레이드 UI, 요금제 배지 등)
- Appearance 섹션의 Pro 테마 카드 삭제
- ProBadge 스타일 삭제
- 관련 로컬라이제이션 키 정리

**테스트 포인트:**
- [ ] 설정 네비게이션에 "Span Pro" 메뉴가 없어야 함
- [ ] 모양 섹션에 Pro 테마(Midnight Gold 등)가 없어야 함

### 1.4 Buy Me a Coffee (2026-02-25)

- Pro 섹션에서 About 섹션으로 이동
- 금액 버튼: $3, $5, $10, $50
- 문구 변경: "Span이 마음에 드셨다면, 커피 한 잔으로 응원해 주세요!"
- 3개 언어 로컬라이제이션 업데이트 (EN/KO/JA)

**테스트 포인트:**
- [ ] 정보 섹션에 커피 카드 표시
- [ ] 언어 변경 시 문구 정상 전환

### 1.5 정보 섹션 (About)

- 앱 이름: Span
- 버전: v1.0.0 (Build 20260217)
- **Evaluation Copy 삭제** (2026-02-25)
- 업데이트 확인 버튼 (3단계 애니메이션: 확인 중 → 최신 버전 → 원래 상태)
- 관련 링크:
  - GitHub 저장소 → `https://github.com/kangjinkyu/Span`
  - 버그 제보 → `https://github.com/kangjinkyu/Span/issues`
  - 개인정보 처리방침 → `https://github.com/kangjinkyu/Span/blob/main/PRIVACY.md`
- **저작권** (2026-02-25): "© 2026 LumiBear Studio. All rights reserved." 하단 중앙

**테스트 포인트:**
- [ ] "Evaluation Copy" 문구가 없어야 함
- [ ] 저작권 표시가 About 섹션 최하단에 중앙 정렬
- [ ] 3개 링크가 각각 올바른 URL로 브라우저 열림
- [ ] 업데이트 확인 버튼 애니메이션 정상 동작

### 1.6 오픈소스 섹션 (2026-02-25 신규)

- 정보와 같은 레벨의 독립 네비게이션 메뉴
- **라이브러리 카테고리:**
  - FluentFTP (Robin Rodricks — MIT License, v53.0.2)
  - SSH.NET (Renci — MIT License, v2025.1.0)
- **아이콘 폰트 카테고리:**
  - Remix Icon (Remix Design — Apache 2.0, 기본 아이콘 팩)
  - Phosphor Icons (MIT License, 선택 가능)
  - Tabler Icons (MIT License, 선택 가능)
- "GitHub에서 전체 라이선스 보기" 링크 → `https://github.com/kangjinkyu/Span/blob/main/LICENSES.md`
- Microsoft 제작 패키지는 표시하지 않음 (고지 의무 없는 자사 프레임워크)

**테스트 포인트:**
- [ ] 네비게이션에서 "오픈소스" 메뉴 선택 시 오픈소스 섹션 표시
- [ ] 라이브러리 2개, 아이콘 폰트 3개 목록 표시
- [ ] 전체 라이선스 링크 클릭 시 GitHub 페이지 열림
- [ ] 3개 언어로 네비게이션 및 설명 텍스트 전환

### 1.7 핵스 프리뷰 토글 (2026-02-25 신규)

- 위치: 고급 섹션
- 기본값: OFF
- ON 시 바이너리 파일을 Hex 뷰어로 미리보기
- OFF 시 바이너리 파일은 Generic 미리보기 (파일 정보만 표시)
- 구현: `PreviewPanelViewModel.cs` — `PreviewType.HexBinary`일 때 설정값에 따라 `PreviewType.Generic`으로 폴백

**테스트 포인트:**
- [ ] 기본 상태에서 .exe, .dll 등 바이너리 파일 선택 시 Generic 미리보기
- [ ] 토글 ON 후 바이너리 파일 선택 시 Hex 뷰어 표시
- [ ] 토글 상태 앱 재시작 후 유지

---

## 2. 숨김 파일 반투명 표시 (2026-02-25)

### 2.1 문제

- `ProgramData`, `$WINDOWS.~BT` 등 숨김 폴더/파일이 일반 항목과 동일한 불투명도로 표시됨
- `IsHidden` 속성이 `FileSystemService.cs`에서만 설정되고, 실제 Miller Columns 경로인 `FolderViewModel.PopulateChildren()`에서는 미설정

### 2.2 수정 내용

- **FolderViewModel.cs** (`PopulateChildren`):
  - `FolderItem` 생성 시 `IsHidden = (d.Attributes & FileAttributes.Hidden) != 0`
  - `FileItem` 생성 시 `IsHidden = (f.Attributes & FileAttributes.Hidden) != 0`
- **LocalFileSystemProvider.cs**:
  - 동일하게 `FolderItem`, `FileItem` 생성 시 `IsHidden` 설정

### 2.3 XAML 바인딩

- `Opacity="{x:Bind IsHidden, Mode=OneWay, Converter={StaticResource BoolToOpacityConverter}}"`
- Hidden=true → Opacity 0.5, Hidden=false → Opacity 1.0
- 4개 뷰 모드 모두 적용: Miller, Details, List, Icon

**테스트 포인트:**
- [ ] C:\ 진입 시 ProgramData, $Recycle.Bin 등 숨김 폴더가 반투명 표시
- [ ] 숨김 파일/폴더가 아닌 항목은 불투명도 100%
- [ ] 설정 > 탐색 > "숨김 파일 표시" OFF 시 숨김 항목 자체가 안 보임
- [ ] Details/List/Icon 뷰에서도 반투명 정상 표시

---

## 3. Miller Columns 아이콘 2px 쉬프트 수정 (2026-02-25)

### 3.1 문제

- 선택된 항목의 아이콘이 ~2px 오른쪽으로 밀림
- 원인: Path indicator Border의 `Margin="-8,2,6,2"` — 6px 우측 마진이 Auto 컬럼을 확장

### 3.2 수정 내용

- `MainWindow.xaml` 내 4개 path indicator Border:
  - `Margin="-8,2,6,2"` → `Margin="-8,2,0,2"`
- 대상 템플릿: FolderTemplate (Miller), FolderTemplate (Details 포함 시), 관련 DataTemplate들

**테스트 포인트:**
- [ ] Miller Columns에서 폴더 선택/비선택 시 아이콘 위치 동일
- [ ] 경로 표시기(파란 세로선) 활성화 시에도 아이콘 위치 변동 없음
- [ ] 여러 뎁스 탐색 후에도 아이콘 정렬 유지

---

## 4. 탭 이름 버그 수정 (2026-02-25)

### 4.1 문제

- 앱 재시작 시 탭 이름이 이전 세션의 파일 이름("URCounting.dll")으로 표시되고, 실제 뷰는 Home

### 4.2 수정 내용

- **MainViewModel.TabManagement.cs**:
  - `LoadTabsFromSettings()`: ViewMode가 Home이면 `Header = "Home"` 강제 설정
  - `SaveActiveTabState()`: `CurrentViewMode`에 따라 Header 동기화
    - Home → "Home"
    - 그 외 → `Explorer.CurrentFolderName ?? "Home"`

**테스트 포인트:**
- [ ] Home 탭 열린 상태에서 앱 종료 → 재시작 → 탭 이름 "Home"
- [ ] 폴더 탐색 중 앱 종료 → 재시작 → 탭 이름이 해당 폴더 이름
- [ ] 여러 탭(Home + 폴더들) 혼합 상태에서 세션 복원 정확성

---

## 5. 설정/홈 모드 상태바 숨김 (2026-02-25)

### 5.1 수정 내용

- **MainViewModel.cs** (`UpdateStatusBar`):
  - Settings/Home 모드에서 `StatusItemCountText`, `StatusSelectionText`, `StatusDiskSpaceText` 전부 빈 문자열
  - `StatusViewModeText`만 현재 모드명 표시

- **MainWindow.xaml**:
  - 파일 아이콘(`&#xE8A5;`) StackPanel에 `Visibility="{x:Bind helpers:StatusBarHelper.ShowIfNotEmpty(ViewModel.StatusItemCountText)}"` 추가
  - ItemCountText가 비면 아이콘도 함께 숨김

**테스트 포인트:**
- [ ] 설정 탭에서 좌하단 "X개 항목", "X개 선택됨" 표시 안 됨
- [ ] 설정 탭에서 우하단 디스크 용량 표시 안 됨
- [ ] 설정 탭에서 파일 아이콘(&#xE8A5;) 표시 안 됨
- [ ] Home 탭에서도 동일하게 상태바 비어 있음
- [ ] 탐색 모드로 전환 시 항목 수, 선택, 디스크 용량 정상 표시

---

## 6. 설정 모드 Split View 비활성화 (2026-02-25)

### 6.1 수정 내용

- **MainWindow.xaml**: SplitViewButton에 `Visibility` 바인딩 추가 → Settings 모드에서 버튼 숨김
- **MainWindow.SplitPreviewManager.cs**: `IsNotSettingsMode()` 헬퍼 메서드 추가
- **MainWindow.xaml.cs** (`SetViewModeVisibility`): Settings/Home 진입 시 Split View 강제 비활성화
  - `IsSplitViewEnabled = false`
  - `SplitterCol.Width = 0`, `RightPaneCol.Width = 0`
  - `ActivePane = Left`

**테스트 포인트:**
- [ ] Split View 활성 상태에서 설정 탭 열기 → Split View 자동 해제
- [ ] 설정 탭에서 Split View 버튼 보이지 않음
- [ ] 설정 → 탐색 모드 복귀 시 Split View 버튼 다시 표시
- [ ] Home 탭에서도 Split View 버튼 숨김 및 자동 해제

---

## 7. 삭제 작업 개선 (2026-02-25)

### 7.1 수정 내용

- **DeleteFileOperation.cs**: Shell API 기반 휴지통 이동 및 진행률 추적
- 영구 삭제(Shift+Delete)와 휴지통 이동 분리
- 진행률 콜백 지원

**테스트 포인트:**
- [ ] Delete 키 → 휴지통 이동 (설정의 삭제 확인 토글 연동)
- [ ] Shift+Delete → 영구 삭제 확인 대화상자 → 영구 삭제
- [ ] 대량 파일 삭제 시 진행률 표시
- [ ] 접근 거부 파일 삭제 시도 시 에러 처리

---

## 8. Git 서비스 안정성 (2026-02-25)

### 8.1 수정 내용

- **GitStatusService.cs**: DI 해상도 및 UI 스레드 안전성 개선

**테스트 포인트:**
- [ ] Git이 설치되지 않은 환경에서 앱 정상 실행
- [ ] Git 리포지토리 폴더 진입 시 상태 배지 표시
- [ ] 빠른 폴더 전환 시 Git 상태 로딩 취소 정상 동작

---

## 9. 로컬라이제이션 (2026-02-25 업데이트)

### 9.1 추가/변경 키

| 키 | EN | KO | JA |
|----|----|----|-----|
| Settings_OpenSourceNav | Open Source | 오픈소스 | オープンソース |
| Settings_OpenSourceDesc | Open source libraries... | 이 프로젝트에서 사용된... | このプロジェクトで... |
| Settings_FullLicenseLink | View full license text on GitHub | GitHub에서 전체 라이선스 보기 | GitHubで全ライセンスを表示 |
| Settings_BuyMeCoffeeDesc | (Pro 참조 제거) | Span이 마음에 드셨다면... | Spanを気に入っていただけたら... |

### 9.2 삭제된 키

- Pro 관련: ProTitle, PlanBadgeText, UpgradeProTitle, ProThemesLabel, MidnightGoldDesc 등
- Settings_EvalCopy

**테스트 포인트:**
- [ ] EN/KO/JA 전환 시 오픈소스 섹션 텍스트 정상 번역
- [ ] 삭제된 Pro 관련 키 참조 시 크래시 없음

---

## 10. GitHub 문서 연동 (2026-02-25 신규)

### 10.1 리포지토리

- URL: `https://github.com/kangjinkyu/Span`
- 용도: 문서 전용 (소스코드 미포함)

### 10.2 문서 목록

| 파일 | 용도 |
|------|------|
| README.md | 앱 소개, 기능 비교표, 단축키, 성능 특징 |
| PRIVACY.md | 개인정보 처리방침 (데이터 수집 없음) |
| LICENSES.md | 서드파티 오픈소스 라이선스 전문 |

### 10.3 앱 내 링크 연결

| 설정 위치 | 대상 |
|-----------|------|
| 정보 > GitHub 저장소 | `https://github.com/kangjinkyu/Span` |
| 정보 > 버그 제보 | `https://github.com/kangjinkyu/Span/issues` |
| 정보 > 개인정보 처리방침 | `https://github.com/kangjinkyu/Span/blob/main/PRIVACY.md` |
| 오픈소스 > 전체 라이선스 보기 | `https://github.com/kangjinkyu/Span/blob/main/LICENSES.md` |

**테스트 포인트:**
- [ ] 4개 링크 각각 클릭 시 기본 브라우저에서 올바른 페이지 열림
- [ ] 네트워크 미연결 시 에러 처리 (OS 브라우저에 위임)

---

## 변경 파일 요약 (2026-02-25)

| 파일 | 변경 내용 |
|------|-----------|
| `MainWindow.xaml` | 아이콘 쉬프트 수정, SplitView 버튼 Visibility, 상태바 아이콘 Visibility |
| `MainWindow.xaml.cs` | Settings 모드 Split View 강제 해제 |
| `MainWindow.SettingsHandler.cs` | 다크 테마 즉시 적용 (토글 트릭) |
| `MainWindow.SplitPreviewManager.cs` | IsNotSettingsMode() 헬퍼 |
| `SettingsModeView.xaml` | Pro 제거, 오픈소스 섹션 추가, 저작권 추가, 링크 URL 설정 |
| `SettingsModeView.xaml.cs` | 오픈소스 섹션 등록, 로컬라이제이션 매핑 |
| `MainViewModel.cs` | UpdateStatusBar Settings/Home 조기 리턴 |
| `MainViewModel.TabManagement.cs` | 탭 헤더 동기화 (저장/복원) |
| `FolderViewModel.cs` | PopulateChildren에 IsHidden 설정 |
| `LocalFileSystemProvider.cs` | IsHidden 설정 |
| `PreviewPanelViewModel.cs` | ShowHexPreview 설정 연동 |
| `ISettingsService.cs` | ShowHexPreview 인터페이스 추가 |
| `SettingsService.cs` | ShowHexPreview 구현 (기본값 false) |
| `LocalizationService.cs` | 오픈소스/커피/라이선스 키 추가, Pro 키 정리 |
| `DeleteFileOperation.cs` | Shell API 휴지통 이동 + 진행률 |
| `GitStatusService.cs` | DI/UI 스레드 안정성 |
| `DetailsModeView.xaml` | 숨김 파일 반투명 |
| `IconModeView.xaml` | 숨김 파일 반투명 |
| `ListModeView.xaml` | 숨김 파일 반투명 |
