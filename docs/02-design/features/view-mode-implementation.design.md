# View Mode Implementation Design Document

> **Summary**: Span 파일 탐색기에 Miller Columns, Details, Icon 3가지 뷰 모드 구현
>
> **Project**: Span (WinUI 3 File Explorer)
> **Version**: 1.0.0
> **Author**: view-mode-planning team
> **Date**: 2026-02-13
> **Status**: Draft
> **Planning Doc**: [view-mode-implementation.plan.md](../../01-plan/features/view-mode-implementation.plan.md)

### Pipeline References

| Phase | Document | Status |
|-------|----------|--------|
| Phase 1 | Schema Definition | ✅ (ViewMode enum) |
| Phase 2 | Coding Conventions | ✅ (CLAUDE.md) |
| Phase 3 | Mockup | ✅ (1.png 참고) |
| Phase 4 | API Spec | N/A (UI 전환 로직) |

---

## 1. Overview

### 1.1 Design Goals

1. **유연성**: 사용자가 상황에 맞는 뷰 모드 선택 가능
2. **일관성**: 모든 뷰 모드에서 동일한 데이터 소스 및 키보드 네비게이션
3. **성능**: 뷰 전환 300ms 이내, 메모리 증가 20% 이하
4. **호환성**: Miller Columns 기존 기능 100% 유지

### 1.2 Design Principles

- **Single Source of Truth**: 모든 뷰가 `FolderViewModel.Children` 공유
- **MVVM Pattern**: ViewMode 상태는 ViewModel이 관리, View는 Visibility 전환만
- **Lazy Loading**: 각 뷰는 표시될 때만 렌더링 (Visibility.Collapsed 활용)
- **Keyboard First**: 모든 뷰 모드에서 동일한 단축키 동작

---

## 2. Architecture

### 2.1 Component Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         MainWindow (View)                        │
│  - ViewModeSelector: SplitButton (CommandBar)                   │
│  - MillerColumnsView: ScrollViewer (기존)                       │
│  - DetailsView: DataGrid (신규)                                 │
│  - IconGridView: GridView (신규)                                │
│  - OnViewModeChanged() → UI Visibility 전환                     │
└───────────────┬─────────────────────────────────────────────────┘
                │ DataContext
                v
┌─────────────────────────────────────────────────────────────────┐
│                  MainViewModel (Root ViewModel)                  │
│  - CurrentViewMode: ViewMode (Observable)                       │
│  - SwitchViewMode(ViewMode mode)                                │
│  - SaveViewModePreference() → LocalSettings                     │
│  - LoadViewModePreference() ← LocalSettings                     │
└───────────────┬─────────────────────────────────────────────────┘
                │ Explorer Property
                v
┌─────────────────────────────────────────────────────────────────┐
│                  ExplorerViewModel (Miller Engine)               │
│  - ViewMode: ViewMode (현재 활성 모드)                          │
│  - CurrentFolder: FolderViewModel? (마지막 컬럼)                │
│  - CurrentItems: ObservableCollection (CurrentFolder.Children)  │
│  - OnViewModeChanged() → 뷰별 추가 로직                         │
└───────────────┬─────────────────────────────────────────────────┘
                │ Columns: ObservableCollection<FolderViewModel>
                v
┌─────────────────────────────────────────────────────────────────┐
│                     FolderViewModel (Column)                     │
│  - Children: ObservableCollection<FileSystemViewModel>          │
│  - SelectedChild: FileSystemViewModel? (선택 항목)              │
│  - IsActive: bool (포커스 표시)                                 │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Data Flow

#### ViewMode 전환 흐름

```
[사용자: View 버튼 클릭 또는 Ctrl+1/2/3]
    ↓
[MainViewModel.SwitchViewMode(ViewMode mode)]
    ├─ CurrentViewMode = mode
    ├─ SaveViewModePreference()
    └─ OnPropertyChanged(nameof(CurrentViewMode))
           ↓
    [MainWindow: x:Bind IsMillerColumnsMode()]
           ↓
    [MillerColumnsView.Visibility = Visible/Collapsed]
    [DetailsView.Visibility = Visible/Collapsed]
    [IconGridView.Visibility = Visible/Collapsed]
           ↓
    [활성 뷰에 포커스 설정]
```

#### Details 모드 데이터 바인딩

```
[ExplorerViewModel.CurrentItems]
    ↓ (OneWay Binding)
[DataGrid.ItemsSource]
    ↓
[DataGridRow 생성 (가상화)]
    ↓
[사용자: 컬럼 헤더 클릭]
    ↓
[OnDetailsColumnHeaderClick(string columnName)]
    ├─ SortCurrentColumn(columnName)
    │      ├─ CurrentFolder.Children.OrderBy(...)
    │      └─ Children.Clear() + AddRange()
    └─ 정렬 인디케이터 업데이트 (▲▼)
```

#### Icon 모드 데이터 바인딩

```
[ExplorerViewModel.CurrentItems]
    ↓ (OneWay Binding)
[GridView.ItemsSource]
    ↓
[ItemTemplate 적용]
    ├─ IconSize에 따라 16/48/96/256 크기
    └─ WrapGrid 자동 줄바꿈
           ↓
[사용자: Icon 크기 변경]
    ↓
[MainViewModel.CurrentIconSize = IconSize.Large]
    ↓
[ItemTemplate Selector 재평가]
    ↓
[UI 업데이트]
```

### 2.3 Dependencies

| Component | Depends On | Purpose |
|-----------|------------|---------|
| MainWindow | MainViewModel.CurrentViewMode | UI Visibility 전환 |
| MainViewModel | Windows.Storage.ApplicationData | ViewMode 영속화 |
| ExplorerViewModel | FolderViewModel.Children | 공통 데이터 소스 |
| DataGrid | NaturalStringComparer | Details 모드 정렬 |
| GridView | ItemTemplateSelector | Icon 크기별 템플릿 |

---

## 3. Data Model

### 3.1 ViewMode Enum

**파일**: `src/Span/Span/Models/ViewMode.cs` (신규)

```csharp
namespace Span.Models
{
    /// <summary>
    /// 파일 탐색기 뷰 모드 정의
    /// </summary>
    public enum ViewMode
    {
        /// <summary>
        /// Miller Columns: macOS Finder 스타일 계층 탐색
        /// </summary>
        MillerColumns = 0,

        /// <summary>
        /// Details: 테이블 뷰 (Name, Date Modified, Type, Size)
        /// </summary>
        Details = 1,

        /// <summary>
        /// Icon Small: 16x16 그리드
        /// </summary>
        IconSmall = 2,

        /// <summary>
        /// Icon Medium: 48x48 그리드
        /// </summary>
        IconMedium = 3,

        /// <summary>
        /// Icon Large: 96x96 그리드
        /// </summary>
        IconLarge = 4,

        /// <summary>
        /// Icon Extra Large: 256x256 그리드 (썸네일 지원)
        /// </summary>
        IconExtraLarge = 5
    }
}
```

### 3.2 IconSize Helper (Extension)

**파일**: `src/Span/Span/Helpers/ViewModeExtensions.cs` (신규)

```csharp
namespace Span.Helpers
{
    public static class ViewModeExtensions
    {
        /// <summary>
        /// ViewMode가 Icon 계열인지 확인
        /// </summary>
        public static bool IsIconMode(this ViewMode mode)
        {
            return mode >= ViewMode.IconSmall && mode <= ViewMode.IconExtraLarge;
        }

        /// <summary>
        /// Icon 모드의 픽셀 크기 반환
        /// </summary>
        public static int GetIconPixelSize(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.IconSmall => 16,
                ViewMode.IconMedium => 48,
                ViewMode.IconLarge => 96,
                ViewMode.IconExtraLarge => 256,
                _ => 48 // Default
            };
        }

        /// <summary>
        /// ViewMode 표시 이름 (UI용)
        /// </summary>
        public static string GetDisplayName(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.MillerColumns => "Miller Columns",
                ViewMode.Details => "Details",
                ViewMode.IconSmall => "Small Icons",
                ViewMode.IconMedium => "Medium Icons",
                ViewMode.IconLarge => "Large Icons",
                ViewMode.IconExtraLarge => "Extra Large Icons",
                _ => mode.ToString()
            };
        }

        /// <summary>
        /// 키보드 단축키 텍스트
        /// </summary>
        public static string GetShortcutText(this ViewMode mode)
        {
            return mode switch
            {
                ViewMode.MillerColumns => "Ctrl+1",
                ViewMode.Details => "Ctrl+2",
                ViewMode.IconSmall or ViewMode.IconMedium or ViewMode.IconLarge or ViewMode.IconExtraLarge => "Ctrl+3",
                _ => ""
            };
        }
    }
}
```

### 3.3 MainViewModel Extensions

**파일**: `src/Span/Span/ViewModels/MainViewModel.cs` (수정)

```csharp
public partial class MainViewModel : ObservableObject
{
    // 기존 코드...

    [ObservableProperty]
    private ViewMode _currentViewMode = ViewMode.MillerColumns;

    [ObservableProperty]
    private ViewMode _currentIconSize = ViewMode.IconMedium; // Icon 모드 기본 크기

    /// <summary>
    /// 뷰 모드 전환
    /// </summary>
    public void SwitchViewMode(ViewMode mode)
    {
        if (CurrentViewMode == mode) return;

        // Icon 모드 전환 시 크기 업데이트
        if (mode.IsIconMode())
        {
            CurrentIconSize = mode;
            CurrentViewMode = ViewMode.IconMedium; // UI에서는 Icon 통합 표시
        }
        else
        {
            CurrentViewMode = mode;
        }

        SaveViewModePreference();
        Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: {mode.GetDisplayName()}");
    }

    /// <summary>
    /// ViewMode 설정 저장 (LocalSettings)
    /// </summary>
    private void SaveViewModePreference()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["ViewMode"] = (int)CurrentViewMode;
            settings.Values["IconSize"] = (int)CurrentIconSize;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"SaveViewModePreference error: {ex.Message}");
        }
    }

    /// <summary>
    /// ViewMode 설정 로드 (앱 시작 시)
    /// </summary>
    public void LoadViewModePreference()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

            if (settings.Values.TryGetValue("ViewMode", out var mode))
            {
                CurrentViewMode = (ViewMode)(int)mode;
            }

            if (settings.Values.TryGetValue("IconSize", out var size))
            {
                CurrentIconSize = (ViewMode)(int)size;
            }

            Helpers.DebugLogger.Log($"[MainViewModel] ViewMode loaded: {CurrentViewMode.GetDisplayName()}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LoadViewModePreference error: {ex.Message}");
            CurrentViewMode = ViewMode.MillerColumns; // Fallback
        }
    }
}
```

### 3.4 ExplorerViewModel Extensions

**파일**: `src/Span/Span/ViewModels/ExplorerViewModel.cs` (수정)

```csharp
public partial class ExplorerViewModel : ObservableObject
{
    // 기존 코드...

    /// <summary>
    /// 현재 활성 폴더 (Details/Icon 모드용)
    /// Miller Columns의 마지막 컬럼 반환
    /// </summary>
    public FolderViewModel? CurrentFolder => Columns.LastOrDefault();

    /// <summary>
    /// 현재 표시할 항목 리스트 (Details/Icon 모드용)
    /// </summary>
    public ObservableCollection<FileSystemViewModel> CurrentItems =>
        CurrentFolder?.Children ?? new ObservableCollection<FileSystemViewModel>();

    /// <summary>
    /// ViewMode 변경 시 추가 로직
    /// </summary>
    partial void OnViewModeChanged(ViewMode oldValue, ViewMode newValue)
    {
        Helpers.DebugLogger.Log($"[ExplorerViewModel] ViewMode changed: {oldValue} → {newValue}");

        // Details/Icon 모드로 전환 시 CurrentItems 갱신 알림
        if (newValue == ViewMode.Details || newValue.IsIconMode())
        {
            OnPropertyChanged(nameof(CurrentFolder));
            OnPropertyChanged(nameof(CurrentItems));
        }
    }
}
```

---

## 4. UI/UX Specifications

### 4.1 ViewMode Selector (CommandBar)

**파일**: `src/Span/Span/MainWindow.xaml` (수정)

**위치**: AddressBar 우측, SearchBox 좌측

```xml
<!-- ViewMode Selector -->
<AppBarButton x:Name="ViewModeButton"
              Label="View"
              ToolTipService.ToolTip="Change view mode">
    <AppBarButton.Icon>
        <FontIcon Glyph="&#xE8FD;" /> <!-- ViewAll icon -->
    </AppBarButton.Icon>
    <AppBarButton.Flyout>
        <MenuFlyout Placement="BottomEdgeAlignedRight">
            <!-- Miller Columns -->
            <MenuFlyoutItem Text="Miller Columns"
                            Click="OnViewModeMillerColumns">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE8FD;" />
                </MenuFlyoutItem.Icon>
                <MenuFlyoutItem.KeyboardAccelerators>
                    <KeyboardAccelerator Key="Number1" Modifiers="Control" />
                </MenuFlyoutItem.KeyboardAccelerators>
            </MenuFlyoutItem>

            <!-- Details -->
            <MenuFlyoutItem Text="Details"
                            Click="OnViewModeDetails">
                <MenuFlyoutItem.Icon>
                    <FontIcon Glyph="&#xE8EF;" /> <!-- List icon -->
                </MenuFlyoutItem.Icon>
                <MenuFlyoutItem.KeyboardAccelerators>
                    <KeyboardAccelerator Key="Number2" Modifiers="Control" />
                </MenuFlyoutItem.KeyboardAccelerators>
            </MenuFlyoutItem>

            <MenuFlyoutSeparator />

            <!-- Icon Submenu -->
            <MenuFlyoutSubItem Text="Icons">
                <MenuFlyoutSubItem.Icon>
                    <FontIcon Glyph="&#xE91B;" /> <!-- GridView icon -->
                </MenuFlyoutSubItem.Icon>

                <MenuFlyoutItem Text="Extra Large Icons"
                                Click="OnViewModeIconExtraLarge" />
                <MenuFlyoutItem Text="Large Icons"
                                Click="OnViewModeIconLarge" />
                <MenuFlyoutItem Text="Medium Icons"
                                Click="OnViewModeIconMedium" />
                <MenuFlyoutItem Text="Small Icons"
                                Click="OnViewModeIconSmall" />
            </MenuFlyoutSubItem>
        </MenuFlyout>
    </AppBarButton.Flyout>
</AppBarButton>
```

### 4.2 Miller Columns View (기존 유지)

```xml
<!-- Miller Columns View -->
<ScrollViewer x:Name="MillerColumnsView"
              Grid.Row="2"
              Visibility="{x:Bind IsMillerColumnsMode(ViewModel.CurrentViewMode), Mode=OneWay}"
              HorizontalScrollMode="Auto"
              HorizontalScrollBarVisibility="Auto"
              VerticalScrollMode="Disabled"
              VerticalScrollBarVisibility="Disabled">

    <ItemsControl ItemsSource="{x:Bind ViewModel.Explorer.Columns, Mode=OneWay}">
        <!-- 기존 Miller Columns ItemTemplate -->
    </ItemsControl>
</ScrollViewer>
```

### 4.3 Details View (DataGrid)

```xml
<!-- Details View -->
<Grid x:Name="DetailsView"
      Grid.Row="2"
      Visibility="{x:Bind IsDetailsMode(ViewModel.CurrentViewMode), Mode=OneWay}">

    <ListView x:Name="DetailsListView"
              ItemsSource="{x:Bind ViewModel.Explorer.CurrentItems, Mode=OneWay}"
              SelectedItem="{x:Bind ViewModel.Explorer.CurrentFolder.SelectedChild, Mode=TwoWay}"
              SelectionMode="Single"
              IsItemClickEnabled="True"
              ItemClick="OnDetailsItemClick"
              KeyDown="OnDetailsKeyDown">

        <ListView.ItemContainerStyle>
            <Style TargetType="ListViewItem">
                <Setter Property="HorizontalContentAlignment" Value="Stretch" />
                <Setter Property="Padding" Value="8,4" />
            </Style>
        </ListView.ItemContainerStyle>

        <ListView.HeaderTemplate>
            <DataTemplate>
                <Grid Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                      Padding="8,8">
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="3*" /> <!-- Name -->
                        <ColumnDefinition Width="2*" /> <!-- Date Modified -->
                        <ColumnDefinition Width="1.5*" /> <!-- Type -->
                        <ColumnDefinition Width="1*" /> <!-- Size -->
                    </Grid.ColumnDefinitions>

                    <!-- Name Header -->
                    <Button Grid.Column="0"
                            Content="Name"
                            Click="OnSortByName"
                            HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Left">
                        <Button.ContentTemplate>
                            <DataTemplate>
                                <StackPanel Orientation="Horizontal" Spacing="4">
                                    <TextBlock Text="Name" />
                                    <TextBlock x:Name="NameSortIndicator" Text="" />
                                </StackPanel>
                            </DataTemplate>
                        </Button.ContentTemplate>
                    </Button>

                    <!-- Date Modified Header -->
                    <Button Grid.Column="1"
                            Content="Date Modified"
                            Click="OnSortByDate"
                            HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Left" />

                    <!-- Type Header -->
                    <Button Grid.Column="2"
                            Content="Type"
                            Click="OnSortByType"
                            HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Left" />

                    <!-- Size Header -->
                    <Button Grid.Column="3"
                            Content="Size"
                            Click="OnSortBySize"
                            HorizontalAlignment="Stretch"
                            HorizontalContentAlignment="Left" />
                </Grid>
            </DataTemplate>
        </ListView.HeaderTemplate>

        <ListView.ItemTemplate>
            <DataTemplate x:DataType="vm:FileSystemViewModel">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="3*" />
                        <ColumnDefinition Width="2*" />
                        <ColumnDefinition Width="1.5*" />
                        <ColumnDefinition Width="1*" />
                    </Grid.ColumnDefinitions>

                    <!-- Name + Icon -->
                    <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8">
                        <TextBlock FontFamily="Segoe Fluent Icons"
                                   Text="{x:Bind IconGlyph}"
                                   Foreground="{x:Bind IconBrush}"
                                   VerticalAlignment="Center" />
                        <TextBlock Text="{x:Bind Name, Mode=OneWay}"
                                   VerticalAlignment="Center" />
                    </StackPanel>

                    <!-- Date Modified -->
                    <TextBlock Grid.Column="1"
                               Text="{x:Bind Model.DateModified, Mode=OneWay}"
                               VerticalAlignment="Center" />

                    <!-- Type -->
                    <TextBlock Grid.Column="2"
                               Text="{x:Bind Model.FileType, Mode=OneWay}"
                               VerticalAlignment="Center" />

                    <!-- Size (FileViewModel only) -->
                    <TextBlock Grid.Column="3"
                               Text="{x:Bind ((models:FileItem)Model).Size, Mode=OneWay, Converter={StaticResource FileSizeConverter}}"
                               VerticalAlignment="Center"
                               Visibility="{x:Bind ((models:FileItem)Model).Size, Converter={StaticResource NullToVisibilityConverter}}" />
                </Grid>
            </DataTemplate>
        </ListView.ItemTemplate>
    </ListView>
</Grid>
```

### 4.4 Icon View (GridView)

```xml
<!-- Icon View -->
<GridView x:Name="IconGridView"
          Grid.Row="2"
          Visibility="{x:Bind IsIconMode(ViewModel.CurrentViewMode), Mode=OneWay}"
          ItemsSource="{x:Bind ViewModel.Explorer.CurrentItems, Mode=OneWay}"
          SelectedItem="{x:Bind ViewModel.Explorer.CurrentFolder.SelectedChild, Mode=TwoWay}"
          SelectionMode="Single"
          IsItemClickEnabled="True"
          ItemClick="OnIconItemClick"
          KeyDown="OnIconKeyDown">

    <GridView.ItemsPanel>
        <ItemsPanelTemplate>
            <ItemsWrapGrid Orientation="Horizontal"
                           MaximumRowsOrColumns="-1" />
        </ItemsPanelTemplate>
    </GridView.ItemsPanel>

    <GridView.ItemContainerStyle>
        <Style TargetType="GridViewItem">
            <Setter Property="Margin" Value="8" />
        </Style>
    </GridView.ItemContainerStyle>

    <GridView.ItemTemplateSelector>
        <local:IconSizeTemplateSelector>
            <!-- Small Icon Template (16x16) -->
            <local:IconSizeTemplateSelector.SmallTemplate>
                <DataTemplate x:DataType="vm:FileSystemViewModel">
                    <StackPanel Width="80" Spacing="4">
                        <TextBlock FontFamily="Segoe Fluent Icons"
                                   Text="{x:Bind IconGlyph}"
                                   FontSize="16"
                                   Foreground="{x:Bind IconBrush}"
                                   HorizontalAlignment="Center" />
                        <TextBlock Text="{x:Bind Name, Mode=OneWay}"
                                   TextWrapping="Wrap"
                                   TextAlignment="Center"
                                   MaxLines="2" />
                    </StackPanel>
                </DataTemplate>
            </local:IconSizeTemplateSelector.SmallTemplate>

            <!-- Medium Icon Template (48x48) -->
            <local:IconSizeTemplateSelector.MediumTemplate>
                <DataTemplate x:DataType="vm:FileSystemViewModel">
                    <StackPanel Width="100" Spacing="8">
                        <Border Width="48" Height="48"
                                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="4">
                            <TextBlock FontFamily="Segoe Fluent Icons"
                                       Text="{x:Bind IconGlyph}"
                                       FontSize="32"
                                       Foreground="{x:Bind IconBrush}"
                                       HorizontalAlignment="Center"
                                       VerticalAlignment="Center" />
                        </Border>
                        <TextBlock Text="{x:Bind Name, Mode=OneWay}"
                                   TextWrapping="Wrap"
                                   TextAlignment="Center"
                                   MaxLines="2" />
                    </StackPanel>
                </DataTemplate>
            </local:IconSizeTemplateSelector.MediumTemplate>

            <!-- Large Icon Template (96x96) -->
            <local:IconSizeTemplateSelector.LargeTemplate>
                <DataTemplate x:DataType="vm:FileSystemViewModel">
                    <StackPanel Width="120" Spacing="8">
                        <Border Width="96" Height="96"
                                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="8">
                            <TextBlock FontFamily="Segoe Fluent Icons"
                                       Text="{x:Bind IconGlyph}"
                                       FontSize="64"
                                       Foreground="{x:Bind IconBrush}"
                                       HorizontalAlignment="Center"
                                       VerticalAlignment="Center" />
                        </Border>
                        <TextBlock Text="{x:Bind Name, Mode=OneWay}"
                                   TextWrapping="Wrap"
                                   TextAlignment="Center"
                                   MaxLines="3" />
                    </StackPanel>
                </DataTemplate>
            </local:IconSizeTemplateSelector.LargeTemplate>

            <!-- Extra Large Icon Template (256x256) -->
            <local:IconSizeTemplateSelector.ExtraLargeTemplate>
                <DataTemplate x:DataType="vm:FileSystemViewModel">
                    <StackPanel Width="280" Spacing="12">
                        <Border Width="256" Height="256"
                                Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                                CornerRadius="12">
                            <!-- TODO Phase 3.2: Image thumbnail for image files -->
                            <TextBlock FontFamily="Segoe Fluent Icons"
                                       Text="{x:Bind IconGlyph}"
                                       FontSize="128"
                                       Foreground="{x:Bind IconBrush}"
                                       HorizontalAlignment="Center"
                                       VerticalAlignment="Center" />
                        </Border>
                        <TextBlock Text="{x:Bind Name, Mode=OneWay}"
                                   TextWrapping="Wrap"
                                   TextAlignment="Center"
                                   FontSize="14"
                                   MaxLines="3" />
                    </StackPanel>
                </DataTemplate>
            </local:IconSizeTemplateSelector.ExtraLargeTemplate>
        </local:IconSizeTemplateSelector>
    </GridView.ItemTemplateSelector>
</GridView>
```

### 4.5 IconSizeTemplateSelector

**파일**: `src/Span/Span/Helpers/IconSizeTemplateSelector.cs` (신규)

```csharp
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Span.Helpers
{
    public class IconSizeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? SmallTemplate { get; set; }
        public DataTemplate? MediumTemplate { get; set; }
        public DataTemplate? LargeTemplate { get; set; }
        public DataTemplate? ExtraLargeTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (container is FrameworkElement element)
            {
                // MainWindow에서 CurrentIconSize 가져오기
                var mainWindow = App.Current.MainWindow;
                if (mainWindow?.DataContext is ViewModels.MainViewModel vm)
                {
                    return vm.CurrentIconSize switch
                    {
                        Models.ViewMode.IconSmall => SmallTemplate ?? MediumTemplate,
                        Models.ViewMode.IconMedium => MediumTemplate ?? SmallTemplate,
                        Models.ViewMode.IconLarge => LargeTemplate ?? MediumTemplate,
                        Models.ViewMode.IconExtraLarge => ExtraLargeTemplate ?? LargeTemplate,
                        _ => MediumTemplate
                    };
                }
            }

            return MediumTemplate ?? base.SelectTemplateCore(item, container);
        }
    }
}
```

---

## 5. State Management

### 5.1 ViewMode State Transitions

```
     [App Start]
         ↓
   LoadViewModePreference()
         ↓
   CurrentViewMode = saved or MillerColumns
         ↓
┌────────────────────────────────┐
│   MillerColumns (Default)      │ ←─────┐
└────────────────────────────────┘       │
    │                   ↑                 │
    │ Ctrl+2 or Menu    │ Ctrl+1         │
    ↓                   │                 │
┌────────────────────────────────┐       │
│         Details                │       │
└────────────────────────────────┘       │
    │                   ↑                 │
    │ Ctrl+3 or Menu    │ Ctrl+1/2       │
    ↓                   │                 │
┌────────────────────────────────┐       │
│      Icon (Small/M/L/XL)       │───────┘
└────────────────────────────────┘
         │
         │ [App Close]
         ↓
   SaveViewModePreference()
```

### 5.2 Selection State Synchronization

**문제**: 뷰 모드 전환 시 선택 항목 유지

**해결**:
1. 모든 뷰가 동일한 `ViewModel.Explorer.CurrentFolder.SelectedChild` 바인딩
2. 뷰 전환 시 선택 항목은 ViewModel에서 유지
3. 새 뷰 활성화 시 자동으로 선택 복원

```csharp
// MainWindow.xaml.cs
private void OnViewModeChanged(ViewMode newMode)
{
    // Visibility 전환
    MillerColumnsView.Visibility = IsMillerColumnsMode(newMode) ? Visibility.Visible : Visibility.Collapsed;
    DetailsView.Visibility = IsDetailsMode(newMode) ? Visibility.Visible : Visibility.Collapsed;
    IconGridView.Visibility = IsIconMode(newMode) ? Visibility.Visible : Visibility.Collapsed;

    // 활성 뷰에 포커스
    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
    {
        if (IsDetailsMode(newMode))
            DetailsListView.Focus(FocusState.Programmatic);
        else if (IsIconMode(newMode))
            IconGridView.Focus(FocusState.Programmatic);
        // Miller Columns는 기존 포커스 유지
    });
}
```

### 5.3 Sort State Management (Details Mode)

**상태**: `_currentSortColumn`, `_currentSortAscending`

```csharp
// MainWindow.xaml.cs
private string _currentSortColumn = "Name";
private bool _currentSortAscending = true;

private void OnSortByName(object sender, RoutedEventArgs e)
{
    if (_currentSortColumn == "Name")
        _currentSortAscending = !_currentSortAscending;
    else
    {
        _currentSortColumn = "Name";
        _currentSortAscending = true;
    }

    SortDetailsView();
    UpdateSortIndicators();
}

private void SortDetailsView()
{
    var folder = ViewModel.Explorer.CurrentFolder;
    if (folder == null || folder.Children.Count == 0) return;

    IEnumerable<FileSystemViewModel> sorted = _currentSortColumn switch
    {
        "Name" => _currentSortAscending
            ? folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Name, Helpers.NaturalStringComparer.Instance)
            : folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Name, Helpers.NaturalStringComparer.Instance),

        "Date" => _currentSortAscending
            ? folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Model.DateModified)
            : folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Model.DateModified),

        "Size" => _currentSortAscending
            ? folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => (x as FileViewModel)?.Model.Size ?? 0)
            : folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => (x as FileViewModel)?.Model.Size ?? 0),

        "Type" => _currentSortAscending
            ? folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenBy(x => x.Model.FileType)
            : folder.Children.OrderBy(x => x is FileViewModel ? 1 : 0).ThenByDescending(x => x.Model.FileType),

        _ => folder.Children
    };

    var sortedList = sorted.ToList();
    folder.Children.Clear();
    foreach (var item in sortedList)
    {
        folder.Children.Add(item);
    }
}

private void UpdateSortIndicators()
{
    // TODO: XAML에서 ▲▼ 인디케이터 업데이트
}
```

---

## 6. Implementation Details

### 6.1 Phase 1: ViewMode Infrastructure

**목표**: 뷰 모드 전환 메커니즘 구축

#### 작업 1: ViewMode enum 및 Extensions 생성

- `Models/ViewMode.cs`
- `Helpers/ViewModeExtensions.cs`

#### 작업 2: MainViewModel 확장

- `CurrentViewMode` 프로퍼티
- `SwitchViewMode()` 메서드
- `LoadViewModePreference()`, `SaveViewModePreference()`

#### 작업 3: ExplorerViewModel 확장

- `CurrentFolder` 프로퍼티
- `CurrentItems` 프로퍼티

#### 작업 4: MainWindow XAML 수정

- ViewMode Selector CommandBar 추가
- 3개 View 컨테이너 추가 (Visibility.Collapsed)
- Visibility 바인딩 함수 (`IsMillerColumnsMode`, `IsDetailsMode`, `IsIconMode`)

#### 작업 5: 키보드 단축키

```csharp
// MainWindow.xaml.cs OnGlobalKeyDown
if (e.Key == VirtualKey.Number1 && ctrl)
{
    ViewModel.SwitchViewMode(ViewMode.MillerColumns);
    e.Handled = true;
}
else if (e.Key == VirtualKey.Number2 && ctrl)
{
    ViewModel.SwitchViewMode(ViewMode.Details);
    e.Handled = true;
}
else if (e.Key == VirtualKey.Number3 && ctrl)
{
    ViewModel.SwitchViewMode(ViewModel.CurrentIconSize); // 마지막 Icon 크기
    e.Handled = true;
}
```

### 6.2 Phase 2: Details Mode Implementation

**목표**: 테이블 뷰 완성

#### 작업 1: ListView with Header

- `DetailsListView` XAML 작성
- 컬럼 헤더 버튼 (Name, Date, Type, Size)
- ItemTemplate (Grid with 4 columns)

#### 작업 2: 정렬 로직

- `OnSortByName`, `OnSortByDate`, `OnSortBySize`, `OnSortByType`
- `SortDetailsView()` 구현
- NaturalStringComparer 통합

#### 작업 3: 이벤트 핸들러

- `OnDetailsItemClick` → 폴더 열기
- `OnDetailsKeyDown` → Enter/Delete/F2 처리

#### 작업 4: 선택 동기화

- `SelectedItem` TwoWay 바인딩
- Miller Columns ↔ Details 전환 시 선택 유지

### 6.3 Phase 3: Icon Mode Implementation

**목표**: 그리드 뷰 완성

#### 작업 1: GridView with ItemsWrapGrid

- `IconGridView` XAML 작성
- ItemsWrapGrid 레이아웃

#### 작업 2: IconSizeTemplateSelector

- `Helpers/IconSizeTemplateSelector.cs`
- 4가지 템플릿 (Small/Medium/Large/ExtraLarge)

#### 작업 3: 크기 전환

- ViewMode Selector 메뉴에서 Icon 크기 선택
- `CurrentIconSize` 변경 → Template 재평가

#### 작업 4: 이벤트 핸들러

- `OnIconItemClick` → 폴더 열기
- `OnIconKeyDown` → 방향키/Enter/Delete/F2 처리

---

## 7. Testing Strategy

### 7.1 Unit Test (수동)

| 테스트 케이스 | 예상 결과 |
|--------------|----------|
| Ctrl+1/2/3 단축키 | 해당 ViewMode로 전환 |
| View 메뉴 클릭 | 메뉴 표시, 항목 선택 시 전환 |
| ViewMode 전환 후 앱 재시작 | 마지막 ViewMode 복원 |
| Miller → Details 전환 | 마지막 컬럼의 Children 표시 |
| Details에서 Name 헤더 클릭 | 이름순 정렬 (Natural) |
| Details에서 Enter 키 | 폴더 열기, Details 유지 |
| Icon Small → Large 전환 | 아이콘 크기 변경, 레이아웃 재배치 |
| Icon에서 방향키 | 그리드 네비게이션 |

### 7.2 Integration Test

| 시나리오 | 검증 항목 |
|---------|----------|
| Miller → Details → Icon 순환 | 모든 전환 정상, 선택 유지 |
| Details에서 파일 삭제 | UI 갱신, Details 유지 |
| Icon에서 폴더 열기 | 하위 폴더 표시, Icon 유지 |
| 여러 ViewMode에서 F2 rename | 모든 모드에서 정상 동작 |
| 30분 탐색 후 ViewMode 전환 | 메모리 누수 없음 |

### 7.3 Performance Test

| 메트릭 | 목표 | 측정 방법 |
|--------|------|-----------|
| ViewMode 전환 시간 | 300ms 이하 | Stopwatch |
| Details 정렬 (1000항목) | 100ms 이하 | Performance Profiler |
| Icon 렌더링 (100항목) | 500ms 이하 | Performance Profiler |
| 메모리 증가 (3 뷰 유지) | 20% 이하 | Task Manager |

---

## 8. Risks and Mitigations

### 8.1 Technical Risks

**Risk**: 3개 View를 동시에 XAML에 유지하면 메모리 사용량 증가

- **Mitigation**: Visibility.Collapsed는 렌더링 스킵하지만 트리는 유지
- **Measurement**: Task Manager로 메모리 측정
- **Fallback**: Unloaded 이벤트에서 ItemsSource 해제

**Risk**: Details DataGrid 정렬이 ViewModel.Children과 충돌

- **Mitigation**: OneWay 바인딩, 정렬은 Children 직접 수정
- **Fallback**: CollectionViewSource 사용

**Risk**: Icon ExtraLarge 모드에서 대량 아이템 렌더링 시 성능 저하

- **Mitigation**: VirtualizingStackPanel 자동 가상화
- **Fallback**: 페이징 또는 스크롤 감지 로딩

### 8.2 UX Risks

**Risk**: 사용자가 ViewMode 전환 UI를 못 찾을 수 있음

- **Mitigation**: CommandBar에 눈에 띄는 "View" 버튼
- **Fallback**: 우클릭 메뉴에도 View 옵션 추가

**Risk**: Details 모드에서 컬럼 너비 조정 불가

- **Mitigation**: Phase 4에서 컬럼 Resize 기능 추가
- **Fallback**: 고정 너비 비율 (3:2:1.5:1)

---

## 9. Next Steps

1. ✅ **Design 문서 작성 완료** (현재 단계)
2. **Phase 1 구현**: ViewMode 인프라 구축
   - Models/ViewMode.cs
   - Helpers/ViewModeExtensions.cs
   - MainViewModel 확장
   - MainWindow XAML 수정
3. **Phase 2 구현**: Details 모드
4. **Phase 3 구현**: Icon 모드
5. **Gap Analysis**: 설계-구현 일치 검증
6. **최종 Report**: 완료 보고서

---

**설계 승인 후 즉시 Phase 1 구현을 시작할 수 있습니다.**
