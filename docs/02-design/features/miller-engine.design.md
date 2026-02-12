# Design: Miller Column Engine (miller-engine)

## 1. Data Model Design (Models)
파일 시스템의 엔티티를 추상화합니다.

```csharp
// Models/IFileSystemItem.cs
public interface IFileSystemItem
{
    string Name { get; }
    string Path { get; }
    string IconGlyph { get; } // 임시 아이콘 (나중에 이미지로 교체)
}

// Models/DriveItem.cs
public class DriveItem : IFileSystemItem
{
    // ... 드라이브 정보
}

// Models/FolderItem.cs
public class FolderItem : IFileSystemItem
{
    // ... 폴더 정보
}

// Models/FileItem.cs
public class FileItem : IFileSystemItem
{
    public long Size { get; set; }
    public DateTime Modified { get; set; }
}
```

## 2. Service Architecture (Services)
`System.IO`를 래핑하여 비동기로 데이터를 제공합니다.

- **FileService**:
    - `Task<IEnumerable<IFileSystemItem>> GetDrivesAsync()`
    - `Task<IEnumerable<IFileSystemItem>> GetItemsAsync(string path)`
    - *Zero Lag* 원칙에 따라 `Task.Run`으로 I/O 작업을 백그라운드 스레드에서 처리합니다.

## 3. ViewModel Logic (ViewModels)
밀러 컬럼의 계층 구조를 관리합니다.

- **ColumnViewModel**:
    - `ObservableCollection<IFileSystemItem> Items`: 해당 컬럼의 파일 목록
    - `IFileSystemItem SelectedItem`: 선택된 항목
    - `bool IsLoading`: 로딩 인디케이터 제어

- **MillerColumnViewModel**:
    - `ObservableCollection<ColumnViewModel> Columns`: 전체 컬럼 리스트
    - `NavigateTo(path)`: 경로 이동 시 컬럼 체인 재구성 로직

## 4. UI Implementation (Views)
- **MillerColumnView.xaml**:
    - `ScrollViewer` (Horizontal)
    - `ItemsControl` (HorizontalStackPanel) -> `DataTemplate` (ColumnView)
    - 각 컬럼은 `ListView`를 사용하여 가상화 처리 (`ItemsStackPanel` 기본 적용)

## 5. Implementation Steps
1.  `IFileSystemItem` 및 파생 클래스 구현
2.  `FileService` 구현 (실제 C: 드라이브 접근)
3.  `ViewModels` 구현 (계층 네비게이션 로직)
4.  `MillerColumnView` XAML 구현 및 `MainPage`에 배치
