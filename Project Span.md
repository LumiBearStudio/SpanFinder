📂 Project Span: High-Performance Lateral Explorer
"Expand your view, Span your files."

1. 프로젝트 개요 (Overview)
앱 이름: Span (스팬)

핵심 컨셉: 윈도우 환경에서 가장 빠르고 가벼운 밀러 컬럼(Miller Columns) 기반 파일 탐색기.

타겟 유저: 맥의 Finder 경험을 선호하는 개발자, 디자이너, 그리고 **'빠릿함'**에 목마른 윈도우 파워 유저.

개발 철학:

Zero Lag: 모든 UI는 가상화(Virtualization)되어야 하며, I/O는 비동기(Async)로 처리한다.

Native Feel: 윈도우 11의 WinUI 3 디자인 언어(Mica, Fluent)를 완벽하게 따른다.

Keyboard First: 마우스 없이도 모든 탐색과 조작이 가능해야 한다.

2. 기능 명세 (Functional Specifications v1.0)
A. 뷰 & 네비게이션 (View & Navigation)
밀러 컬럼 뷰 (Miller Columns - Default):

좌우 스크롤을 통한 계층 이동.

선택 시 하위 폴더가 우측 패널로 즉시 확장(Span).

각 컬럼 너비 조절 가능 (Resizing).

3단 뷰 모드 전환:

커맨드 바 또는 단축키로 즉시 전환.

자세히 보기 (List): 파일 크기, 날짜 등 속성 중심의 그리드 뷰.

아이콘 보기 (Grid): 썸네일 중심의 갤러리 뷰 (이미지/영상 확인용).

분할 뷰 (Split View):

하나의 탭 화면을 좌/우로 2등분.

좌측 패널 ↔ 우측 패널 간 파일 드래그 앤 드롭 이동/복사 지원.

B. 윈도우 & 탭 관리 (Window & Tabs)
탭 브라우징 (Tabbed Interface):

크롬/엣지 스타일의 상단 탭 바.

Ctrl+T(새 탭), Ctrl+W(탭 닫기).

탭 분리 (Tear-off Tabs): [Killer Feature]

탭을 마우스로 잡고 창 밖으로 드래그하면 새로운 윈도우로 독립.

반대로, 독립된 윈도우의 탭을 다른 윈도우로 합치기(Docking) 가능.

C. 파일 조작 (Operations)
커맨드 바 (Command Bar):

윈도우 11 스타일의 심플한 상단 메뉴.

새로 만들기, 잘라내기/복사/붙여넣기, 삭제, 정렬, 보기, ...

컨텍스트 메뉴:

우클릭 시 윈도우 시스템 쉘 메뉴(Shell Context Menu) 호출 (알집, 반디집 등 외부 확장 프로그램 호환).

미리보기 (Preview):

밀러 컬럼의 가장 우측 패널에 선택된 파일의 메타데이터 및 썸네일 표시.

분할 뷰에서는 기본 OFF, 커맨드 바 토글(Ctrl+Shift+P)로 활성 패널에만 표시.

D. 안전 장치 (Safety & Reliability) ⚠️
실수로 파일을 잃지 않도록 하는 **필수** 보호 기능:

Undo/Redo (Ctrl+Z / Ctrl+Y):

삭제, 이동, 이름 바꾸기 등 파일 조작에 대한 되돌리기/다시 실행.

내부 작업 히스토리 스택 관리 (최근 50개).

상태바에 "이동 완료 — Ctrl+Z로 되돌리기" 토스트 표시.

휴지통 vs 영구삭제:

Delete 키: 휴지통으로 이동 (기본 동작).

Shift+Delete: 영구 삭제 (확인 다이얼로그 표시).

파일 충돌 처리 (Conflict Resolution):

동일 이름 파일 존재 시 다이얼로그 표시:
  - 덮어쓰기 (Replace)
  - 건너뛰기 (Skip)
  - 둘 다 유지 (Keep Both — 자동 넘버링)
  - 나머지 항목에 모두 적용 체크박스.

파일 작업 진행률 (Progress):

대용량 복사/이동 시 진행률 표시 (플라이아웃 또는 하단 패널).

현재 파일명, 전체 진행률(%), 남은 시간, 속도(MB/s) 표시.

일시정지/취소 버튼 제공.

E. 선택 및 편집 (Selection & Editing) ✏️
밀러 컬럼에서의 효율적인 파일 선택과 편집:

다중 선택 (Multi-Select):

Ctrl+클릭: 개별 항목 추가 선택.

Shift+클릭: 범위 선택.

Ctrl+A: 현재 컬럼 전체 선택.

선택 수는 상태바에 실시간 반영.

인라인 이름 바꾸기 (Inline Rename):

F2 키 또는 느린 더블 클릭으로 항목 이름 바로 편집.

확장자를 제외한 파일명만 자동 선택.

Esc 취소, Enter 확정.

외부 드래그 앤 드롭 (External Drag & Drop):

Span → 바탕화면/다른 앱: 파일 드래그 아웃 지원.

바탕화면/다른 앱 → Span: 파일 드래그 인 수용.

드롭 시 이동(기본) / Ctrl 누르면 복사 / 폴더 위 호버 시 자동 확장.

폴더/파일 크기 계산:

상태바에 선택된 항목의 총 크기 실시간 표시.

폴더 우클릭 속성에서 하위 전체 크기 비동기 계산.

F. 어드레스바 & UX 보조 (Address Bar & UX) 🧭
빠른 이동과 일관된 사용 경험:

주소창 직접 입력 (Ctrl+L):

빵크럼 바 클릭 시 텍스트 입력 모드로 전환.

경로 직접 입력, UNC 경로(\\server\share), 환경 변수(%APPDATA%) 지원.

자동 완성(Auto-Complete) 지원 — 입력 중 폴더 후보 드롭다운.

Quick Look (Space 키 프리뷰):

파일 선택 후 Space 키 한 번으로 즉석 프리뷰 오버레이.

이미지, PDF, 텍스트, 마크다운, 코드 등 빠른 확인.

Esc로 닫기. 풀스크린 아님, 라이트한 팝업.

정렬 및 컬럼 너비 기억:

폴더별 정렬 방식(이름/날짜/크기/종류) 기억.

밀러 컬럼 각 열의 사용자 조정 너비 기억.

설정 초기화 옵션 제공.

3. 기술 스택 및 아키텍처 (Tech Stack)
프레임워크: WinUI 3 (Windows App SDK)

최신 윈도우 렌더링 엔진 사용, 고해상도 DPI 지원, Mica 소재 적용 용이.

언어: C# (.NET 8 or 9)

디자인 패턴: MVVM (Model-View-ViewModel)

CommunityToolkit.Mvvm 라이브러리 적극 권장 (보일러플레이트 코드 최소화).

성능 핵심 기술:

UI Virtualization: ItemsRepeater 또는 ListView의 가상화 기능을 사용하여 10만 개의 파일도 끊김 없이 스크롤.

Async/Await: EnumerateFilesAsync 등을 활용하여 UI 스레드 블로킹 방지.

Span<T> / Memory<T>: 대용량 파일 IO 처리 시 메모리 효율 극대화 (이름값 하기).

4. UI 레이아웃 (Wireframe Description)
Plaintext
[ Window Title Bar (Mica Material Background) ]
[ 탭 영역:  [📂 프로젝트 X] [📂 문서] [ + ]                                     _ □ X ]
-------------------------------------------------------------------------------------
[ Command Bar: (+)New  (Cut)(Copy)(Paste)  (Sort v) (View v) ...          (Search)  ]
-------------------------------------------------------------------------------------
[ Address Bar:  C:\ > Users > Dev > Span > Source            [Ctrl+L: 직접 입력]    ]
-------------------------------------------------------------------------------------
| [Sidebar]  | [Split A (Active)]                 | [Split B (Inactive)]            |
|            |                                    |                                 |
| ★ 즐겨찾기 |  User     | >Dev      |  Assets    |  (다른 경로 표시 가능)            |
| 💻 내 PC   |  Public   |  Design   | >Source    |                                 |
| 🗑 휴지통   |  Windows  |  Docs     |  WPF_Test  |                                 |
|            |           |           |  WinUI3    |                                 |
|            |           |           |            |                                 |
-------------------------------------------------------------------------------------
[ Status Bar:  12 items selected  |  Total 450 MB  |  Ctrl+Z 되돌리기 가능          ]

5. 개발 로드맵 (Roadmap)
Phase 1: Project Setup & Core Logic (기반 다지기)

WinUI 3 솔루션 Span.sln 생성.

기본 레이아웃(NavView, TabView) 구성.

파일 시스템 읽기 로직 (ViewModel) 구현.

Phase 2: The Miller Engine (밀러 컬럼 구현)

가로로 확장되는 List 구조 구현.

ItemsRepeater를 활용한 밀러 컬럼 UI 렌더링.

다중 선택, 인라인 이름 바꾸기, 정렬/컬럼 너비 기억.

가장 중요한 성능 최적화 단계.

Phase 3: File Operations & Safety (파일 조작 & 안전)

Undo/Redo 히스토리 스택 구현.

파일 복사/이동 진행률 표시 및 충돌 처리 다이얼로그.

외부 드래그 앤 드롭 (OLE Drag & Drop) 연동.

휴지통 / 영구삭제 분기 처리.

Phase 4: Window Management (탭 & 창 관리)

탭 드래그 앤 드롭 이벤트 처리.

새 윈도우 인스턴스 생성 및 데이터(ViewModel) 전달 로직 구현.

분할 뷰 구현 및 패널 간 D&D.

Phase 5: Polish & Deploy (완성도)

커맨드 바 기능 연결.

아이콘/리스트 뷰 모드 추가.

Quick Look(Space 프리뷰), 주소창 직접 입력(Ctrl+L).

다크 모드 테스트 및 배포 패키징(MSIX).

Phase 6: Remote Drives (원격 드라이브 — 차기 버전) 🔜

클라우드 드라이브 통합 (OneDrive, Google Drive 등).

SMB(네트워크 공유 폴더) 탐색 지원.

FTP/SFTP 원격 파일 탐색 및 전송.

사이드바에 원격 드라이브 섹션 추가, 연결 상태 표시.