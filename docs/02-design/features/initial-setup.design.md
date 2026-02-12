# Design: Initial Project Setup & Architecture (initial-setup)

## 1. System Architecture
- **Framework**: WinUI 3 (Windows App SDK)
- **Runtime**: .NET 9
- **Pattern**: MVVM (Model-View-ViewModel) using CommunityToolkit.Mvvm
- **Project Type**: Desktop (Packaged/Unpackaged TBD)

## 2. Directory Structure Design
본격적인 개발을 위한 표준 폴더 구조 설계입니다.

```
Span/
├── .gitignore              # .NET/VS 표준 제외 설정
├── README.md               # 프로젝트 개요
├── Span.sln                # 메인 솔루션
├── docs/                   # PDCA 및 기획 문서
│   ├── 00-context/         # 원본 요구사항 및 스펙
│   ├── 01-plan/            # 기능별 PDCA 계획
│   ├── 02-design/          # 상세 설계서
│   ├── 03-mockup/          # 디자인 자산 및 프로토타입
│   └── 04-report/          # 완료 보고서
└── src/                    # 소스 코드
    └── Span/               # 메인 WinUI 3 프로젝트
        ├── Span.csproj
        ├── App.xaml
        ├── MainWindow.xaml
        ├── Models/         # 데이터 모델 (File, Folder, Drive)
        ├── ViewModels/     # 비즈니스 로직 및 상태 관리
        ├── Views/          # UI 컴포넌트 (MillerColumns, PreviewPane)
        ├── Services/       # 파일 시스템 IO, 설정 서비스
        ├── Helpers/        # 공통 유틸리티
        └── Assets/         # 이미지, 아이콘 리소스
```

## 3. Data Model Design (Initial)
파일 시스템 탐색을 위한 기본 인터페이스 정의입니다.

- `IFileSystemItem`: 공통 인터페이스
- `FileItem`: 파일 객체 (Name, Path, Size, ModifiedDate)
- `FolderItem`: 폴더 객체 (Name, Path, Children)
- `DriveItem`: 드라이브 객체 (Name, RootPath, TotalSpace, FreeSpace)

## 4. UI/UX Component Layout
`docs/03-mockup/prototype-v1/`의 설계를 WinUI 3 컨트롤로 매핑합니다.

- **MainShell**: `NavigationView`를 활용한 레이아웃
- **Tabs**: `TabView` 컨트롤 사용
- **MillerColumnView**: `ItemsRepeater` 또는 커스텀 가상화 패널을 이용한 가로 스크롤 레이아웃
- **PreviewPane**: 우측 고정 패널, 선택된 아이템의 상세 정보 표시

## 5. Implementation Strategy
1.  루트 폴더 정리 (기존 design 폴더 및 중복 md 파일 삭제 유도)
2.  Visual Studio 2022에서 WinUI 3 Template 프로젝트 생성
3.  MVVM Toolkit NuGet 패키지 설치 및 기본 DI 설정
4.  로드맵 Phase 1(기반 다지기) 시작

## 6. Verification Plan
- [ ] 솔루션이 정상적으로 빌드되는가?
- [ ] MVVM 패턴에 따라 View와 ViewModel이 분리되어 있는가?
- [ ] 가상화된 리스트 레이아웃이 대량의 파일(10,000+)에서도 지연 없이 동작하는가? (초기 테스트 필요)
