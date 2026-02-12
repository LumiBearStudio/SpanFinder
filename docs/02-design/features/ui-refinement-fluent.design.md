# Design: UI Refinement with Fluent Design & Icons (ui-refinement-fluent)

## 1. Visual Style & Resources
`design/style.css`의 디자인 토큰을 WinUI 3 `ResourceDictionary`로 변환합니다.

### 1.1 Colors & Brushes (App.xaml)
- **SpanAccentColor**: `#60cdff` (SolidColorBrush)
- **SpanMicaBase**: `#202020` (Background)
- **SpanLayer1**: `#2d2d2d` (Sidebar, Unified Bar)
- **SpanLayer2**: `#383838` (Cards, Flyouts)
- **SpanControlBorder**: `rgba(255, 255, 255, 0.10)`

### 1.2 Iconography (Segoe Fluent Icons)
모든 아이콘을 `FontFamily="Segoe Fluent Icons"` 기반 Glyph로 매핑합니다.
- Back: `\uE72B`
- Forward: `\uE761`
- Up: `\uE197`
- New: `\uE710`
- Cut: `\uE8C6`
- Copy: `\uE8C8`
- Paste: `\uE77F`
- Rename: `\uE8AC`
- Delete: `\uE74D`
- Folder: `\uE8B7`
- File: `\uE8A5`
- ArrowRight: `\uE974` (Miller Item Arrow)

## 2. Layout Structure (MainWindow.xaml)
`index.html`과 100% 일치하도록 Grid 레이아웃을 재구성합니다.

```xml
<Grid x:Name="RootGrid">
    <Grid.RowDefinitions>
        <RowDefinition Height="Auto" /> <!-- TitleBar + Tabs -->
        <RowDefinition Height="Auto" /> <!-- Unified Bar (Compact) -->
        <RowDefinition Height="*" />    <!-- Main Area (Sidebar + Content) -->
        <RowDefinition Height="Auto" /> <!-- Status Bar -->
    </Grid.RowDefinitions>

    <!-- Row 0: TitleBar & TabView -->
    <!-- Row 1: Unified Bar (Merged Nav + Address + Actions) -->
    <Grid x:Name="UnifiedBar" Grid.Row="1" Background="{StaticResource SpanLayer1Brush}">
        <!-- Nav Buttons | AddressBar (Breadcrumb) | Action Buttons | Search -->
    </Grid>

    <!-- Row 2: NavigationView (Sidebar) + Miller Columns -->
    <NavigationView Grid.Row="2">
        <!-- Sidebar items mapped from index.html -->
        <NavigationView.Content>
            <Frame x:Name="ContentFrame" />
        </NavigationView.Content>
    </NavigationView>

    <!-- Row 3: Status Bar -->
</Grid>
```

## 3. Miller Column Item Implementation
`MillerColumnView.xaml`의 리스트 항목 디자인을 고도화합니다.

- **Layout**: `Grid` (Icon | Name | Arrow)
- **Hover/Selection**: `design/style.css`의 `.miller-item:hover` 및 `.selected` 스타일 적용
- **Virtualization**: `ListView`의 내장 가상화 유지

## 4. Breadcrumb Implementation
`AddressBar` 영역에 현재 경로를 조각(Crumb) 단위로 표시하는 커스트 컨트롤 또는 `ItemsControl`을 배치합니다.

## 5. Technical Requirements
- 모든 버튼은 `Style="{StaticResource SubtleButtonStyle}"`과 유사한 커스텀 스타일을 적용하여 프로토타입의 깔끔한 느낌 유지
- `AppTitleBar` 영역의 `TabView`는 `ExtendsContentIntoTitleBar` 환경에서 드래그가 원활하도록 `SetTitleBar` 영역 조정
