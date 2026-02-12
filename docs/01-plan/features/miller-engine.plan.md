# Plan: Miller Column Engine Implementation (miller-engine)

## 1. Overview
Span 프로젝트의 핵심 아이덴티티인 "Miller Columns(밀러 컬럼)" 뷰와 실제 로컬 파일 시스템을 연동하는 로직을 구현합니다. 단순히 UI만 보여주는 것이 아니라, 실제 C 드라이브 등의 폴더 구조를 탐색할 수 있어야 합니다.

## 2. Goals
- [ ] **Core Logic**: `System.IO` 기반의 파일 시스템 읽기 서비스 구현 (`FileService`)
- [ ] **Data Models**: `FileItem`, `FolderItem`, `DriveItem` 실제 구현
- [ ] **UI Component**: 가로로 확장되는 `MillerColumnView` 구현 (ItemsRepeater 활용)
- [ ] **Interaction**: 폴더 클릭 시 하위 컬럼 동적 생성 및 애니메이션
- [ ] **Performance**: 비동기(Async) 로딩으로 UI 프리징 방지

## 3. Tasks
- [ ] **Task 1: File System Service**
  - `src/Span/Services/FileService.cs`: `GetDrives()`, `GetItemsAsync(path)` 구현
  - 드라이브 및 폴더/파일 구분 로직
- [ ] **Task 2: Miller Column ViewModel**
  - `src/Span/ViewModels/MillerColumnViewModel.cs`: 컬럼별 상태 관리
  - `src/Span/ViewModels/ColumnViewModel.cs`: 개별 컬럼의 아이템 리스트 관리
- [ ] **Task 3: Miller Column UI**
  - `src/Span/Views/MillerColumnView.xaml`: 가로 `ScrollViewer` + `ItemsControl` (또는 `ListView` 체인)
  - 폴더 선택 시 우측에 새 컬럼 추가하는 로직
- [ ] **Task 4: Integration**
  - `MainPage.xaml`에 "Coming Soon" 텍스트 제거하고 `MillerColumnView` 배치

## 4. Schedule
- 시작일: 2026-02-11
- 목표 완료일: 2026-02-11

## 5. Resources
- `Project Span.md`: "Phase 2: The Miller Engine" 참조
- WinUI 3 `ItemsRepeater` 문서 참조
