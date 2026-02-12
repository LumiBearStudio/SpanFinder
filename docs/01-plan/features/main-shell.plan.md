# Plan: Main Shell Layout Implementation (main-shell)

## 1. Overview
HTML 프로토타입의 디자인을 WinUI 3 프로젝트에 이식합니다. Mica 소재를 배경으로 사용하고, 상단 탭 바(`TabView`)와 사이드바(`NavigationView`)가 결합된 기본 셸 구조를 구축합니다.

## 2. Goals
- [ ] `MainWindow`에 Mica 소재(Backdrop) 적용
- [ ] 커스텀 타이틀 바 구성 (탭 바 영역 확보)
- [ ] `TabView`를 이용한 멀티 탭 인터페이스 기초 구현
- [ ] `NavigationView`를 이용한 왼쪽 사이드바 구성
- [ ] MVVM 패턴을 위한 `MainViewModel` 연결

## 3. Tasks
- [ ] **Task 1: 테마 및 리소스 설정**
  - `App.xaml`에 Mica 관련 리소스 및 기본 다크 테마 설정
- [ ] **Task 2: MainWindow 디자인**
  - `MainWindow.xaml`에 `NavigationView` 및 `TabView` 배치
  - 프로토타입의 컬러 시스템(Accent Color 등)을 `App.xaml`에 이식
- [ ] **Task 3: ViewModel 연결**
  - `ViewModels/MainViewModel.cs` 생성 및 `MainWindow`의 `DataContext`로 설정
- [ ] **Task 4: 타이틀 바 커스터마이징**
  - WinUI 3의 `ExtendsContentIntoTitleBar`를 사용하여 현대적인 룩앤필 구현

## 4. Schedule
- 시작일: 2026-02-11
- 목표 완료일: 2026-02-11

## 5. Resources
- `docs/03-mockup/prototype-v1/index.html`: 레이아웃 참조
- `docs/00-context/requirements.md`: 기능 사양 참조
