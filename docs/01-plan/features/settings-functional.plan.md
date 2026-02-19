# Plan: Settings Functional Implementation

## Feature Name
`settings-functional`

## Created
2026-02-17

## Status
Plan

---

## 1. Background & Problem Statement

Span 파일 탐색기의 설정 화면(SettingsDialog)은 5개 섹션(일반, 모양, 탐색, 도구, 정보)에 25개 이상의 UI 컨트롤을 갖추고 있으나, **실제 동작하는 기능이 2개뿐**이다 (언어 변경 알림, 업데이트 확인 애니메이션). 나머지 모든 설정은 시각적으로만 존재하며 앱 재시작 시 초기화된다.

### Current State
- SettingsService가 존재하지 않음
- 설정 저장/로드 메커니즘 없음 (LocalSettings는 Preview 너비와 즐겨찾기만 사용)
- 테마 전환 코드 없음
- 숨김 파일 표시 하드코딩 (FileSystemService에서 `continue`로 건너뜀)
- 밀도/폰트 변경 미구현

---

## 2. Goals

### Primary Goal
설정 화면의 모든 기능을 실제 동작하도록 구현하여, 사용자가 변경한 설정이 즉시 앱에 반영되고 앱 재시작 후에도 유지되도록 한다.

### Success Criteria
- [ ] 설정 저장/로드 서비스 (SettingsService) 구현
- [ ] 앱 시작 시 저장된 설정 자동 적용
- [ ] 테마(시스템/라이트/다크) 실시간 전환
- [ ] 숨김 파일/확장자 표시 토글 동작
- [ ] 밀러 컬럼/상세/아이콘 뷰에서 설정 반영
- [ ] 빌드 에러 0개

---

## 3. Scope

### In Scope (구현 대상)

#### Phase 1: SettingsService Foundation
| 항목 | 설명 | 우선순위 |
|------|------|----------|
| SettingsService 생성 | `Windows.Storage.ApplicationData.Current.LocalSettings` 기반 | CRITICAL |
| SettingsViewModel 생성 | 양방향 바인딩용 ViewModel | CRITICAL |
| DI 등록 | App.xaml.cs에 Singleton 등록 | CRITICAL |
| 앱 시작 시 로드 | MainWindow 초기화에서 설정 적용 | CRITICAL |

#### Phase 2: General Section (일반)
| 설정 | Key | Type | Default | 구현 내용 |
|------|-----|------|---------|-----------|
| 언어 | `Language` | string | "system" | LocalizationService 연동, 실제 언어 전환 |
| 시작 동작 | `StartupBehavior` | enum(0,1,2) | 0 (마지막 세션) | 마지막 경로 저장/복원 or 홈 or 특정 폴더 |
| 시스템 트레이 | `MinimizeToTray` | bool | false | (UI만 — 트레이 기능은 별도 feature) |

#### Phase 3: Appearance Section (모양)
| 설정 | Key | Type | Default | 구현 내용 |
|------|-----|------|---------|-----------|
| 테마 | `Theme` | string | "system" | `ElementTheme` 변경, Window.RequestedTheme 적용 |
| 밀도 | `Density` | string | "comfortable" | Padding/FontSize/RowHeight 조정 |
| 폰트 | `FontFamily` | string | "Segoe UI" | App-level FontFamily 리소스 오버라이드 |

#### Phase 4: Browsing Section (탐색)
| 설정 | Key | Type | Default | 구현 내용 |
|------|-----|------|---------|-----------|
| 숨김 파일 표시 | `ShowHiddenFiles` | bool | false | FileSystemService.GetItemsAsync 필터 조건 |
| 파일 확장자 표시 | `ShowFileExtensions` | bool | true | FileSystemViewModel.DisplayName 로직 |
| 체크박스 선택 | `ShowCheckboxes` | bool | false | ListView SelectionMode 변경 |
| 밀러 컬럼 동작 | `MillerClickBehavior` | string | "single" | 싱글/더블 클릭으로 열기 |
| 이미지 썸네일 | `ShowThumbnails` | bool | true | IconService 썸네일 로드 토글 |
| Quick Look | `EnableQuickLook` | bool | true | Space키 미리보기 토글 |
| 삭제 확인 | `ConfirmDelete` | bool | true | 삭제 시 ContentDialog 표시 여부 |
| 실행취소 기록 | `UndoHistorySize` | int | 50 | FileOperationHistory MaxSize |

#### Phase 5: Tools Section (도구)
| 설정 | Key | Type | Default | 구현 내용 |
|------|-----|------|---------|-----------|
| 기본 터미널 | `DefaultTerminal` | string | "cmd" | 터미널 열기 시 사용할 프로그램 |
| Context Menu | `ShowContextMenu` | bool | true | 우클릭 컨텍스트 메뉴 표시 여부 |

#### Phase 6: About Section (정보)
| 항목 | 구현 내용 |
|------|-----------|
| 빌드 번호 | Assembly 버전 정보 표시 |
| 라이선스 | 정적 텍스트 |
| GitHub 링크 | `Launcher.LaunchUriAsync` |
| Buy Me Coffee | `Launcher.LaunchUriAsync` |

### Out of Scope (이번 구현에서 제외)
- Smart Run 기능 (별도 feature로 분리)
- Pro 테마/업그레이드 (유료 기능 — 별도 feature)
- 실제 업데이트 확인 (서버 인프라 필요)
- 시스템 트레이 기능 (별도 feature)
- 다국어 전환 (현재 LocalizationService는 컨텍스트 메뉴만 지원 — 전체 i18n은 별도)

---

## 4. Technical Approach

### Architecture

```
┌─────────────────────────────────────────────────┐
│                SettingsDialog.xaml               │
│  (UI Controls — x:Bind to SettingsViewModel)    │
└─────────┬───────────────────────────────────────┘
          │ TwoWay Binding
┌─────────▼───────────────────────────────────────┐
│            SettingsViewModel                     │
│  (ObservableProperty for each setting)          │
│  OnPropertyChanged → SettingsService.Set()      │
└─────────┬───────────────────────────────────────┘
          │
┌─────────▼───────────────────────────────────────┐
│            SettingsService (Singleton)           │
│  - Load(): LocalSettings → Properties          │
│  - Set(key, value): Property → LocalSettings   │
│  - SettingsChanged event                        │
│  - 기본값 관리                                    │
└─────────┬───────────────────────────────────────┘
          │ Event
┌─────────▼───────────────────────────────────────┐
│         Consumers (설정 소비자)                    │
│  - MainWindow: 테마 적용                         │
│  - FileSystemService: 숨김파일 필터              │
│  - FileOperationHistory: Undo 크기              │
│  - ExplorerViewModel: 밀러 컬럼 동작            │
└─────────────────────────────────────────────────┘
```

### Key Design Decisions

1. **Persistence**: `Windows.Storage.ApplicationData.Current.LocalSettings` 사용 (이미 FavoritesService에서 사용 중인 패턴)
2. **Binding**: SettingsViewModel에서 `[ObservableProperty]`로 양방향 바인딩
3. **Event-based propagation**: `SettingsChanged` 이벤트로 변경 사항을 앱 전체에 전파
4. **즉시 적용**: OK/Cancel 없이 변경 즉시 적용 (macOS Finder 방식)

---

## 5. Implementation Order

```
Phase 1: SettingsService + SettingsViewModel (Foundation)
    ↓
Phase 2: General — 시작 동작 (마지막 경로 저장/복원)
    ↓
Phase 3: Appearance — 테마 전환 (가장 가시적인 변화)
    ↓
Phase 4: Browsing — 숨김 파일/확장자 (핵심 파일탐색기 기능)
    ↓
Phase 5: Tools — 터미널 설정
    ↓
Phase 6: About — 버전 정보, 링크
    ↓
Phase 7: XAML 바인딩 연결 + 통합 테스트
```

---

## 6. Files to Create/Modify

### New Files
| File | Description |
|------|-------------|
| `Services/SettingsService.cs` | 설정 저장/로드/이벤트 서비스 |
| `ViewModels/SettingsViewModel.cs` | 설정 다이얼로그 ViewModel |

### Modified Files
| File | Changes |
|------|---------|
| `App.xaml.cs` | SettingsService DI 등록, 시작 시 설정 적용 |
| `Views/SettingsDialog.xaml` | x:Name/x:Bind 추가, 이벤트 핸들러 연결 |
| `Views/SettingsDialog.xaml.cs` | SettingsViewModel 연동, 기존 stub 코드 제거 |
| `Services/FileSystemService.cs` | ShowHiddenFiles 설정 반영 |
| `ViewModels/ExplorerViewModel.cs` | 밀러 컬럼 동작 설정 반영 |
| `ViewModels/FileSystemViewModel.cs` | DisplayName 확장자 토글 |
| `MainWindow.xaml.cs` | 테마 적용, 시작 경로 복원, 삭제 확인 |

---

## 7. Risk Assessment

| Risk | Impact | Mitigation |
|------|--------|------------|
| 테마 전환 시 런타임 크래시 | HIGH | ElementTheme만 변경 (Mica는 유지) |
| 숨김 파일 필터링 성능 | LOW | 이미 어트리뷰트 체크 존재, 조건만 변경 |
| 설정 마이그레이션 | LOW | 기본값 fallback으로 처리 |
| XAML 바인딩 에러 | MEDIUM | Phase별 빌드 확인 |
| 밀도 변경 시 레이아웃 깨짐 | MEDIUM | Resource Dictionary 오버라이드 방식 |

---

## 8. Estimated Effort

| Phase | 예상 규모 |
|-------|-----------|
| Phase 1: Foundation | ~200 lines (2 new files) |
| Phase 2: General | ~50 lines |
| Phase 3: Appearance | ~100 lines (테마 가장 복잡) |
| Phase 4: Browsing | ~150 lines (가장 많은 항목) |
| Phase 5: Tools | ~30 lines |
| Phase 6: About | ~20 lines |
| Phase 7: Integration | ~100 lines (XAML 바인딩) |
| **Total** | **~650 lines** |

---

## 9. Dependencies

- `CommunityToolkit.Mvvm` — ObservableProperty (이미 설치됨)
- `Windows.Storage.ApplicationData` — 설정 저장 (이미 사용 중)
- 기존 `FileSystemService`, `ExplorerViewModel` — 설정 소비자
