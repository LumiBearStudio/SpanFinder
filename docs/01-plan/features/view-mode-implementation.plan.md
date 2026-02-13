# View Mode Implementation Plan

**작성일**: 2026-02-13
**작성자**: Team Lead (view-mode-planning)
**목표**: Span 파일 탐색기에 3가지 뷰 모드(Miller Columns, Details, Icon) 구현

---

## 1. 문제 정의

### 1.1 현재 상태

Span은 현재 **Miller Columns 단일 뷰 모드**만 지원합니다.

- ✅ **Miller Columns**: macOS Finder 스타일의 계층적 탐색
- ❌ **Details 모드**: 파일 정보를 테이블 형식으로 표시하는 Windows Explorer의 가장 많이 사용되는 모드
- ❌ **Icon 모드**: 시각적 파일 탐색을 위한 그리드 레이아웃
- ❌ **뷰 모드 전환**: 사용자가 상황에 맞는 뷰를 선택할 수 없음

### 1.2 사용자 요구사항

Windows 사용자의 **90%는 Details와 Icon 모드를 주로 사용**합니다:

1. **Details 모드**: 파일 정보 비교, 정렬, 크기/날짜 확인에 최적
2. **Icon 모드**: 이미지/폴더 시각적 식별, 빠른 탐색에 최적
3. **Miller Columns**: 깊은 계층 구조 탐색, 여러 폴더 동시 비교에 최적

### 1.3 참고: 1.png 분석

Windows Explorer의 뷰 모드 메뉴 (10가지):
- 아주 큰 아이콘 (Extra Large Icons) - 256x256
- 큰 아이콘 (Large Icons) - 96x96
- 보통 아이콘 (Medium Icons) - 48x48
- 작은 아이콘 (Small Icons) - 16x16
- 목록 (List) - 작은 아이콘 + 단일 컬럼
- **자세히 (Details)** - 테이블 뷰 ⭐ 우선 구현
- 타일 (Tiles) - 중간 아이콘 + 메타데이터
- 내용 (Content) - 파일 미리보기 + 상세 정보
- 세부 정보 창 (Details Pane) - 우측 패널
- 미리 보기 창 (Preview Pane) - 우측 패널

**구현 범위**: Miller Columns + Details + Icon (Small/Medium/Large/ExtraLarge) **3가지 모드**로 시작

---

## 2. 해결 방안

### 2.1 아키텍처 설계

#### ViewMode 정의

```csharp
public enum ViewMode
{
    MillerColumns,  // 현재 기본 모드
    Details,        // 테이블 뷰 (Name, Date, Type, Size)
    IconSmall,      // 16x16 그리드
    IconMedium,     // 48x48 그리드
    IconLarge,      // 96x96 그리드
    IconExtraLarge  // 256x256 그리드
}
```

#### 뷰 모드 전환 메커니즘

```
MainViewModel
├─ CurrentViewMode: ViewMode (ObservableProperty)
├─ SwitchViewMode(ViewMode): void
└─ GetViewModeDisplayName(ViewMode): string

ExplorerViewModel
├─ ViewMode: ViewMode (현재 활성 모드)
└─ OnViewModeChanged(): void (컬럼/리스트 전환 로직)

MainWindow.xaml
├─ ViewModeSelector: CommandBar with SplitButton
├─ MillerColumnsView: Existing ScrollViewer
├─ DetailsView: DataGrid (새로 추가)
└─ IconGridView: GridView (새로 추가)
```

### 2.2 UI/UX 설계

#### 뷰 모드 전환 UI

**위치**: 주소 표시줄 우측, 검색창 좌측

```
┌────────────────────────────────────────────────────────────┐
│ 📁 C:\Users\... [▼]    [🔍 Search]    [≡ View ▼]  [⋮]     │
└────────────────────────────────────────────────────────────┘
```

**SplitButton 메뉴**:
```
≡ View
├─ ✓ Miller Columns    (Ctrl+1)
├─ Details             (Ctrl+2)
├─ Icons ▶
│  ├─ Extra Large
│  ├─ Large
│  ├─ Medium
│  └─ Small
```

#### Miller Columns 모드 (현재)

- 기존 구현 유지
- 가로 스크롤 가능한 컬럼 레이아웃
- 각 컬럼은 FolderViewModel.Children 표시

#### Details 모드

**레이아웃**: DataGrid with Sortable Columns

```
┌──────────────────────────────────────────────────────────┐
│ Name ▲    │ Date Modified ▼   │ Type        │ Size      │
├──────────────────────────────────────────────────────────┤
│ 📁 Documents │ 2026-02-10 10:30 │ File folder │           │
│ 📁 Pictures  │ 2026-02-09 14:22 │ File folder │           │
│ 📄 report.docx│ 2026-02-13 09:15│ Word Doc    │ 1.2 MB   │
│ 📄 data.xlsx │ 2026-02-12 16:45 │ Excel File  │ 345 KB   │
└──────────────────────────────────────────────────────────┘
```

**주요 기능**:
- 컬럼 헤더 클릭으로 정렬 (기존 NaturalStringComparer 활용)
- 컬럼 너비 조정 가능
- 단일 폴더만 표시 (Miller Columns의 마지막 컬럼과 동일한 데이터)

#### Icon 모드

**레이아웃**: WrapGrid with Adaptive Sizing

```
┌─────────────────────────────────────────┐
│  🖼️       🖼️       📁       📁         │
│ img1.jpg  img2.png Documents Pictures   │
│                                         │
│  📄       📄       🎵       🎥         │
│ doc1.docx data.xlsx song.mp3 video.mp4  │
└─────────────────────────────────────────┘
```

**아이콘 크기**:
- ExtraLarge: 256x256 (썸네일 표시)
- Large: 96x96
- Medium: 48x48
- Small: 16x16 + 한 줄 텍스트

**주요 기능**:
- 자동 줄바꿈 그리드 레이아웃
- 이미지 파일 썸네일 지원 (Phase 3.2 - 선택적)
- 정렬 유지 (Name, Date, Size 기준)

### 2.3 데이터 바인딩 전략

#### Single Source of Truth

모든 뷰 모드는 **동일한 FolderViewModel.Children** 컬렉션을 공유:

```csharp
// ExplorerViewModel.cs
public FolderViewModel? CurrentFolder => ViewMode switch
{
    ViewMode.MillerColumns => Columns.LastOrDefault(),
    ViewMode.Details => Columns.LastOrDefault(),
    _ => Columns.LastOrDefault() // Icon modes
};

public ObservableCollection<FileSystemViewModel> CurrentItems =>
    CurrentFolder?.Children ?? new();
```

#### 뷰 모드별 Visibility 전환

```xml
<!-- MainWindow.xaml -->
<ScrollViewer x:Name="MillerColumnsView"
              Visibility="{x:Bind IsMillerColumnsMode(ViewModel.CurrentViewMode), Mode=OneWay}">
    <!-- 기존 Miller Columns UI -->
</ScrollViewer>

<DataGrid x:Name="DetailsView"
          ItemsSource="{x:Bind ViewModel.Explorer.CurrentItems, Mode=OneWay}"
          Visibility="{x:Bind IsDetailsMode(ViewModel.CurrentViewMode), Mode=OneWay}">
    <!-- Details 모드 DataGrid -->
</DataGrid>

<GridView x:Name="IconGridView"
          ItemsSource="{x:Bind ViewModel.Explorer.CurrentItems, Mode=OneWay}"
          Visibility="{x:Bind IsIconMode(ViewModel.CurrentViewMode), Mode=OneWay}">
    <!-- Icon 모드 GridView -->
</GridView>
```

### 2.4 상태 관리

#### ViewMode 영속화

```csharp
// MainViewModel.cs
private ViewMode _currentViewMode = ViewMode.MillerColumns;

public void SaveViewModePreference()
{
    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
    settings.Values["LastViewMode"] = (int)CurrentViewMode;
}

public void LoadViewModePreference()
{
    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
    if (settings.Values.TryGetValue("LastViewMode", out var mode))
    {
        CurrentViewMode = (ViewMode)(int)mode;
    }
}
```

#### 폴더별 ViewMode 기억 (Phase 3 - 선택적)

고급 기능: 각 폴더마다 선호하는 뷰 모드 저장
- Pictures 폴더 → Icon ExtraLarge
- Documents 폴더 → Details
- Downloads 폴더 → Details

---

## 3. 구현 계획

### 3.1 Phase 1: ViewMode 인프라 구축 (1-2일)

**목표**: 뷰 모드 전환 메커니즘 및 UI 기반 구축

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| ViewMode enum 정의 | Models/ViewMode.cs (신규) | 30min | 5/5 |
| MainViewModel.CurrentViewMode 추가 | ViewModels/MainViewModel.cs | 1h | 5/5 |
| ExplorerViewModel.CurrentFolder 구현 | ViewModels/ExplorerViewModel.cs | 1h | 4/5 |
| ViewMode 전환 CommandBar UI | MainWindow.xaml | 2h | 4/5 |
| Visibility 전환 함수 (x:Bind) | MainWindow.xaml.cs | 1h | 5/5 |
| ViewMode 영속화 (LocalSettings) | ViewModels/MainViewModel.cs | 1h | 3/5 |

**검증 방법**:
- ViewMode 변경 시 UI 전환 확인 (빈 컨테이너 표시)
- 앱 재시작 후 마지막 ViewMode 복원 확인
- Miller Columns 기존 기능 정상 동작 확인

### 3.2 Phase 2: Details 모드 구현 (2-3일)

**목표**: 테이블 형식 Details 뷰 완성

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| DataGrid XAML 구현 | MainWindow.xaml | 3h | 5/5 |
| 컬럼 정의 (Name, Date, Type, Size) | MainWindow.xaml | 2h | 4/5 |
| 정렬 헤더 클릭 핸들러 | MainWindow.xaml.cs | 2h | 4/5 |
| NaturalStringComparer 통합 | MainWindow.xaml.cs | 1h | 3/5 |
| 선택 동기화 (DataGrid ↔ ViewModel) | MainWindow.xaml.cs | 2h | 5/5 |
| Enter/더블클릭으로 폴더 열기 | MainWindow.xaml.cs | 1h | 4/5 |
| 키보드 네비게이션 (↑↓) | MainWindow.xaml.cs | 1h | 3/5 |
| 인라인 rename (F2) 지원 | MainWindow.xaml | 2h | 3/5 |

**검증 방법**:
- Details 모드에서 폴더/파일 표시 확인
- 컬럼 정렬 (이름, 날짜, 크기, 유형) 정상 동작
- 폴더 더블클릭 시 해당 폴더로 네비게이션
- F2 rename, Delete, Ctrl+C/V 등 기존 기능 동작
- 선택 항목 동기화 (Details ↔ Miller Columns 전환 시)

### 3.3 Phase 3: Icon 모드 구현 (2-3일)

**목표**: 그리드 형식 Icon 뷰 완성

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| GridView XAML 구현 | MainWindow.xaml | 2h | 5/5 |
| ItemTemplate (4가지 크기별) | MainWindow.xaml | 3h | 4/5 |
| WrapGrid 레이아웃 설정 | MainWindow.xaml | 1h | 3/5 |
| 아이콘 크기 전환 (Small/Medium/Large/XL) | MainWindow.xaml.cs | 2h | 4/5 |
| 선택 동기화 (GridView ↔ ViewModel) | MainWindow.xaml.cs | 1h | 4/5 |
| Enter/더블클릭으로 폴더 열기 | MainWindow.xaml.cs | 1h | 4/5 |
| 키보드 네비게이션 (방향키) | MainWindow.xaml.cs | 1h | 3/5 |

**검증 방법**:
- Icon 모드에서 폴더/파일 그리드 표시
- 4가지 아이콘 크기 전환 정상 동작
- 자동 줄바꿈 레이아웃 확인
- 폴더 더블클릭 시 네비게이션
- F2 rename, Delete 등 기존 기능 동작

### 3.4 Phase 3.2: 썸네일 지원 (선택적, 3-5일)

**목표**: 이미지 파일 미리보기 (Icon ExtraLarge/Large 모드)

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| 썸네일 생성 서비스 | Services/ThumbnailService.cs (신규) | 4h | 4/5 |
| 비동기 이미지 로딩 | ViewModels/FileViewModel.cs | 3h | 3/5 |
| 캐싱 메커니즘 | Services/ThumbnailService.cs | 2h | 3/5 |
| ItemTemplate에 Image 바인딩 | MainWindow.xaml | 1h | 2/5 |

**검증 방법**:
- .jpg, .png 파일에 썸네일 표시
- 대용량 이미지 폴더에서 성능 확인 (500+ 이미지)
- 메모리 사용량 측정 (캐시 제한)

### 3.5 Phase 4: UX 개선 (1-2일)

**목표**: 사용성 향상 및 폴리싱

| 작업 | 파일 | 예상 시간 | Impact |
|------|------|-----------|--------|
| ViewMode 전환 애니메이션 | MainWindow.xaml | 2h | 2/5 |
| Details 컬럼 너비 저장/복원 | MainWindow.xaml.cs | 1h | 2/5 |
| Icon 크기 설정 저장/복원 | MainWindow.xaml.cs | 30min | 2/5 |
| 컨텍스트 메뉴에 View 추가 | MainWindow.xaml | 1h | 2/5 |
| 키보드 단축키 (Ctrl+1/2/3) | MainWindow.xaml.cs | 1h | 3/5 |
| 빈 폴더 플레이스홀더 (모든 모드) | MainWindow.xaml | 1h | 2/5 |

**검증 방법**:
- 뷰 모드 전환 시 부드러운 애니메이션
- 앱 재시작 후 Details 컬럼 너비, Icon 크기 복원
- Ctrl+1 (Miller), Ctrl+2 (Details), Ctrl+3 (Icon) 동작
- 빈 폴더에서 "This folder is empty" 메시지 표시

---

## 4. 검증 방법

### 4.1 기능별 테스트 시나리오

#### ViewMode 전환

**시나리오**:
1. Miller Columns 모드에서 폴더 탐색
2. "≡ View" 버튼 클릭 → "Details" 선택
3. Details 모드로 전환, 동일한 폴더 내용 표시 확인
4. "Icon → Large" 선택
5. Large Icon 모드로 전환 확인

**성공 기준**:
- 모든 모드 간 전환이 즉시 반영 (300ms 이내)
- 선택된 항목이 모드 전환 후에도 유지
- 폴더 내용이 모든 모드에서 일관되게 표시

#### Details 모드 정렬

**시나리오**:
1. Details 모드로 전환
2. "Name" 헤더 클릭 → 오름차순 정렬 확인
3. "Name" 헤더 다시 클릭 → 내림차순 정렬 확인
4. "Date Modified" 헤더 클릭 → 날짜순 정렬 확인
5. "Size" 헤더 클릭 → 크기순 정렬 확인

**성공 기준**:
- Natural sorting 적용 (1.txt, 2.txt, 10.txt 순서)
- 폴더 우선 정렬 유지
- 정렬 방향 인디케이터 (▲▼) 표시
- 정렬 상태가 모드 전환 후에도 유지

#### Icon 모드 크기 전환

**시나리오**:
1. Icon Small 모드 선택 → 16x16 아이콘 확인
2. Icon Medium 선택 → 48x48 아이콘 확인
3. Icon Large 선택 → 96x96 아이콘 확인
4. Icon Extra Large 선택 → 256x256 아이콘 확인

**성공 기준**:
- 각 크기별 적절한 아이콘 표시
- 자동 줄바꿈 레이아웃 정상 동작
- 선택 항목이 크기 변경 후에도 유지

#### 키보드 네비게이션

**시나리오**:
1. Details 모드에서 ↑↓ 키로 항목 이동
2. Enter 키로 폴더 열기
3. Icon 모드에서 방향키로 항목 이동
4. F2로 rename, Delete로 삭제

**성공 기준**:
- 모든 모드에서 키보드 네비게이션 동일하게 동작
- Miller Columns의 기존 단축키 유지

### 4.2 성능 측정 기준

| 항목 | 목표 | 측정 방법 |
|------|------|-----------|
| ViewMode 전환 시간 | 300ms 이하 | Stopwatch |
| Details 정렬 시간 (1000항목) | 100ms 이하 | Performance Profiler |
| Icon 모드 렌더링 (100항목) | 500ms 이하 | Performance Profiler |
| 썸네일 로딩 (50이미지) | 2s 이하 | Stopwatch |
| 메모리 사용량 증가 | 20% 이하 | Task Manager |

### 4.3 호환성 테스트

- [ ] Miller Columns 기존 기능 정상 동작
- [ ] 모든 모드에서 Ctrl+C/V, Delete, F2 동작
- [ ] 모든 모드에서 선택 항목 동기화
- [ ] 앱 재시작 후 ViewMode 및 설정 복원
- [ ] 다양한 화면 크기에서 레이아웃 정상 표시

---

## 5. 위험 및 대응 방안

### 5.1 기술적 위험

**위험 1: 3가지 뷰 UI를 동시에 유지하면서 메모리 사용량 증가**
- 대응: Visibility.Collapsed 사용 시 XAML 트리는 유지되므로 메모리 증가
- 완화: 실제 ItemsSource는 공유하므로 데이터 중복 없음
- 폴백: 성능 문제 시 Unloaded 이벤트에서 ItemsSource 해제

**위험 2: Details DataGrid 정렬이 FolderViewModel.Children과 충돌**
- 대응: DataGrid.ItemsSource는 OneWay 바인딩, 정렬은 View 레벨에서만 처리
- 완화: SortCurrentColumn 메서드 재사용 가능
- 폴백: CollectionViewSource 사용하여 정렬 레이어 분리

**위험 3: Icon 모드에서 대량 이미지 썸네일 로딩 시 UI 블로킹**
- 대응: Phase 3.2를 선택적으로 구현, 우선순위 낮음
- 완화: 비동기 로딩 + 가상화 (VirtualizingStackPanel)
- 폴백: 썸네일 대신 파일 아이콘만 표시

**위험 4: 3가지 뷰 간 선택 상태 동기화 실패**
- 대응: Single Source of Truth (ViewModel.SelectedChild) 원칙 유지
- 완화: 뷰 전환 시 선택 항목 명시적 설정
- 폴백: 선택 초기화 후 재설정

### 5.2 일정 위험

**위험: Phase 3.2 썸네일 구현이 예상보다 복잡할 수 있음**
- 대응: 선택적 기능으로 분류, Phase 1-3 완료 후 평가
- 완화: Windows.Storage.FileProperties API 활용
- 폴백: 썸네일 없이 아이콘만 표시해도 충분한 가치

### 5.3 UX 위험

**위험: 사용자가 뷰 모드 전환 UI를 찾지 못할 수 있음**
- 대응: CommandBar에 눈에 띄는 "≡ View" 버튼 배치
- 완화: 툴팁으로 "Change view (Ctrl+1/2/3)" 안내
- 폴백: 우클릭 메뉴에도 View 옵션 추가

---

## 6. 성공 메트릭

### 6.1 정량적 메트릭

- **뷰 모드 전환 속도**: 300ms 이내
- **Details 정렬 성능**: 1000항목 100ms 이하
- **Icon 렌더링 성능**: 100항목 500ms 이하
- **메모리 증가**: 20% 이하 (3가지 뷰 동시 유지)

### 6.2 정성적 메트릭

- **사용자 편의성**: Windows Explorer 수준의 친숙한 UX
- **뷰 모드 일관성**: 모든 모드에서 동일한 키보드 단축키 동작
- **시각적 완성도**: Details 테이블, Icon 그리드 정렬 및 간격

### 6.3 기능 완성도

**Phase 1-3 완료 시**:
- [x] Miller Columns 모드 (기존)
- [x] Details 모드 (새로 추가)
- [x] Icon 모드 4가지 크기 (새로 추가)
- [x] 뷰 모드 전환 UI
- [x] 설정 영속화

**Phase 3.2-4 완료 시 (선택적)**:
- [ ] 이미지 썸네일 지원
- [ ] 컬럼 너비 저장/복원
- [ ] 부드러운 전환 애니메이션

---

## 7. 참고 자료

### 설계 참고

- **1.png**: Windows Explorer 뷰 모드 메뉴 참고 이미지
- **file-explorer-tuning.plan.md**: 기존 Plan 문서 구조 및 형식
- **CLAUDE.md**: Span 프로젝트 아키텍처 및 MVVM 패턴

### 외부 자료

- [Microsoft DataGrid Documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.datagrid)
- [Microsoft GridView Documentation](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.gridview)
- [Windows File Explorer UX Guidelines](https://learn.microsoft.com/en-us/windows/win32/uxguide/ctrl-list-views)
- [WinUI 3 ItemsRepeater](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.controls.itemsrepeater)

### 유사 프로젝트

- **Files**: Modern Windows file manager with multiple view modes
- **One Commander**: Dual-pane file manager with Miller Columns support
- **Total Commander**: Classic file manager with various view options

---

## 8. 다음 단계

1. ✅ **PDCA Plan 작성 완료** (현재 단계)
2. **Design 문서 작성**: 각 Phase별 상세 UI/데이터 구조 설계
3. **Phase 1 구현**: ViewMode 인프라 구축
4. **Phase 2 구현**: Details 모드 완성
5. **Phase 3 구현**: Icon 모드 완성
6. **Gap Analysis**: 설계-구현 일치 검증
7. **최종 Report**: 완료 보고서 작성

---

**계획 승인 후 즉시 Design 문서 작성 및 Phase 1 구현을 시작할 수 있습니다.**
