# Design: Main Shell Layout (main-shell)

## 1. Visual Design (Mica & Theme)
- **Background**: `MicaBackdrop`을 사용하여 Windows 11 네이티브 느낌 구현.
- **Theme**: 기본 Dark 테마 적용.
- **Colors**: 프로토타입의 Accent Color (`#60cdff`)를 WinUI 3 `SystemAccentColor` 리소스와 매핑.

## 2. Layout Structure (XAML)
`MainWindow.xaml`의 계층 구조 설계:

```xml
<Window>
    <Grid>
        <!-- Custom Title Bar Area -->
        <Grid x:Name="AppTitleBar">
            <TabView x:Name="MainTabView" VerticalAlignment="Bottom" />
        </Grid>

        <!-- Main Content with Sidebar -->
        <NavigationView x:Name="RootNavView" 
                        PaneDisplayMode="Left"
                        IsBackButtonVisible="Collapsed">
            <NavigationView.MenuItems>
                <!-- Favorites, Drives, etc. -->
            </NavigationView.MenuItems>
            
            <Frame x:Name="ContentFrame" />
        </NavigationView>
    </Grid>
</Window>
```

## 3. ViewModel Design
- **MainViewModel**:
    - `ObservableCollection<TabItem>`: 열려있는 탭 관리
    - `SelectedTab`: 현재 활성화된 탭
    - `AddTabCommand`, `CloseTabCommand` 구현

## 4. Technical Details
- **Title Bar**: `ExtendsContentIntoTitleBar = true` 및 `SetTitleBar(AppTitleBar)`를 사용하여 탭을 타이틀 바 영역까지 확장.
- **Navigation**: `NavigationView`의 `SelectionChanged` 이벤트를 ViewModel의 명령과 바인딩.

## 5. Components to Create
1. `src/Span/ViewModels/MainViewModel.cs`
2. `src/Span/Views/MainPage.xaml` (실제 Miller Columns가 들어갈 페이지)
3. `src/Span/Helpers/TitleBarHelper.cs` (타이틀 바 높이 및 드래그 영역 관리 유틸리티)
