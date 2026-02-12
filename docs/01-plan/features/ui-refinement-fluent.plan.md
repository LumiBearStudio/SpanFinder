# Plan: UI Refinement with Fluent Design & Icons (ui-refinement-fluent)

## 1. Overview
`design/` 폴더의 HTML 프로토타입 디자인을 WinUI 3에 완벽하게 이식합니다. 기존의 부족한 셸 구조를 버리고, 프로토타입에서 정의한 **Unified Bar(Compact 레이아웃)**와 **Fluent System Icons(Segoe Fluent Icons)**를 전면 적용하여 기획 의도에 맞는 고품질 UI를 구축합니다.

## 2. Goals
- [ ] **Exact Layout**: `index.html`의 구조(TitleBar-Tabs / Unified Bar / Sidebar-Content / StatusBar)를 1:1 매핑
- [ ] **Icon System**: 모든 아이콘을 `Segoe Fluent Icons`로 교체 (Remix Icon 대체)
- [ ] **Unified Bar**: 네비게이션 버튼, 주소창(Breadcrumb), 액션 버튼을 하나의 컴팩트한 바에 통합
- [ ] **Visual Consistency**: `style.css`에서 정의한 색상 체계와 Mica 효과를 WinUI 3 ThemeResource로 최적화 이식
- [ ] **Miller Column Polish**: 개별 아이템의 디자인(아이콘, 텍스트, 화살표)을 프로토타입 수준으로 고도화

## 3. Tasks
- [ ] **Task 1: Icon Mapping & Resources**
  - `App.xaml`에 `Segoe Fluent Icons` 폰트 리소스 및 프로토타입 컬러(`--accent` 등) 정의
- [ ] **Task 2: MainWindow Layout Redesign**
  - `MainWindow.xaml`을 `index.html` 구조로 전면 재작성
  - `Unified Bar` 구현: `Breadcrumb`와 `CommandBar`의 물리적 결합
- [ ] **Task 3: Fluent Icon Integration**
  - 모든 `Button` 및 `NavigationViewItem`의 아이콘을 `FontIcon` (Segoe Fluent Icons)으로 교체
- [ ] **Task 4: Miller Column Item Styling**
  - `MillerColumnView.xaml`의 `DataTemplate`을 프로토타입의 `.miller-item` 디자인으로 수정
- [ ] **Task 5: Breadcrumb Implementation**
  - 현재 경로를 보여주는 Breadcrumb 컨트롤 기초 구현

## 4. Schedule
- 시작일: 2026-02-11
- 목표 완료일: 2026-02-11

## 5. Resources
- `design/index.html`, `design/style.css`: 최우선 참조 디자인
- [Segoe Fluent Icons Glyphs](https://learn.microsoft.com/en-us/windows/apps/design/style/segoe-fluent-icons-font): 아이콘 매핑용
