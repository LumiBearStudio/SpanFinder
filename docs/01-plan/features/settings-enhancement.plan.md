# Plan: Settings Enhancement

## Feature Name
`settings-enhancement`

## Created
2026-02-17

## Status
Plan

---

## 1. Background & Problem Statement

Span의 SettingsService와 SettingsDialog는 기본 기능(테마, 숨김파일, 확장자, 밀도, 폰트, 터미널 등 14개 설정)을 제공하지만, 코드에 하드코딩된 기능 28개와 macOS Finder/Windows Explorer가 제공하는 핵심 설정들이 누락되어 있다.

### Current State (구현 완료)
| 섹션 | 설정 수 | 항목 |
|------|---------|------|
| 일반 | 3 | Language, StartupBehavior, SystemTray(UI만) |
| 모양 | 3 | Theme, Density, FontFamily |
| 탐색 | 8 | ShowHidden, Extensions, Checkboxes, MillerClick, Thumbnails, QuickLook, ConfirmDelete, UndoHistory |
| 도구 | 2 | DefaultTerminal, ContextMenu |
| 정보 | 0 | 정적 콘텐츠 (설정값 없음) |
| **합계** | **16** | |

### Gap Analysis — 코드에 하드코딩된 설정 가능 항목
| 카테고리 | 항목 수 | 예시 |
|----------|---------|------|
| 레이아웃 & 크기 | 3 | 컬럼 너비(220px), 프리뷰 패널 너비(280px) |
| 정렬 | 3 | 정렬 기준/방향 미저장, 폴더 우선 하드코딩 |
| 파일 작업 | 2 | 드래그 기본 동작, 충돌 해결 방식 |
| 뷰 모드 | 2 | 기본 뷰 모드, 뷰 모드 기억 |
| 탐색 동작 | 3 | 최근 폴더 수(20), 주소창 자동완성, 새탭 경로 |
| 사이드바 | 2 | 사이드바 표시/숨김, 섹션 구성 |
| 성능 | 2 | 프리뷰 디바운스(200ms), 타입어헤드 타임아웃(800ms) |

### Finder/Explorer 대비 누락 핵심 설정
- 정렬 기준/방향 영구 저장 (Finder & Explorer 모두 제공)
- 기본 뷰 모드 설정 (Finder: 아이콘/리스트/컬럼/갤러리)
- 밀러 컬럼 너비 조정 (Finder: Option+드래그로 전체 동일 너비)
- 폴더 우선 정렬 토글 (Windows Explorer: "Keep folders on top")
- 새 탭 열기 경로 (Files App: 홈/특정폴더/마지막경로)
- 확장자 변경 시 경고 (Finder: Advanced 탭)
- 파일 크기 표시 형식 (Files App: Binary/Decimal)
- 상태바 표시/숨김 (Finder & Explorer 모두 제공)

---

## 2. Goals

### Primary Goal
Span의 설정 화면을 macOS Finder Advanced/Windows Explorer Folder Options 수준으로 확장하여, 파워 유저가 원하는 핵심 커스터마이징을 제공한다.

### Success Criteria
- [ ] 정렬 기준/방향이 앱 재시작 후에도 유지됨
- [ ] 기본 뷰 모드 설정 가능 (Miller/Details/Icon)
- [ ] 밀러 컬럼 기본 너비 조정 가능 (180~400px)
- [ ] 새 탭 열기 시 경로 설정 가능
- [ ] 폴더 우선 정렬 토글 동작
- [ ] 드래그앤드랍 기본 동작 설정 가능
- [ ] 프리뷰 패널 동작 설정 추가
- [ ] 주소창 동작 설정 추가
- [ ] 빌드 에러 0개

---

## 3. Scope

### Phase 1: 정렬 설정 영구화 (Sort Preferences)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 정렬 기준 | `DefaultSortField` | string | "name" | name/date/size/type |
| 정렬 방향 | `DefaultSortDirection` | string | "asc" | asc/desc |
| 폴더 우선 | `KeepFoldersOnTop` | bool | true | 정렬 시 폴더를 항상 위에 |

**구현 포인트**:
- SettingsService에 3개 속성 추가
- FolderViewModel.EnsureChildrenLoadedAsync()에서 설정 읽기
- MainWindow.xaml.cs OnSortBy* 핸들러에서 설정 저장
- SettingsDialog 탐색 섹션에 정렬 카드 추가

### Phase 2: 뷰 모드 설정 (View Mode Preferences)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 기본 뷰 모드 | `DefaultViewMode` | string | "miller" | miller/details/icon_medium |
| 뷰 모드 기억 | `RememberViewMode` | bool | false | true면 마지막 사용한 뷰 모드 저장 |
| 마지막 뷰 모드 | `LastViewMode` | string | "miller" | RememberViewMode=true 시 사용 |

**구현 포인트**:
- ExplorerViewModel/MainViewModel에서 초기 뷰 모드 설정 적용
- 뷰 모드 변경 시 LastViewMode 저장
- SettingsDialog 탐색 섹션에 기본 뷰 모드 카드 추가

### Phase 3: 밀러 컬럼 레이아웃 (Miller Column Layout)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 컬럼 기본 너비 | `MillerColumnWidth` | int | 220 | 180~400px 범위 |
| 컬럼 최소 너비 | `MillerColumnMinWidth` | int | 150 | 최소 너비 |

**구현 포인트**:
- MainWindow.xaml.cs ColumnWidth 상수(220px) → 설정값 읽기
- SettingsDialog 탐색 섹션에 슬라이더 추가
- 설정 변경 시 현재 열린 컬럼 너비 즉시 업데이트

### Phase 4: 탭 & 탐색 동작 (Tab & Navigation)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 새 탭 경로 | `NewTabPath` | string | "home" | home/last/custom |
| 사용자 지정 탭 경로 | `CustomNewTabPath` | string | "" | NewTabPath=custom일 때 |
| 폴더를 새 탭으로 열기 | `OpenFolderInNewTab` | bool | false | 더블클릭 시 새 탭으로 |
| 빈 영역 더블클릭 상위 이동 | `DoubleClickBlankGoUp` | bool | false | Files App 스타일 |

**구현 포인트**:
- MainViewModel.AddTab()에서 NewTabPath 설정 적용
- MainWindow.xaml.cs 더블클릭 핸들러에 빈 영역 체크 추가
- SettingsDialog 일반 섹션에 탭 동작 카드 추가

### Phase 5: 파일 작업 동작 (File Operations)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 드래그 기본 동작 | `DragDropDefault` | string | "copy" | copy/move/ask |
| 확장자 변경 경고 | `WarnOnExtensionChange` | bool | true | 이름 변경 시 확장자 변경 경고 |
| 이름 충돌 해결 | `FileConflictAction` | string | "rename" | rename/overwrite/skip/ask |

**구현 포인트**:
- MainWindow.xaml.cs 드래그 핸들러에서 DragDropDefault 읽기
- HandleRename()에서 확장자 변경 감지 및 경고 다이얼로그
- HandlePaste()에서 충돌 해결 방식 분기
- SettingsDialog 탐색 섹션에 파일 작업 카드 추가

### Phase 6: 프리뷰 & 주소창 (Preview & Address Bar)
| 설정 | Key | Type | Default | 설명 |
|------|-----|------|---------|------|
| 프리뷰 패널 위치 | `PreviewPanelPosition` | string | "right" | right/bottom |
| 주소창 자동완성 | `AddressBarAutocomplete` | bool | true | 경로 자동완성 활성화 |
| 상태바 표시 | `ShowStatusBar` | bool | true | 하단 정보 표시줄 |

**구현 포인트**:
- 프리뷰 패널 레이아웃 방향 변경 (Grid Row/Column 전환)
- 주소창 AutoSuggestBox TextChanged 핸들러에 설정 체크
- 상태바 Visibility 바인딩
- SettingsDialog 모양 섹션에 추가

### Out of Scope
- Smart Run 기능 (별도 feature)
- Pro 테마/유료 기능 (별도 feature)
- 태그 시스템 (Finder Tags — 별도 feature)
- 키보드 단축키 커스터마이징 (고급 feature)
- 설정 내보내기/가져오기 (v2)
- 폴더별 뷰 모드 기억 (복잡도 높음 — v2)
- 사이드바 섹션 커스터마이징 (v2)

---

## 4. Technical Approach

### SettingsService 확장 패턴
기존 SettingsService의 Get/Set 패턴 유지:
```csharp
// 기존 패턴 재사용
public string DefaultSortField
{
    get => Get("DefaultSortField", "name");
    set => Set("DefaultSortField", value);
}
```

### SettingsDialog XAML 확장 패턴
기존 카드 스타일(SettingsCardBorder, CardIconContainer 등) 재사용:
```xml
<!-- 기존 스타일과 동일한 패턴 -->
<Border Style="{StaticResource SettingsCardBorder}">
    <Grid>
        <Border Style="{StaticResource CardIconContainer}">
            <FontIcon Glyph="..." Style="{StaticResource CardIconStyle}" />
        </Border>
        <StackPanel>
            <TextBlock Text="설정 이름" Style="{StaticResource SettingsCardLabel}" />
            <TextBlock Text="설명" Style="{StaticResource SettingsCardDesc}" />
        </StackPanel>
        <ComboBox/ToggleSwitch/Slider />
    </Grid>
</Border>
```

### SettingsDialog.xaml.cs 확장 패턴
기존 LoadSettingsToUI() + WireEvents() 패턴 유지:
```csharp
// LoadSettingsToUI()에 추가
SortFieldCombo.SelectedIndex = _settings.DefaultSortField switch { ... };

// WireEvents()에 추가
SortFieldCombo.SelectionChanged += (s, e) => {
    if (_isLoading) return;
    _settings.DefaultSortField = SortFieldCombo.SelectedIndex switch { ... };
};
```

### Settings Changed 이벤트 소비 패턴
기존 `SettingChanged` 이벤트 구독 패턴 유지:
```csharp
_settings.SettingChanged += (key, value) =>
{
    if (key == "DefaultSortField") { /* 정렬 기준 변경 처리 */ }
};
```

---

## 5. SettingsDialog 섹션별 UI 추가 계획

### 일반 (General) — Phase 4 추가
```
기존: 언어, 시작 동작, 시스템 트레이
추가: ┌─ 새 탭 경로 (홈/마지막 경로/사용자 지정)
      └─ 폴더를 새 탭으로 열기 토글
```

### 모양 (Appearance) — Phase 6 추가
```
기존: 테마, Pro 테마, 밀도, 폰트
추가: ┌─ 상태바 표시 토글
      └─ 프리뷰 패널 위치 (오른쪽/하단)
```

### 탐색 (Browsing) — Phase 1,2,3,5 추가
```
기존: 보기 옵션, 밀러 동작, 썸네일, Quick Look, 삭제 확인, Undo
추가: ┌─ 정렬 기본값 (기준 + 방향 + 폴더 우선)
      ├─ 기본 뷰 모드 (Miller/Details/Icon)
      ├─ 밀러 컬럼 너비 (슬라이더 180~400)
      ├─ 파일 작업 (드래그 동작, 충돌 해결, 확장자 경고)
      └─ 빈 영역 더블클릭 상위 이동
```

### 도구 (Tools) — 변경 없음
```
기존: 터미널, Smart Run, 컨텍스트 메뉴
```

### 정보 (About) — 변경 없음
```
기존: 앱 정보, Pro 업그레이드, 후원, 링크
```

---

## 6. Files to Modify

| File | Changes | Phase |
|------|---------|-------|
| `Services/SettingsService.cs` | 20개 새 속성 추가 | 1-6 |
| `Views/SettingsDialog.xaml` | 9개 새 카드 UI 추가 | 1-6 |
| `Views/SettingsDialog.xaml.cs` | LoadSettingsToUI/WireEvents 확장, 검색 키워드 추가 | 1-6 |
| `ViewModels/FolderViewModel.cs` | 정렬 설정 적용 | 1 |
| `MainWindow.xaml.cs` | 컬럼 너비, 드래그 동작, 빈 영역 클릭, 뷰 모드 초기화 | 2,3,4,5 |
| `ViewModels/MainViewModel.cs` | 뷰 모드 설정, 새 탭 경로 | 2,4 |
| `ViewModels/ExplorerViewModel.cs` | 기본 뷰 모드 | 2 |

---

## 7. Implementation Order

```
Phase 1: Sort Preferences (정렬 영구화)
  ├─ SettingsService: 3 속성
  ├─ SettingsDialog: 정렬 카드 1개
  ├─ FolderViewModel: 정렬 로직 수정
  └─ MainWindow: OnSortBy* 핸들러 설정 저장
    ↓
Phase 2: View Mode (뷰 모드 설정)
  ├─ SettingsService: 3 속성
  ├─ SettingsDialog: 뷰 모드 카드 1개
  └─ MainViewModel: 초기 뷰 모드 적용
    ↓
Phase 3: Miller Column Width (컬럼 너비)
  ├─ SettingsService: 2 속성
  ├─ SettingsDialog: 슬라이더 카드 1개
  └─ MainWindow: 컬럼 너비 동적 적용
    ↓
Phase 4: Tab & Navigation (탭 동작)
  ├─ SettingsService: 4 속성
  ├─ SettingsDialog: 탭 카드 2개
  └─ MainViewModel/MainWindow: 탭 경로/더블클릭 동작
    ↓
Phase 5: File Operations (파일 작업)
  ├─ SettingsService: 3 속성
  ├─ SettingsDialog: 파일 작업 카드 1개
  └─ MainWindow: 드래그/이름변경/붙여넣기 로직
    ↓
Phase 6: Preview & UI (프리뷰 & UI)
  ├─ SettingsService: 3 속성
  ├─ SettingsDialog: UI 카드 2개
  └─ MainWindow: 프리뷰 위치, 상태바
    ↓
Build Verification
```

---

## 8. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| 설정 수 급증으로 SettingsDialog 스크롤 길어짐 | LOW | 기존 검색 기능으로 해결, 카테고리별 그룹핑 |
| 정렬 설정 변경 시 성능 영향 | LOW | 이미 정렬 로직 존재, 비교 함수만 교체 |
| 밀러 컬럼 너비 변경 시 레이아웃 깨짐 | MEDIUM | 최소/최대 범위 강제 (150~400px) |
| 프리뷰 패널 위치 변경 복잡도 | HIGH | Phase 6으로 후순위, 필요시 v2로 연기 |
| 드래그 동작 변경 시 기존 동작 회귀 | MEDIUM | 기본값 "copy"로 유지, 설정 변경 시만 분기 |
| 파일 충돌 해결 "ask" 모드 다이얼로그 | MEDIUM | 기존 FileConflictDialog 재활용 |

---

## 9. Estimated Effort

| Phase | 새 설정 수 | UI 카드 수 | 코드 변경 규모 |
|-------|-----------|-----------|---------------|
| Phase 1: Sort | 3 | 1 | ~80 lines |
| Phase 2: View Mode | 3 | 1 | ~60 lines |
| Phase 3: Miller Width | 2 | 1 | ~50 lines |
| Phase 4: Tab & Nav | 4 | 2 | ~100 lines |
| Phase 5: File Ops | 3 | 1 | ~120 lines |
| Phase 6: Preview & UI | 3 | 2 | ~80 lines |
| **Total** | **18** | **8** | **~490 lines** |

---

## 10. Dependencies

- 기존 SettingsService (Get/Set/SettingChanged 패턴) — 구현 완료
- 기존 SettingsDialog XAML 스타일 — 구현 완료
- CommunityToolkit.Mvvm — 설치 완료
- 멀티선택 + 드래그앤드랍 Plan (Phase 5 파일 작업은 이것과 연관)

---

## 11. Finder/Explorer 설정 대비표

| 설정 | Finder | Explorer | Span 현재 | 이번 Plan |
|------|--------|----------|-----------|----------|
| 정렬 기준 영구 저장 | O | O | X | Phase 1 |
| 정렬 방향 영구 저장 | O | O | X | Phase 1 |
| 폴더 우선 정렬 토글 | O | O | 하드코딩 | Phase 1 |
| 기본 뷰 모드 | O | O | 하드코딩 | Phase 2 |
| 컬럼 너비 조정 | O | - | 하드코딩 | Phase 3 |
| 새 탭 기본 경로 | - | - | 하드코딩 | Phase 4 |
| 빈 영역 더블클릭 | - | - | X | Phase 4 |
| 드래그 기본 동작 | OS기본 | OS기본 | 하드코딩 | Phase 5 |
| 확장자 변경 경고 | O | - | X | Phase 5 |
| 이름 충돌 해결 | 다이얼로그 | 다이얼로그 | 자동이름 | Phase 5 |
| 프리뷰 위치 | 오른쪽 | 오른쪽 | 오른쪽 | Phase 6 |
| 상태바 토글 | O | O | 없음 | Phase 6 |
| 주소창 자동완성 토글 | - | - | 항상ON | Phase 6 |
