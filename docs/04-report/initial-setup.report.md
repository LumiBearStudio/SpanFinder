# Span Project Status Report

> **Last Updated**: 2026-02-24
> **Total Commits**: 75
> **Test Suite**: 359 tests (100% pass)

---

## 1. Project Overview

**Span**은 macOS Finder에서 영감을 받은 Windows용 고성능 Miller Columns 파일 탐색기입니다.

| Item | Detail |
|------|--------|
| Framework | WinUI 3 (Windows App SDK 1.8) |
| Language | C# (.NET 8) |
| Architecture | MVVM + CommunityToolkit.Mvvm + DI |
| Source Files | 185 C# + 33 XAML |
| Test Files | 29 C# (19 test classes) |

---

## 2. Implemented Features (116+)

### Core Navigation
- Miller Columns 엔진 (동적 컬럼 추가/제거, 선택 전파)
- 4가지 뷰 모드 (Miller / Details / Icon / List)
- 탭 시스템 (다중 탭, 탭 분리/합치기, 드래그 앤 드롭)
- 뒤로/앞으로 히스토리 네비게이션
- 주소창 (브레드크럼 + 편집 모드)
- 즐겨찾기 사이드바 (트리/플랫, 드래그 순서 변경)

### File Operations
- 복사/이동/삭제 (휴지통 + 영구 삭제)
- 인라인 리네임 (확장자 분리, F2 사이클링)
- 배치 리네임
- 압축/해제 (ZIP)
- 새 파일/폴더 생성
- 실행 취소/다시 실행 (Undo/Redo)
- 작업 진행률 표시 + 일시정지/취소

### Advanced Features
- 파일 미리보기 (이미지, 텍스트, PDF, 미디어)
- 검색 (고급 쿼리 파서: 크기/날짜/종류 필터)
- 원격 연결 (FTP/SFTP)
- 폴더 크기 계산 (백그라운드)
- 클라우드 동기화 상태 표시
- 러버밴드 선택 (Details/Icon 뷰)
- 네이티브 Shell 컨텍스트 메뉴 통합

### UI/UX
- 다크/라이트 테마 + 4종 커스텀 테마 (Dracula, Tokyo Night, Catppuccin, Gruvbox)
- 3종 아이콘 팩 (Remix, Phosphor, Tabler)
- 레이아웃 밀도 설정 (Compact/Comfortable/Spacious)
- 키보드 네비게이션 (40+ 단축키)
- 설정 탭 (일반/외관/탐색기/단축키/연결/정보)
- Mica 백드롭, Fluent 디자인

### Error Handling & Robustness
- MAX_PATH (260자+) 방어 — 크래시 방지 + 사용자 안내
- 접근 권한 에러 UI (아이콘 + 메시지 + 리트라이)
- 네트워크 단절 에러 핸들링
- PathTooLongException 방어 (모든 파일 작업)

---

## 3. PDCA Document Structure

```
docs/
├── 00-context/          # 요구사항, 설정 스펙, 기능 문서, 참조 자료
│   ├── requirements.md
│   ├── settings-spec.md
│   ├── feature-documentation.md
│   └── windows11-file-explorer-features.md
├── 01-plan/features/    # 10개 계획서
├── 02-design/features/  # 10개 설계서
├── 03-analysis/         # 분석서 + macOS Finder 참조
│   ├── features/        # 기능별 Gap 분석
│   └── macos-finder-feature-list.md
├── 03-mockup/           # HTML/CSS 프로토타입
├── 04-report/           # 이 리포트
└── archive/2026-02/     # 아카이브 (test-coverage PDCA)
```

---

## 4. Test Coverage

| Category | Testable Files | Tested | Coverage |
|----------|---------------|--------|----------|
| Models | 8 | 9 | 90% |
| Helpers | 4 | 3 | 75% |
| Services/FileOperations | 12 | 13 | 100% |
| Services (기타) | 2 | 3 | 100% |
| **합계** | **30** | **28** | **93.3%** |

- 총 359개 런타임 테스트, 19개 테스트 파일
- ViewModel/UI 계층은 WinUI 의존으로 테스트 제외 (FlaUI UI 자동화 별도)

---

## 5. Recent Changes (2026-02-24)

### MAX_PATH 방어 + 에러 UI
- `FolderViewModel`: ErrorMessage/ErrorIcon/HasError 프로퍼티, 5종 예외 핸들링
- `MainWindow.xaml`: 에러 상태 UI (아이콘 + 메시지 + 리트라이 버튼) × 2곳
- `FileOperations` (5개): PathTooLongException 구체적 에러 메시지
- `ExplorerViewModel`: NavigateToPath PathTooLongException 가드

### 문서 정리
삭제 11종:
- 루트: `Setting Span.md`, `Project Span.md`, `test-settings.md`, `bug.md`, `checklist.md`
- docs: `refactoring-plan.md`, `test-feature-list.md`, `test-checklist.md`, `03-analysis/102.md`
- 기타: `.pdca-snapshots/` (10개 JSON), `.pdca-status.json`

---

## 6. Key Files

| File | Lines | Role |
|------|-------|------|
| MainWindow.xaml | ~2,400 | 전체 UI 레이아웃 |
| MainWindow.xaml.cs | ~3,100 | 이벤트 핸들러, 포커스 관리 |
| MainViewModel.cs | ~1,900 | 탭/드라이브/즐겨찾기 관리 |
| ExplorerViewModel.cs | ~1,000 | Miller Columns 엔진 |
| FolderViewModel.cs | ~520 | 폴더 로딩/에러/캐시 |

---

## 7. Next Steps

`ROADMAP.md` 참조:
- 파일 태깅/컬러 레이블
- 중복 파일 찾기
- 터미널 통합 패널
- 빠른 실행 (Command Palette)
- 파일 비교 (diff)
