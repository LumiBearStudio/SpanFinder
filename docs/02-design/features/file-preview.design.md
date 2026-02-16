# File Preview Panel - Design Document

**작성일**: 2026-02-16
**Plan 참조**: `docs/01-plan/features/file-preview.plan.md`
**목표**: macOS Finder 스타일 파일 정보/미리보기 패널 구현 (Split View 양쪽 독립 지원)

---

## 1. 구현 범위 (Phase 1 - P0/P1)

이번 구현에서 다루는 범위:
- 미리보기 패널 레이아웃 (각 패널 내부 우측 사이드바)
- 토글 버튼 + 단축키 (`Ctrl+Shift+P`)
- 이미지 미리보기 (JPG, PNG, BMP, GIF, TIFF, WEBP, ICO)
- 텍스트 미리보기 (TXT, CS, JSON, XML, MD, LOG, INI, CFG, YAML, TOML 등)
- PDF 첫 페이지 미리보기
- 미디어 미리보기 (MP4, MP3, WAV, WMA, AVI, MKV, FLAC, OGG)
- 폴더 정보 (항목 수)
- 기본 메타데이터 (이름, 크기, 종류, 생성일, 수정일, 해상도, 재생시간)
- Split View 양쪽 패널 독립 미리보기
- 상태 저장/복원

---

## 2. 파일 구조

### 신규 파일

```
src/Span/Span/
├── Models/
│   └── PreviewType.cs              # 미리보기 타입 열거형
├── Services/
│   └── PreviewService.cs           # 미리보기 로딩 + 메타데이터 서비스
├── ViewModels/
│   └── PreviewPanelViewModel.cs    # 미리보기 패널 상태 관리 VM
└── Views/
    └── PreviewPanelView.xaml/.cs   # 미리보기 패널 UI (UserControl)
```

### 수정 파일

```
src/Span/Span/
├── ViewModels/MainViewModel.cs     # IsLeftPreviewEnabled, IsRightPreviewEnabled 추가
├── MainWindow.xaml                 # 패널 레이아웃에 Preview 통합
├── MainWindow.xaml.cs              # 선택 변경 → 미리보기 업데이트, 단축키, Cleanup
└── Assets/Styles/Icons.xaml        # 미리보기 토글 아이콘 추가 (선택)
```

---

## 3. 모델 설계

### 3.1 PreviewType.cs

```csharp
// Models/PreviewType.cs
namespace Span.Models
{
    public enum PreviewType
    {
        None,       // 선택 없음
        Image,      // 이미지 미리보기 (BitmapImage)
        Text,       // 텍스트 파일 내용 (string)
        Pdf,        // PDF 첫 페이지 (렌더링된 BitmapImage)
        Media,      // 비디오/오디오 (MediaSource)
        Folder,     // 폴더 정보
        Generic     // 미지원 파일 (메타데이터만)
    }
}
```

---

## 4. 서비스 설계

### 4.1 PreviewService.cs

순수 데이터 로딩 서비스. UI 의존성 없음. DI 등록: `Singleton`.

```csharp
// Services/PreviewService.cs
namespace Span.Services
{
    public class PreviewService
    {
        // --- 파일 타입 판별 ---

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tiff", ".tif", ".webp", ".ico"
        };

        private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".cs", ".json", ".xml", ".md", ".log", ".ini", ".cfg", ".yaml", ".yml",
            ".toml", ".html", ".htm", ".css", ".js", ".ts", ".py", ".java", ".cpp", ".c",
            ".h", ".go", ".rs", ".sh", ".bat", ".ps1", ".sql", ".csv", ".tsv", ".gitignore",
            ".editorconfig", ".env", ".dockerfile", ".xaml", ".csproj", ".sln"
        };

        private static readonly HashSet<string> PdfExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".pdf"
        };

        private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".mp3", ".wav", ".wma", ".avi", ".mkv", ".flac", ".ogg", ".aac",
            ".m4a", ".m4v", ".mov", ".wmv", ".webm"
        };

        public PreviewType GetPreviewType(string filePath);
        // → 확장자 기반 판별. IFileSystemItem이 FolderItem이면 Folder 반환.

        // --- 메타데이터 ---

        public FilePreviewMetadata GetBasicMetadata(string filePath);
        // → System.IO.FileInfo 사용 (동기, 빠름)
        // → 반환: Name, Size, Created, Modified, Extension, IsReadOnly

        public async Task<ImageMetadata?> GetImageMetadataAsync(string filePath, CancellationToken ct);
        // → StorageFile.Properties.GetImagePropertiesAsync()
        // → 반환: Width, Height, DateTaken, CameraManufacturer, CameraModel

        public async Task<MediaMetadata?> GetMediaMetadataAsync(string filePath, CancellationToken ct);
        // → GetVideoPropertiesAsync() or GetMusicPropertiesAsync()
        // → 반환: Duration, Bitrate, Width, Height (video), Artist, Album (audio)

        public int GetFolderItemCount(string folderPath);
        // → Directory.EnumerateFileSystemEntries().Count() (hidden 제외)

        // --- 미리보기 콘텐츠 로딩 ---

        public async Task<BitmapImage?> LoadImagePreviewAsync(string filePath, uint maxSize, CancellationToken ct);
        // → StorageFile.GetThumbnailAsync(ThumbnailMode.SingleItem, maxSize)
        // → 실패 시 BitmapImage.SetSourceAsync(stream) 풀백

        public async Task<string?> LoadTextPreviewAsync(string filePath, int maxChars, CancellationToken ct);
        // → StreamReader with detectEncodingFromByteOrderMarks
        // → maxChars 제한 (기본 50000), 초과 시 "[미리보기 잘림...]" 추가

        public async Task<BitmapImage?> LoadPdfPreviewAsync(string filePath, CancellationToken ct);
        // → Windows.Data.Pdf.PdfDocument.LoadFromFileAsync()
        // → GetPage(0) → RenderToStreamAsync()
        // → 렌더링된 이미지를 BitmapImage로 반환

        public async Task<MediaSource?> LoadMediaSourceAsync(string filePath, CancellationToken ct);
        // → StorageFile.GetFileFromPathAsync() → MediaSource.CreateFromStorageFile()
    }

    // --- 메타데이터 레코드 ---

    public record FilePreviewMetadata
    {
        public string FileName { get; init; } = "";
        public long Size { get; init; }
        public DateTime Created { get; init; }
        public DateTime Modified { get; init; }
        public string Extension { get; init; } = "";
        public bool IsReadOnly { get; init; }

        public string SizeFormatted => FormatBytes(Size);

        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1) { order++; size /= 1024; }
            return $"{size:0.##} {sizes[order]}";
        }
    }

    public record ImageMetadata(uint Width, uint Height, DateTimeOffset? DateTaken,
                                 string? CameraManufacturer, string? CameraModel);

    public record MediaMetadata(TimeSpan Duration, uint Bitrate,
                                 uint? Width, uint? Height,  // video only
                                 string? Artist, string? Album); // audio only
}
```

### 4.2 DI 등록 (App.xaml.cs)

```csharp
services.AddSingleton<PreviewService>();
```

---

## 5. ViewModel 설계

### 5.1 PreviewPanelViewModel.cs

```csharp
// ViewModels/PreviewPanelViewModel.cs
namespace Span.ViewModels
{
    public partial class PreviewPanelViewModel : ObservableObject, IDisposable
    {
        private readonly PreviewService _previewService;
        private CancellationTokenSource? _currentCts;
        private System.Threading.Timer? _debounceTimer;
        private const int DebounceMs = 200;

        // --- 상태 ---

        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private bool _hasContent;  // 미리보기 가능한 항목이 있는지

        // --- 메타데이터 (항상 표시) ---

        [ObservableProperty] private string _fileName = "";
        [ObservableProperty] private string _fileIconGlyph = "";
        [ObservableProperty] private Brush? _fileIconBrush;
        [ObservableProperty] private string _fileType = "";
        [ObservableProperty] private string _fileSizeFormatted = "";
        [ObservableProperty] private string _dateCreated = "";
        [ObservableProperty] private string _dateModified = "";

        // --- 타입별 추가 정보 ---

        [ObservableProperty] private string _dimensions = "";         // 이미지: "1920 x 1080"
        [ObservableProperty] private string _duration = "";           // 미디어: "00:03:42"
        [ObservableProperty] private string _folderItemCount = "";    // 폴더: "42개 항목"
        [ObservableProperty] private string _artist = "";             // 오디오
        [ObservableProperty] private string _album = "";              // 오디오

        // --- 미리보기 콘텐츠 (하나만 활성) ---

        [ObservableProperty] private PreviewType _currentPreviewType = PreviewType.None;
        [ObservableProperty] private BitmapImage? _imagePreview;
        [ObservableProperty] private string? _textPreview;
        [ObservableProperty] private BitmapImage? _pdfPreview;
        [ObservableProperty] private MediaSource? _mediaSource;

        // --- 생성자 ---

        public PreviewPanelViewModel(PreviewService previewService)
        {
            _previewService = previewService;
        }

        // --- 핵심 메서드 ---

        /// <summary>
        /// 선택 항목 변경 시 호출. 디바운싱 적용.
        /// </summary>
        public void OnSelectionChanged(FileSystemViewModel? selectedItem)
        {
            // 타이머 리셋 (디바운싱)
            _debounceTimer?.Dispose();

            if (selectedItem == null)
            {
                ClearPreview();
                return;
            }

            _debounceTimer = new System.Threading.Timer(
                async _ => await UpdatePreviewAsync(selectedItem),
                null,
                DebounceMs,
                System.Threading.Timeout.Infinite);
        }

        /// <summary>
        /// 미리보기 로딩 (비동기, 취소 가능)
        /// </summary>
        private async Task UpdatePreviewAsync(FileSystemViewModel item)
        {
            // 이전 로딩 취소
            _currentCts?.Cancel();
            _currentCts = new CancellationTokenSource();
            var ct = _currentCts.Token;

            // DispatcherQueue에서 UI 업데이트 필요
            // (Timer 콜백은 ThreadPool 스레드)

            try
            {
                IsLoading = true;
                HasContent = true;

                // 1. 기본 메타데이터 (동기, 빠름)
                SetBasicInfo(item);

                // 2. 타입별 미리보기 로딩
                var previewType = _previewService.GetPreviewType(item.Path);
                ClearPreviewContent(); // 이전 콘텐츠 정리
                CurrentPreviewType = previewType;

                ct.ThrowIfCancellationRequested();

                switch (previewType)
                {
                    case PreviewType.Folder:
                        LoadFolderInfo(item.Path);
                        break;

                    case PreviewType.Image:
                        ImagePreview = await _previewService.LoadImagePreviewAsync(item.Path, 1024, ct);
                        // 이미지 메타데이터
                        var imgMeta = await _previewService.GetImageMetadataAsync(item.Path, ct);
                        if (imgMeta != null)
                            Dimensions = $"{imgMeta.Width} x {imgMeta.Height}";
                        break;

                    case PreviewType.Text:
                        TextPreview = await _previewService.LoadTextPreviewAsync(item.Path, 50000, ct);
                        break;

                    case PreviewType.Pdf:
                        PdfPreview = await _previewService.LoadPdfPreviewAsync(item.Path, ct);
                        break;

                    case PreviewType.Media:
                        MediaSource = await _previewService.LoadMediaSourceAsync(item.Path, ct);
                        var mediaMeta = await _previewService.GetMediaMetadataAsync(item.Path, ct);
                        if (mediaMeta != null)
                        {
                            Duration = mediaMeta.Duration.ToString(@"hh\:mm\:ss");
                            if (mediaMeta.Width.HasValue && mediaMeta.Height.HasValue)
                                Dimensions = $"{mediaMeta.Width} x {mediaMeta.Height}";
                            if (!string.IsNullOrEmpty(mediaMeta.Artist))
                                Artist = mediaMeta.Artist;
                            if (!string.IsNullOrEmpty(mediaMeta.Album))
                                Album = mediaMeta.Album;
                        }
                        break;

                    case PreviewType.Generic:
                        // 메타데이터만 표시, 미리보기 없음
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                // 정상 취소 (빠른 선택 변경 시)
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[PreviewPanel] Error: {ex.Message}");
                CurrentPreviewType = PreviewType.Generic;
            }
            finally
            {
                if (!ct.IsCancellationRequested)
                    IsLoading = false;
            }
        }

        private void SetBasicInfo(FileSystemViewModel item)
        {
            FileName = item.Name;
            FileIconGlyph = item.IconGlyph;
            FileIconBrush = item.IconBrush;
            FileType = item.FileType;

            if (item is FolderViewModel)
            {
                FileSizeFormatted = "";
                DateCreated = "";
                DateModified = item.DateModified;
            }
            else
            {
                var metadata = _previewService.GetBasicMetadata(item.Path);
                FileSizeFormatted = metadata.SizeFormatted;
                DateCreated = metadata.Created.ToString("yyyy-MM-dd HH:mm");
                DateModified = metadata.Modified.ToString("yyyy-MM-dd HH:mm");
            }
        }

        private void LoadFolderInfo(string path)
        {
            var count = _previewService.GetFolderItemCount(path);
            FolderItemCount = $"{count}개 항목";
        }

        private void ClearPreviewContent()
        {
            // 이전 미디어 소스 정리
            if (MediaSource != null)
            {
                MediaSource.Dispose();
                MediaSource = null;
            }
            ImagePreview = null;
            TextPreview = null;
            PdfPreview = null;
            Dimensions = "";
            Duration = "";
            FolderItemCount = "";
            Artist = "";
            Album = "";
        }

        public void ClearPreview()
        {
            ClearPreviewContent();
            FileName = "";
            FileType = "";
            FileSizeFormatted = "";
            DateCreated = "";
            DateModified = "";
            CurrentPreviewType = PreviewType.None;
            HasContent = false;
            IsLoading = false;
        }

        public void Dispose()
        {
            _currentCts?.Cancel();
            _currentCts?.Dispose();
            _debounceTimer?.Dispose();
            ClearPreviewContent();
        }
    }
}
```

---

## 6. View 설계 (PreviewPanelView.xaml)

### 6.1 UserControl 구조

```
PreviewPanelView (UserControl, Background=SpanBgLayer2Brush)
├── ScrollViewer (전체 스크롤)
│   └── StackPanel (Vertical)
│       │
│       ├── [Header Section]
│       │   ├── FileIcon (FontIcon, 48px)
│       │   └── FileName (TextBlock, Bold)
│       │
│       ├── [Preview Area] (PreviewType에 따라 하나만 표시)
│       │   ├── Image: <Image Stretch="Uniform" MaxHeight="400"/>
│       │   ├── Text: <ScrollViewer MaxHeight="300"><TextBlock FontFamily="Consolas"/></ScrollViewer>
│       │   ├── PDF: <Image Stretch="Uniform" MaxHeight="500"/>
│       │   ├── Media: <MediaPlayerElement AreTransportControlsEnabled="True" AutoPlay="False" MaxHeight="300"/>
│       │   ├── Folder: <StackPanel> 폴더 아이콘(64px) + 항목 수 </StackPanel>
│       │   └── Generic: 파일 아이콘(64px) 표시
│       │
│       ├── [Separator] (Rectangle Height=1)
│       │
│       └── [Metadata Section]
│           ├── Row: "종류" → FileType
│           ├── Row: "크기" → FileSizeFormatted
│           ├── Row: "생성일" → DateCreated
│           ├── Row: "수정일" → DateModified
│           ├── Row: "해상도" → Dimensions (이미지/비디오, 조건부)
│           ├── Row: "재생시간" → Duration (미디어, 조건부)
│           ├── Row: "아티스트" → Artist (오디오, 조건부)
│           └── Row: "앨범" → Album (오디오, 조건부)
│
└── [Empty State] (HasContent=false일 때)
    └── TextBlock "파일을 선택하면\n미리보기가 표시됩니다"
```

### 6.2 Visibility 패턴

각 미리보기 영역은 `CurrentPreviewType` 바인딩으로 제어:

```csharp
// PreviewPanelView.xaml.cs
public Visibility IsPreviewType(PreviewType current, PreviewType target)
    => current == target ? Visibility.Visible : Visibility.Collapsed;
```

```xml
<Image Visibility="{x:Bind IsPreviewType(ViewModel.CurrentPreviewType, models:PreviewType.Image), Mode=OneWay}"/>
```

### 6.3 메타데이터 행 템플릿

```xml
<!-- 재사용 가능한 메타데이터 행 -->
<Grid Height="24" Padding="16,0">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="80"/>   <!-- Label (고정) -->
        <ColumnDefinition Width="*"/>    <!-- Value -->
    </Grid.ColumnDefinitions>
    <TextBlock Text="종류" FontSize="12"
               Foreground="{ThemeResource SpanTextTertiaryBrush}"/>
    <TextBlock Grid.Column="1" Text="{x:Bind ViewModel.FileType, Mode=OneWay}"
               FontSize="12" IsTextSelectionEnabled="True"
               Foreground="{ThemeResource SpanTextPrimaryBrush}"/>
</Grid>
```

### 6.4 코드 비하인드

```csharp
// Views/PreviewPanelView.xaml.cs
public sealed partial class PreviewPanelView : UserControl
{
    public PreviewPanelViewModel? ViewModel { get; private set; }

    public PreviewPanelView()
    {
        this.InitializeComponent();
    }

    public void Initialize(PreviewPanelViewModel viewModel)
    {
        ViewModel = viewModel;
        RootPanel.DataContext = ViewModel;
    }

    /// <summary>
    /// 외부에서 선택 변경 시 호출
    /// </summary>
    public void UpdatePreview(FileSystemViewModel? selectedItem)
    {
        ViewModel?.OnSelectionChanged(selectedItem);
    }

    /// <summary>
    /// 미디어 재생 중지 (선택 변경 또는 패널 닫힐 때)
    /// </summary>
    public void StopMedia()
    {
        if (PreviewMediaPlayer?.MediaPlayer != null)
        {
            PreviewMediaPlayer.MediaPlayer.Pause();
            PreviewMediaPlayer.Source = null;
        }
    }

    public void Cleanup()
    {
        StopMedia();
        ViewModel?.Dispose();
        ViewModel = null;
        RootPanel.DataContext = null;
    }
}
```

---

## 7. MainViewModel 확장

### 7.1 추가 프로퍼티

```csharp
// ViewModels/MainViewModel.cs 추가

// 미리보기 패널 상태 (양쪽 독립)
[ObservableProperty]
private bool _isLeftPreviewEnabled = false;

[ObservableProperty]
private bool _isRightPreviewEnabled = false;

// 현재 활성 패널의 미리보기 상태 (편의 프로퍼티)
public bool IsActivePreviewEnabled =>
    ActivePane == ActivePane.Left ? IsLeftPreviewEnabled : IsRightPreviewEnabled;
```

### 7.2 토글 메서드

```csharp
public void TogglePreview()
{
    if (ActivePane == ActivePane.Left)
        IsLeftPreviewEnabled = !IsLeftPreviewEnabled;
    else
        IsRightPreviewEnabled = !IsRightPreviewEnabled;

    SavePreviewState();
}
```

### 7.3 상태 저장/복원

```csharp
private void SavePreviewState()
{
    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
    settings.Values["IsLeftPreviewEnabled"] = IsLeftPreviewEnabled;
    settings.Values["IsRightPreviewEnabled"] = IsRightPreviewEnabled;
}

// MainWindow.xaml.cs에서 호출 (Cleanup 시 현재 너비 저장)
public void SavePreviewWidths(double leftWidth, double rightWidth)
{
    var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
    settings.Values["LeftPreviewWidth"] = leftWidth;
    settings.Values["RightPreviewWidth"] = rightWidth;
}

// LoadViewModePreference()에 추가:
if (settings.Values.TryGetValue("IsLeftPreviewEnabled", out var leftPrev))
    IsLeftPreviewEnabled = (bool)leftPrev;
if (settings.Values.TryGetValue("IsRightPreviewEnabled", out var rightPrev))
    IsRightPreviewEnabled = (bool)rightPrev;
```

### 7.4 Cleanup 확장

```csharp
public void Cleanup()
{
    // ... 기존 코드 ...
    SavePreviewState();  // 추가
    // ... 기존 코드 ...
}
```

---

## 8. MainWindow.xaml 레이아웃 변경

### 8.1 각 패널 내부에 미리보기 삽입

**핵심 변경**: `Left Pane Content` Grid.Row="1" 내부를 2열 Grid로 변경

#### 변경 전 (현재)
```xml
<!-- Left Pane Content -->
<Grid Grid.Row="1">
    <ScrollViewer x:Name="MillerScrollViewer" .../>
    <views:DetailsModeView x:Name="DetailsView" .../>
    <views:IconModeView x:Name="IconView" .../>
    <views:HomeModeView x:Name="HomeView" .../>
</Grid>
```

#### 변경 후
```xml
<!-- Left Pane Content -->
<Grid Grid.Row="1">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>                                    <!-- Explorer -->
        <ColumnDefinition x:Name="LeftPreviewSplitterCol" Width="0"/>   <!-- Splitter -->
        <ColumnDefinition x:Name="LeftPreviewCol" Width="0"/>           <!-- Preview -->
    </Grid.ColumnDefinitions>

    <!-- Explorer Views (Grid.Column="0") -->
    <Grid Grid.Column="0">
        <ScrollViewer x:Name="MillerScrollViewer" .../>
        <views:DetailsModeView x:Name="DetailsView" .../>
        <views:IconModeView x:Name="IconView" .../>
        <views:HomeModeView x:Name="HomeView" .../>
    </Grid>

    <!-- Preview Splitter -->
    <controls:GridSplitter Grid.Column="1" Width="2"
        Background="{ThemeResource SpanBorderSubtleBrush}"
        Visibility="{x:Bind PreviewVisible(ViewModel.IsLeftPreviewEnabled), Mode=OneWay}"/>

    <!-- Preview Panel -->
    <views:PreviewPanelView x:Name="LeftPreviewPanel" Grid.Column="2"
        MinWidth="200"
        Visibility="{x:Bind PreviewVisible(ViewModel.IsLeftPreviewEnabled), Mode=OneWay}"/>
</Grid>
```

#### Right Pane 동일 패턴

```xml
<!-- Right Pane Content (Grid.Row="1") -->
<Grid Grid.Row="1">
    <Grid.ColumnDefinitions>
        <ColumnDefinition Width="*"/>
        <ColumnDefinition x:Name="RightPreviewSplitterCol" Width="0"/>
        <ColumnDefinition x:Name="RightPreviewCol" Width="0"/>
    </Grid.ColumnDefinitions>

    <Grid Grid.Column="0">
        <ScrollViewer x:Name="MillerScrollViewerRight" .../>
        <views:DetailsModeView x:Name="DetailsViewRight" .../>
        <views:IconModeView x:Name="IconViewRight" .../>
    </Grid>

    <controls:GridSplitter Grid.Column="1" Width="2"
        Background="{ThemeResource SpanBorderSubtleBrush}"
        Visibility="{x:Bind PreviewVisible(ViewModel.IsRightPreviewEnabled), Mode=OneWay}"/>

    <views:PreviewPanelView x:Name="RightPreviewPanel" Grid.Column="2"
        MinWidth="200"
        Visibility="{x:Bind PreviewVisible(ViewModel.IsRightPreviewEnabled), Mode=OneWay}"/>
</Grid>
```

### 8.2 토글 버튼 추가 (Unified Bar Commands)

Split View 토글 버튼 옆에 미리보기 토글 버튼 추가:

```xml
<!-- Preview Toggle (Split View 버튼 옆) -->
<Button x:Name="PreviewToggleButton" Style="{StaticResource UnifiedButtonStyle}"
        ToolTipService.ToolTip="미리보기 (Ctrl+Shift+P)"
        Click="OnPreviewToggleClick">
    <FontIcon Glyph="&#xE8A1;" FontSize="14"/>  <!-- Segoe: PreviewLink -->
</Button>
```

---

## 9. MainWindow.xaml.cs 변경

### 9.1 초기화

```csharp
// 생성자 또는 Loaded에서
private void InitializePreviewPanels()
{
    var previewService = App.Current.Services.GetRequiredService<PreviewService>();

    var leftPreviewVm = new PreviewPanelViewModel(previewService);
    LeftPreviewPanel.Initialize(leftPreviewVm);

    var rightPreviewVm = new PreviewPanelViewModel(previewService);
    RightPreviewPanel.Initialize(rightPreviewVm);
}
```

### 9.2 선택 변경 → 미리보기 업데이트

기존 `OnMillerColumnSelectionChanged` 메서드 끝에 미리보기 업데이트 추가:

```csharp
private void OnMillerColumnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    // ... 기존 코드 ...

    // 미리보기 업데이트
    UpdatePreviewForSelection(folderVm.SelectedChild);
}

private void UpdatePreviewForSelection(FileSystemViewModel? selectedItem)
{
    if (ViewModel.ActivePane == ActivePane.Left && ViewModel.IsLeftPreviewEnabled)
    {
        LeftPreviewPanel.UpdatePreview(selectedItem);
    }
    else if (ViewModel.ActivePane == ActivePane.Right && ViewModel.IsRightPreviewEnabled)
    {
        RightPreviewPanel.UpdatePreview(selectedItem);
    }
}
```

### 9.3 FolderViewModel.SelectedChild 변경 감지 (Details/Icon 모드)

Details/Icon 모드에서는 `OnMillerColumnSelectionChanged`가 아닌 별도 방법으로 선택을 감지해야 함.

**방법**: `ExplorerViewModel.CurrentFolder.PropertyChanged` 구독

```csharp
// 패널별로 CurrentFolder의 SelectedChild 변경을 감시
private void SubscribeToSelectionForPreview(ExplorerViewModel explorer, PreviewPanelView previewPanel, bool isLeftPane)
{
    FolderViewModel? lastWatchedFolder = null;

    explorer.PropertyChanged += (s, e) =>
    {
        if (e.PropertyName == nameof(ExplorerViewModel.CurrentFolder))
        {
            // 이전 폴더 구독 해제
            if (lastWatchedFolder != null)
                lastWatchedFolder.PropertyChanged -= OnWatchedFolderPropertyChanged;

            lastWatchedFolder = explorer.CurrentFolder;

            // 새 폴더 구독
            if (lastWatchedFolder != null)
                lastWatchedFolder.PropertyChanged += OnWatchedFolderPropertyChanged;
        }
    };

    void OnWatchedFolderPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FolderViewModel.SelectedChild)) return;
        if (sender is not FolderViewModel folder) return;

        bool shouldUpdate = isLeftPane ? ViewModel.IsLeftPreviewEnabled : ViewModel.IsRightPreviewEnabled;
        if (shouldUpdate)
        {
            previewPanel.UpdatePreview(folder.SelectedChild);
        }
    }
}
```

### 9.4 단축키

```csharp
// OnGlobalKeyDown에 추가
case VirtualKey.P when ctrl && shift:
    ViewModel.TogglePreview();
    TogglePreviewPanel();
    e.Handled = true;
    break;
```

### 9.5 TogglePreviewPanel 구현

```csharp
private void TogglePreviewPanel()
{
    if (ViewModel.ActivePane == ActivePane.Left)
    {
        if (ViewModel.IsLeftPreviewEnabled)
        {
            // 열기: Width 설정
            LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
            LeftPreviewCol.Width = new GridLength(280, GridUnitType.Pixel);
            // 현재 선택 항목으로 미리보기 즉시 업데이트
            var selected = ViewModel.LeftExplorer.CurrentFolder?.SelectedChild;
            LeftPreviewPanel.UpdatePreview(selected);
        }
        else
        {
            // 닫기
            LeftPreviewSplitterCol.Width = new GridLength(0);
            LeftPreviewCol.Width = new GridLength(0);
            LeftPreviewPanel.StopMedia();
        }
    }
    else
    {
        if (ViewModel.IsRightPreviewEnabled)
        {
            RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
            RightPreviewCol.Width = new GridLength(280, GridUnitType.Pixel);
            var selected = ViewModel.RightExplorer.CurrentFolder?.SelectedChild;
            RightPreviewPanel.UpdatePreview(selected);
        }
        else
        {
            RightPreviewSplitterCol.Width = new GridLength(0);
            RightPreviewCol.Width = new GridLength(0);
            RightPreviewPanel.StopMedia();
        }
    }
}
```

### 9.6 Loaded에서 미리보기 상태 복원

```csharp
// Loaded 이벤트 핸들러에 추가
private void RestorePreviewState()
{
    if (ViewModel.IsLeftPreviewEnabled)
    {
        LeftPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
        LeftPreviewCol.Width = new GridLength(
            LoadPreviewWidth("LeftPreviewWidth", 280), GridUnitType.Pixel);
    }
    if (ViewModel.IsRightPreviewEnabled && ViewModel.IsSplitViewEnabled)
    {
        RightPreviewSplitterCol.Width = new GridLength(2, GridUnitType.Pixel);
        RightPreviewCol.Width = new GridLength(
            LoadPreviewWidth("RightPreviewWidth", 280), GridUnitType.Pixel);
    }
}
```

### 9.7 Cleanup 확장

```csharp
// OnClosed에 추가
LeftPreviewPanel?.Cleanup();
RightPreviewPanel?.Cleanup();
```

### 9.8 Visibility 헬퍼

```csharp
public Visibility PreviewVisible(bool isEnabled)
    => isEnabled ? Visibility.Visible : Visibility.Collapsed;
```

---

## 10. Split View 시나리오 상세

### 10.1 시나리오별 동작 정리

| 시나리오 | 좌 Explorer | 좌 Preview | Splitter | 우 Explorer | 우 Preview |
|----------|-------------|-----------|----------|-------------|-----------|
| 단일 모드, Preview OFF | ★ (full) | - | - | - | - |
| 단일 모드, Preview ON | ★ | ★ (280px) | - | - | - |
| 분할, 양쪽 Preview OFF | ★ | - | ★ | ★ | - |
| 분할, 좌만 Preview ON | ★ | ★ | ★ | ★ (full) | - |
| 분할, 양쪽 Preview ON | ★ | ★ | ★ | ★ | ★ |

### 10.2 미리보기 토글 동작 규칙

1. `Ctrl+Shift+P` → **Active Pane**의 미리보기 토글
2. 단일 모드 → 항상 Left Pane 미리보기 토글
3. 분할 모드 → Active Pane 감지 후 해당 패널 토글
4. 토글 시 즉시 현재 선택 항목의 미리보기 로딩

### 10.3 선택 변경 라우팅

```
Miller Column에서 선택 변경
  → OnMillerColumnSelectionChanged()
  → DetectActivePane() (visual tree 기반)
  → UpdatePreviewForSelection(selectedItem)
    → Left이면 LeftPreviewPanel.UpdatePreview()
    → Right이면 RightPreviewPanel.UpdatePreview()

Details/Icon에서 선택 변경
  → FolderViewModel.SelectedChild PropertyChanged
  → SubscribeToSelectionForPreview() 콜백
  → 해당 패널의 PreviewPanel.UpdatePreview()
```

---

## 11. 성능 설계

### 11.1 디바운싱
- 선택 변경 후 **200ms** 대기 → 미리보기 로딩 시작
- 200ms 이내 재선택 시 이전 타이머 리셋

### 11.2 취소 패턴
```
CancellationTokenSource 관리:
  - 새 로딩 시작 → 이전 CTS Cancel()
  - 패널 닫힐 때 → CTS Cancel()
  - Cleanup 시 → CTS Cancel() + Dispose()
```

### 11.3 메모리 관리
- 이미지: `BitmapImage` 교체 시 이전 참조 해제 (GC 수거)
- 미디어: `MediaSource.Dispose()` 명시적 호출
- 텍스트: 50KB 제한으로 메모리 사용 제한
- PDF: `InMemoryRandomAccessStream` using 패턴

### 11.4 파일 크기 제한
| 타입 | 제한 | 초과 시 |
|------|------|---------|
| 이미지 | 100MB | Generic 타입으로 폴백 |
| 텍스트 | 50KB | 잘림 표시 |
| PDF | 100MB | Generic 타입으로 폴백 |
| 미디어 | 제한 없음 | MediaPlayerElement가 스트리밍 |

---

## 12. 구현 순서 (Step-by-Step)

### Step 1: 모델 + 서비스
1. `Models/PreviewType.cs` 생성
2. `Services/PreviewService.cs` 생성
3. `App.xaml.cs`에 DI 등록
4. 빌드 확인

### Step 2: ViewModel
1. `ViewModels/PreviewPanelViewModel.cs` 생성
2. 빌드 확인

### Step 3: View (UserControl)
1. `Views/PreviewPanelView.xaml` 생성
2. `Views/PreviewPanelView.xaml.cs` 생성
3. 빌드 확인

### Step 4: MainViewModel 확장
1. `IsLeftPreviewEnabled`, `IsRightPreviewEnabled` 추가
2. `TogglePreview()`, `SavePreviewState()` 추가
3. `LoadViewModePreference()`에 미리보기 상태 로드 추가
4. `Cleanup()`에 `SavePreviewState()` 추가
5. 빌드 확인

### Step 5: MainWindow.xaml 레이아웃
1. Left Pane Content Grid에 Preview 3열 추가
2. Right Pane Content Grid에 Preview 3열 추가
3. 토글 버튼 추가
4. 빌드 확인

### Step 6: MainWindow.xaml.cs 통합
1. `InitializePreviewPanels()` 구현
2. `OnMillerColumnSelectionChanged()`에 미리보기 연결
3. `SubscribeToSelectionForPreview()` 구현
4. 단축키 `Ctrl+Shift+P` 추가
5. `TogglePreviewPanel()` 구현
6. Loaded에서 `RestorePreviewState()` 호출
7. OnClosed에서 `Cleanup()` 추가
8. 빌드 확인

### Step 7: 테스트 & 수정
1. 이미지 미리보기 동작 확인
2. 텍스트 미리보기 동작 확인
3. PDF 미리보기 동작 확인
4. 미디어 미리보기 동작 확인
5. Split View에서 양쪽 독립 동작 확인
6. 빠른 선택 변경 시 flickering 없는지 확인
7. 앱 종료 시 크래시 없는지 확인

---

## 13. 검증 체크리스트

- [ ] `Ctrl+Shift+P` → 미리보기 패널 토글
- [ ] 이미지 파일 선택 → 이미지 미리보기 + 해상도 표시
- [ ] 텍스트 파일 선택 → 텍스트 내용 미리보기
- [ ] PDF 파일 선택 → 첫 페이지 미리보기
- [ ] MP4/MP3 선택 → MediaPlayer 표시 + 재생 가능
- [ ] 폴더 선택 → 항목 수 표시
- [ ] 미지원 파일 → 아이콘 + 메타데이터만 표시
- [ ] 기본 메타데이터 표시 (이름, 크기, 종류, 날짜)
- [ ] GridSplitter로 미리보기 너비 조절
- [ ] Split View에서 양쪽 독립 미리보기 토글
- [ ] Split View에서 좌 선택 → 좌 미리보기만 업데이트
- [ ] Split View에서 우 선택 → 우 미리보기만 업데이트
- [ ] 빠른 선택 변경 시 flickering/크래시 없음
- [ ] 미선택 상태 → "파일을 선택하면 미리보기가 표시됩니다"
- [ ] 파일 선택 변경 시 미디어 자동 정지
- [ ] 앱 종료 시 크래시 없음
- [ ] 앱 재시작 시 미리보기 상태 복원
