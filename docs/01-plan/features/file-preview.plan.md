# File Preview Panel Plan

**작성일**: 2026-02-16
**작성자**: Agent Team (Finder preview research + Codebase exploration + WinUI3 API research)
**목표**: Span 파일 탐색기에 macOS Finder 스타일 파일 정보/미리보기 패널 구현

---

## 1. 문제 정의

### 1.1 현재 상태

Span은 현재 파일 선택 시 **파일 미리보기/상세 정보 표시 기능이 없음**:

- Details 뷰에서 이름/날짜/종류/크기 컬럼 표시만 가능
- 파일 내용 미리보기 없음 (이미지, 텍스트, PDF, 미디어 등)
- 파일 메타데이터 상세 확인 불가 (EXIF, 비디오 정보, 오디오 태그 등)
- Split View(분할 모드) 양쪽 패널에서 독립적으로 미리보기 불가

### 1.2 사용자 요구사항

1. **파일 미리보기**: 이미지/텍스트/PDF/미디어 파일 선택 시 내용 미리보기
2. **파일 정보 표시**: 파일명, 크기, 생성일, 수정일, 종류, 해상도(이미지), 재생시간(미디어) 등
3. **패널 위치**: 탐색기 우측에 부착되는 사이드바 형태
4. **Split View 호환**: 분할 모드에서 양쪽 패널 각각 독립적으로 미리보기 패널 사용 가능
5. **토글 가능**: 단축키/버튼으로 미리보기 패널 표시/숨김

### 1.3 macOS Finder Preview 참고 분석

#### Finder Preview Pane 주요 기능
- **위치**: Finder 윈도우 우측 사이드바 (Column View에서는 마지막 컬럼으로 표시)
- **토글**: `Shift+Cmd+P` 단축키, View 메뉴
- **폴더 정보**: 항목 수, 총 크기
- **이미지**: 썸네일 + EXIF 메타데이터 (카메라, 렌즈, ISO, 조리개 등)
- **비디오**: 썸네일 + 재생 컨트롤 (Play 버튼) + 메타데이터
- **오디오**: 앨범 아트 + 재생 컨트롤 + 태그 정보 (아티스트, 앨범, 장르 등)
- **문서**: 내용 미리보기 + 기본 메타데이터
- **Quick Actions**: 이미지 변환, PDF 생성, 마크업, 회전 등
- **미선택 상태**: 빈 화면 표시
- **크기 조절**: 드래그로 너비 조정 가능

#### Quick Look (Space Bar) - 별도 기능
- 독립 플로팅 윈도우, 스페이스바로 토글
- 전체 화면 미리보기 (Option+Space)
- Preview Pane과는 별개의 기능 → **Phase 2에서 고려**

---

## 2. 목표 & 범위

### 2.1 이번 구현 범위 (Phase 1)

| 구분 | 항목 | 우선순위 |
|------|------|---------|
| **레이아웃** | 우측 사이드바 패널 (GridSplitter로 너비 조절) | P0 |
| **토글** | 버튼 + 단축키 (`Ctrl+Shift+P`) | P0 |
| **파일 정보** | 이름, 크기, 종류, 생성일, 수정일 | P0 |
| **이미지 미리보기** | JPG, PNG, BMP, GIF, TIFF 썸네일/미리보기 | P0 |
| **텍스트 미리보기** | TXT, CS, JSON, XML, MD 등 텍스트 파일 내용 표시 | P1 |
| **PDF 미리보기** | 첫 페이지 렌더링 (Windows.Data.Pdf) | P1 |
| **미디어 미리보기** | MP4, MP3, WAV 등 재생 컨트롤 | P1 |
| **폴더 정보** | 항목 수 표시 | P1 |
| **Split View 호환** | 양쪽 패널 독립 미리보기 | P0 |
| **이미지 EXIF** | 해상도, 카메라 정보 등 확장 메타데이터 | P2 |
| **미디어 태그** | 아티스트, 앨범, 비트레이트 등 | P2 |

### 2.2 제외 범위

- Quick Look (Space Bar 플로팅 미리보기) → 별도 기능으로 추후 검토
- Quick Actions (이미지 변환, 회전 등) → 추후 검토
- Office 문서 미리보기 (.docx, .xlsx 등) → 추후 검토
- 구문 강조 (Syntax Highlighting) → 추후 검토

---

## 3. 기술 분석

### 3.1 WinUI 3 API 활용 가능

| 기능 | API | 비고 |
|------|-----|------|
| 이미지 썸네일 | `StorageFile.GetThumbnailAsync()` | Windows 썸네일 캐시 활용, 빠름 |
| 이미지 전체 | `BitmapImage.SetSourceAsync()` | 고품질 미리보기 |
| 텍스트 읽기 | `System.IO.StreamReader` | 인코딩 자동 감지 |
| PDF 렌더링 | `Windows.Data.Pdf.PdfDocument` | 네이티브, 기본 렌더링 |
| 미디어 재생 | `MediaPlayerElement` | 내장 트랜스포트 컨트롤 |
| 기본 메타데이터 | `System.IO.FileInfo` | 빠름 (Size, Created, Modified) |
| 이미지 메타데이터 | `StorageFile.Properties.GetImagePropertiesAsync()` | Width, Height, EXIF |
| 비디오 메타데이터 | `StorageFile.Properties.GetVideoPropertiesAsync()` | Duration, Bitrate, Resolution |
| 오디오 메타데이터 | `StorageFile.Properties.GetMusicPropertiesAsync()` | Artist, Album, Duration |

### 3.2 현재 아키텍처와의 통합 포인트

#### 선택 항목 추적 (Selection Tracking)
```
FolderViewModel.SelectedChild → PropertyChanged 이벤트
  → ExplorerViewModel.FolderVm_PropertyChanged()
  → Preview 패널 업데이트 트리거
```

#### Split View 구조 (현재 XAML)
```
Grid (Content Area)
├── Column 0: LeftPaneContainer (Left Explorer)
├── Column 1: SplitterCol (GridSplitter, 분할선)
└── Column 2: RightPaneCol (Right Explorer)
```

#### 미리보기 패널 추가 후
```
각 패널 내부 Grid:
├── Column 0: 탐색기 뷰 (Miller/Details/Icon/Home)
├── Column 1: PreviewSplitter (GridSplitter)
└── Column 2: PreviewPanel (미리보기)
```

**핵심 결정**: 미리보기 패널은 **각 패널(Left/Right) 내부에 독립적으로 존재**.
- 이유: Split View에서 좌/우 패널이 각각 독립적으로 미리보기를 토글/사용 가능
- Finder의 Column View처럼 탐색기 내부의 마지막 섹션으로 동작

### 3.3 성능 고려사항

| 사항 | 전략 |
|------|------|
| 빠른 선택 변경 | `CancellationTokenSource`로 이전 로딩 취소 |
| 대용량 파일 | 파일 크기 100MB 제한, 텍스트 50KB 까지만 미리보기 |
| 메모리 관리 | 미리보기 전환 시 이전 리소스 정리 (BitmapImage, MediaSource) |
| 디바운싱 | 선택 변경 후 200ms 대기 후 미리보기 로딩 시작 |
| System.IO 우선 | 기본 메타데이터는 `FileInfo` 사용 (StorageFile보다 빠름) |
| 비동기 전용 | 모든 미리보기 로딩은 async/await + CancellationToken |

---

## 4. 아키텍처 설계

### 4.1 신규 파일 구조

```
Models/
  └── FilePreviewInfo.cs           # 미리보기 메타데이터 모델

Services/
  └── PreviewService.cs            # 파일 미리보기 로딩 서비스

ViewModels/
  └── PreviewPanelViewModel.cs     # 미리보기 패널 상태 관리

Views/
  └── PreviewPanelView.xaml/.cs    # 미리보기 패널 UI
```

### 4.2 PreviewPanelViewModel (핵심)

```csharp
public partial class PreviewPanelViewModel : ObservableObject, IDisposable
{
    // 상태
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string _fileName;
    [ObservableProperty] private string _fileType;
    [ObservableProperty] private string _fileSizeFormatted;
    [ObservableProperty] private string _dateCreated;
    [ObservableProperty] private string _dateModified;
    [ObservableProperty] private string _dimensions;       // 이미지/비디오
    [ObservableProperty] private string _duration;          // 미디어

    // 미리보기 콘텐츠 (한 번에 하나만 활성)
    [ObservableProperty] private BitmapImage? _imagePreview;
    [ObservableProperty] private string? _textPreview;
    [ObservableProperty] private MediaSource? _mediaSource;

    // 미리보기 타입 판별
    [ObservableProperty] private PreviewType _currentPreviewType;

    // 메서드
    public async Task UpdatePreviewAsync(IFileSystemItem? item, CancellationToken ct);
    public void ClearPreview();
    public void Dispose();
}
```

### 4.3 PreviewType 열거형

```csharp
public enum PreviewType
{
    None,       // 선택 없음 or 미지원 파일
    Image,      // 이미지 미리보기
    Text,       // 텍스트 파일 내용
    Pdf,        // PDF 첫 페이지
    Media,      // 비디오/오디오 재생
    Folder,     // 폴더 정보
    Generic     // 메타데이터만 표시 (미리보기 불가 파일)
}
```

### 4.4 PreviewPanelView UI 구조

```
PreviewPanelView (UserControl)
├── Header: 파일명 + 아이콘
├── Preview Area (PreviewType에 따라 전환)
│   ├── Image: <Image> 컨트롤
│   ├── Text: <ScrollViewer><TextBlock> (Consolas 폰트)
│   ├── PDF: <Image> (렌더링된 페이지)
│   ├── Media: <MediaPlayerElement> (트랜스포트 컨트롤)
│   ├── Folder: 폴더 아이콘 + 항목 수
│   └── Generic: 파일 타입 아이콘
├── Separator
└── Metadata Section
    ├── 종류 (Kind)
    ├── 크기 (Size)
    ├── 생성일 (Created)
    ├── 수정일 (Modified)
    ├── 해상도 (이미지/비디오)
    └── 재생시간 (미디어)
```

### 4.5 MainViewModel 확장

```csharp
// 미리보기 패널 상태 (양쪽 패널 독립)
[ObservableProperty] private bool _isLeftPreviewEnabled = false;
[ObservableProperty] private bool _isRightPreviewEnabled = false;

// 단일 모드용 (Split 비활성 시)
public bool IsPreviewEnabled => ActivePane == ActivePane.Left
    ? IsLeftPreviewEnabled : IsRightPreviewEnabled;

// 미리보기 토글
public void TogglePreview()
{
    if (ActivePane == ActivePane.Left)
        IsLeftPreviewEnabled = !IsLeftPreviewEnabled;
    else
        IsRightPreviewEnabled = !IsRightPreviewEnabled;
}
```

### 4.6 Split View 시나리오

#### 시나리오 1: 단일 모드 (Split View OFF)
```
[Sidebar] | [Explorer (Miller/Details/Icon)] | [GridSplitter] | [Preview Panel]
```
- 하나의 미리보기 패널만 존재
- `IsLeftPreviewEnabled`로 토글

#### 시나리오 2: 분할 모드 (Split View ON), 양쪽 모두 미리보기 ON
```
[Sidebar] | [Left Explorer] | [Left Preview] | [Splitter] | [Right Explorer] | [Right Preview]
```
- 각 패널에 독립적인 미리보기
- 좌측 파일 선택 → 좌측 미리보기 업데이트
- 우측 파일 선택 → 우측 미리보기 업데이트

#### 시나리오 3: 분할 모드, 한쪽만 미리보기 ON
```
[Sidebar] | [Left Explorer] | [Left Preview] | [Splitter] | [Right Explorer (Full Width)]
```
- 미리보기 토글은 Active Pane 기준

#### 시나리오 4: 분할 모드, 다른 뷰 모드 조합
```
[Sidebar] | [Left: Miller Columns] | [Left Preview] | [Splitter] | [Right: Details View] | [Right Preview]
```
- 뷰 모드와 미리보기 패널은 독립적
- 어떤 뷰 모드에서든 미리보기 패널 사용 가능

---

## 5. 구현 계획

### Phase 1: 핵심 기반 (P0)

#### Step 1-1: 모델 & 서비스
- `Models/FilePreviewInfo.cs` 생성
- `Models/PreviewType.cs` 생성
- `Services/PreviewService.cs` 생성
  - `GetMetadataAsync()` - 기본 메타데이터
  - `LoadImageAsync()` - 이미지 썸네일/미리보기
  - `CancelPending()` - 취소 관리

#### Step 1-2: ViewModel
- `ViewModels/PreviewPanelViewModel.cs` 생성
  - `UpdatePreviewAsync()` - 선택 항목 변경 시 미리보기 업데이트
  - `ClearPreview()` - 미리보기 초기화
  - Debounce 로직 (200ms)
  - CancellationTokenSource 관리

#### Step 1-3: UI
- `Views/PreviewPanelView.xaml/.cs` 생성
  - 이미지 미리보기 영역
  - 메타데이터 표시 영역
  - 로딩 상태 표시

#### Step 1-4: 통합
- `MainViewModel` 확장 (IsLeftPreviewEnabled, IsRightPreviewEnabled)
- `MainWindow.xaml` 레이아웃 변경 (각 패널 내부에 Preview 추가)
- `MainWindow.xaml.cs` 이벤트 연결 (선택 변경 → 미리보기 업데이트)
- 토글 버튼 + 단축키 (`Ctrl+Shift+P`)

### Phase 2: 콘텐츠 미리보기 확장 (P1)

#### Step 2-1: 텍스트 미리보기
- `PreviewService.LoadTextAsync()` 구현
- 인코딩 자동 감지
- 50KB 제한, 잘림 표시

#### Step 2-2: PDF 미리보기
- `PreviewService.LoadPdfPageAsync()` 구현
- Windows.Data.Pdf API 사용
- 첫 페이지만 렌더링

#### Step 2-3: 미디어 미리보기
- `PreviewService.LoadMediaSourceAsync()` 구현
- MediaPlayerElement 통합
- 재생/정지 관리 (선택 변경 시 자동 정지)

#### Step 2-4: 폴더 정보
- 폴더 선택 시 항목 수 표시
- 폴더 아이콘 표시

### Phase 3: 확장 메타데이터 (P2)

#### Step 3-1: 이미지 EXIF
- `GetImagePropertiesAsync()` 활용
- 카메라, 렌즈, ISO, 조리개 등

#### Step 3-2: 미디어 태그
- `GetVideoPropertiesAsync()` / `GetMusicPropertiesAsync()` 활용
- 아티스트, 앨범, 비트레이트 등

---

## 6. UI/UX 설계 방향

### 6.1 미리보기 패널 레이아웃
- **기본 너비**: 280px
- **최소 너비**: 200px
- **최대 너비**: 탐색기 영역의 50%
- **배경**: `SpanBgLayer2Brush` (기존 사이드바와 동일)
- **분리선**: `GridSplitter` 2px (드래그로 너비 조절)

### 6.2 미리보기 영역
- 이미지: `Stretch="Uniform"`, 패널 너비에 맞춤, 최대 높이 400px
- 텍스트: `Consolas` 폰트, 스크롤 가능, 줄번호 없음
- PDF: 렌더링된 이미지, 패널 너비에 맞춤
- 미디어: `MediaPlayerElement`, 컴팩트 트랜스포트 컨트롤

### 6.3 메타데이터 섹션
- 라벨-값 쌍 (좌측 라벨 40%, 우측 값 60%)
- 그룹 구분선
- 텍스트 선택 가능 (`IsTextSelectionEnabled="True"`)

### 6.4 토글 버튼
- 위치: 뷰 모드 버튼 옆 (Unified Bar)
- 아이콘: RemixIcon에서 적절한 사이드바/패널 아이콘 사용
- 단축키: `Ctrl+Shift+P` (macOS Finder와 유사)

### 6.5 빈 상태
- 파일 미선택 시: "파일을 선택하면 미리보기가 표시됩니다" 안내 텍스트
- 미지원 파일: 파일 타입 아이콘 + 기본 메타데이터만 표시

---

## 7. 단축키 계획

| 단축키 | 동작 | 비고 |
|--------|------|------|
| `Ctrl+Shift+P` | 미리보기 패널 토글 | Active Pane 기준 |
| `Space` | Quick Look (Phase 2) | 추후 구현 |

---

## 8. 상태 저장 & 복원

| 설정 | LocalSettings 키 | 기본값 |
|------|------------------|--------|
| 좌측 미리보기 활성 | `IsLeftPreviewEnabled` | `false` |
| 우측 미리보기 활성 | `IsRightPreviewEnabled` | `false` |
| 좌측 미리보기 너비 | `LeftPreviewWidth` | `280` |
| 우측 미리보기 너비 | `RightPreviewWidth` | `280` |

---

## 9. 수정 파일 요약

| 파일 | 작업 | Phase |
|------|------|-------|
| `Models/FilePreviewInfo.cs` | **신규** - 메타데이터 모델 | 1 |
| `Models/PreviewType.cs` | **신규** - 미리보기 타입 열거형 | 1 |
| `Services/PreviewService.cs` | **신규** - 미리보기 로딩 서비스 | 1 |
| `ViewModels/PreviewPanelViewModel.cs` | **신규** - 미리보기 패널 VM | 1 |
| `Views/PreviewPanelView.xaml/.cs` | **신규** - 미리보기 패널 UI | 1 |
| `ViewModels/MainViewModel.cs` | **수정** - IsPreviewEnabled, TogglePreview | 1 |
| `MainWindow.xaml` | **수정** - 패널 레이아웃, 토글 버튼 | 1 |
| `MainWindow.xaml.cs` | **수정** - 이벤트 연결, 단축키, Cleanup | 1 |
| `Assets/Styles/Icons.xaml` | **수정** - 미리보기 아이콘 추가 | 1 |

---

## 10. 검증 체크리스트

### Phase 1 (핵심)
- [ ] `Ctrl+Shift+P` → 미리보기 패널 토글 (표시/숨김)
- [ ] 이미지 파일 선택 → 이미지 미리보기 표시
- [ ] 파일 선택 → 기본 메타데이터 표시 (이름, 크기, 종류, 날짜)
- [ ] GridSplitter로 미리보기 패널 너비 조절
- [ ] Split View에서 양쪽 패널 독립 미리보기 토글
- [ ] Split View에서 좌측 선택 → 좌측 미리보기만 업데이트
- [ ] Split View에서 우측 선택 → 우측 미리보기만 업데이트
- [ ] 빠른 선택 변경 시 이전 로딩 취소 (flickering 없음)
- [ ] 미선택 상태에서 안내 텍스트 표시
- [ ] 앱 종료 시 크래시 없음

### Phase 2 (콘텐츠 확장)
- [ ] 텍스트 파일 내용 미리보기
- [ ] PDF 첫 페이지 미리보기
- [ ] 비디오 파일 재생 가능
- [ ] 오디오 파일 재생 가능
- [ ] 폴더 선택 시 항목 수 표시
- [ ] 파일 선택 변경 시 미디어 자동 정지

### Phase 3 (확장 메타데이터)
- [ ] 이미지 EXIF 정보 표시 (해상도, 카메라 등)
- [ ] 비디오 메타데이터 표시 (해상도, 재생시간, 비트레이트)
- [ ] 오디오 태그 표시 (아티스트, 앨범, 장르)

---

## 11. 리스크 & 완화

| 리스크 | 영향 | 완화 방안 |
|--------|------|----------|
| 대용량 이미지로 메모리 증가 | 중 | 썸네일 캐시 사용, 최대 2048px 제한 |
| 빈번한 선택 변경으로 성능 저하 | 중 | CancellationToken + 200ms 디바운스 |
| PDF 렌더링 품질 제한 | 낮 | Windows.Data.Pdf 기본 기능 사용, 추후 서드파티 고려 |
| MediaPlayerElement 리소스 누수 | 중 | Dispose 패턴 + 선택 변경 시 정리 |
| Split View + Preview로 레이아웃 과밀 | 중 | 최소 너비 제한, 자동 축소 고려 |
| StorageFile API 성능 | 낮 | 기본 메타데이터는 System.IO 사용 |
