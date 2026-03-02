# SPAN Finder - Feature Reference

> Windows용 고성능 Miller Columns 파일 탐색기
> 최종 업데이트: 2026-03-01

---

## 뷰 모드 (4종 + 서브모드)

| 뷰 | 단축키 | 설명 |
|-----|--------|------|
| Miller Columns | `Ctrl+1` | macOS Finder 스타일 계층 탐색, 컬럼 폭 드래그 조절 |
| Details | `Ctrl+2` | 테이블 뷰 (Name, Location, Date, Type, Size), 컬럼 정렬/필터 |
| List | `Ctrl+3` | 고밀도 멀티컬럼 리스트 (세로 흐름) |
| Icons | `Ctrl+4` | Small(16) / Medium(48) / Large(96) / ExtraLarge(256), `Ctrl+Wheel`로 크기 조절 |

특수 모드: **Home** (빠른 액세스 + 최근 폴더), **Settings** (임베디드 설정 탭)

---

## 키보드 단축키

### 파일 조작

| 단축키 | 동작 |
|--------|------|
| `Ctrl+C` | 복사 |
| `Ctrl+X` | 잘라내기 |
| `Ctrl+V` | 붙여넣기 |
| `Ctrl+Shift+V` | 바로가기(.lnk)로 붙여넣기 |
| `Ctrl+D` | 복제 (파일명 + " copy") |
| `Ctrl+Z` | 실행 취소 |
| `Ctrl+Y` | 다시 실행 |
| `Ctrl+A` | 전체 선택 |
| `Ctrl+Shift+A` | 선택 해제 |
| `Ctrl+I` | 선택 반전 |
| `Ctrl+Shift+N` | 새 폴더 |
| `F2` | 이름 변경 — 연속 누르면 사이클: 파일명만 → 전체이름 → 확장자만 (다중 선택 시 배치 이름 변경 다이얼로그) |
| `F5` | 새로고침 |
| `Delete` | 휴지통으로 삭제 |
| `Shift+Delete` | 영구 삭제 |
| `Alt+Enter` | 속성 보기 |

### 네비게이션

| 단축키 | 동작 |
|--------|------|
| `Alt+←` | 뒤로 |
| `Alt+→` | 앞으로 |
| `←` / `→` | Miller 컬럼 간 이동 |
| `Home` / `End` | 목록 처음 / 끝으로 이동 |
| `Enter` | 폴더 열기 / 파일 실행 |
| `Backspace` | 이전 컬럼으로 |
| `Space` | Quick Look 미리보기 (설정에서 활성화) |
| `A-Z, 0-9` | Type-Ahead 검색 (800ms 버퍼) |

### 탭 & 창

| 단축키 | 동작 |
|--------|------|
| `Ctrl+T` | 새 탭 |
| `Ctrl+W` | 탭 닫기 |
| `Ctrl+N` | 새 창 |
| `Ctrl+Tab` | 분할 뷰 패널 전환 |

### UI & 도구

| 단축키 | 동작 |
|--------|------|
| `Ctrl+L` / `Alt+D` / `F4` | 주소 표시줄 편집 모드 |
| `Ctrl+F` | 검색 포커스 |
| `Ctrl+E` / `Ctrl+Shift+E` | 분할 뷰 토글 |
| `Ctrl+P` / `Ctrl+Shift+P` | 미리보기 패널 토글 |
| `Ctrl+1~4` | 뷰 모드 전환 |
| `Ctrl+Shift+=` | Miller 컬럼 너비 균등화 |
| `Ctrl+Shift+-` | Miller 컬럼 자동 너비 맞춤 |
| `Ctrl+,` | 설정 탭 열기 |
| `Ctrl+`` / `Ctrl+'` | 터미널 열기 |
| `F1` / `Shift+?` | 도움말 오버레이 |

### 마우스

| 동작 | 설명 |
|------|------|
| 뒤로/앞으로 버튼 | `Alt+←` / `Alt+→` 동일 |
| `Ctrl+Wheel` | 아이콘 뷰 크기 조절 |
| 러버밴드 드래그 | 범위 선택 |
| 컬럼 경계 드래그 | Miller 컬럼 폭 조절 |
| 컬럼 경계 더블클릭 | 해당 컬럼 내용에 맞게 자동 너비 조절 |
| `Ctrl`+컬럼 드래그 | 모든 컬럼에 동일 너비 일괄 적용 |

---

## 탭 관리

- 다중 탭 동시 운영 (탭별 독립 히스토리)
- 탭 복제 (경로, 뷰 모드, 아이콘 크기 유지)
- 탭 닫기: 단일 / 다른 탭 모두 / 오른쪽 모두
- 탭 Tear-Off: 탭을 새 창으로 분리 (TabStateDto 직렬화)
- 세션 저장/복원: 앱 종료 후 재시작 시 탭 상태 자동 복원

---

## 파일 조작

- **복사/이동/삭제**: 진행률 표시, 일시정지/재개/취소 지원
- **실행 취소/다시 실행**: 최대 50개 히스토리 (설정 가능, 10~100), 복사/이동/삭제/이름변경/압축/해제 모두 Undo 지원
- **새 파일/폴더 생성**
- **인라인 이름 변경**: F2로 편집, Enter 확정, Esc 취소, F2 반복 시 선택 사이클 (파일명→전체→확장자)
- **배치 이름 변경**: 찾기/바꾸기 (정규식, 대소문자 구분), 접두사/접미사, 번호 매기기 ({name}/{n}/{ext} 패턴), 실시간 미리보기 + 충돌 감지
- **파일 복제**: `Ctrl+D`
- **ZIP 압축/해제**: 컨텍스트 메뉴에서 실행
- **충돌 해결**: 파일명 중복 시 자동 접미사 " (n)" 추가
- **동시 작업**: 여러 복사/이동 작업 병렬 실행

---

## 네비게이션

- **히스토리 스택**: Back/Forward (최대 50개), 드롭다운 히스토리 목록
- **주소 표시줄**: Breadcrumb 모드 (클릭 탐색) + 편집 모드 (Ctrl+L)
- **Type-Ahead 검색**: 문자 입력으로 항목 자동 선택 (800ms 타이머)
- **경로 하이라이트**: 현재 경로 항목 시각적 강조

---

## 사이드바

### 드라이브
- **로컬 드라이브**: HDD/SSD/USB 자동 감지
- **네트워크 드라이브**: 매핑된 드라이브 표시
- **클라우드 스토리지**: OneDrive, iCloud, Dropbox, Google Drive 자동 감지
  - SyncRootManager 레지스트리 + Navigation Pane CLSID + 프로바이더별 직접 감지
- **USB 핫플러그**: WM_DEVICECHANGE로 연결/분리 실시간 감지

### 즐겨찾기 & 최근 폴더
- 즐겨찾기 추가/제거 (드래그 드롭 또는 컨텍스트 메뉴)
- 즐겨찾기 순서 변경 (드래그 리오더)
- 최근 방문 폴더 (최대 20개, 자동 저장)
- 빠른 액세스 동기화

### 원격 연결
- **FTP/FTPS**: FluentFTP 라이브러리 기반 (디렉토리 탐색, 업로드/다운로드)
- **SFTP**: SSH 키 인증 + 비밀번호 인증 지원
- **SMB**: WNetEnumResourceW P/Invoke 기반 네트워크 탐색, NetShareEnum으로 공유 폴더 열거
- 연결 정보 저장/관리 (ConnectionManagerService)
- 관리자 공유($-접미사) 자동 필터링
- 작업당 5초 타임아웃

---

## 분할 뷰 (Split View)

- `Ctrl+E`로 토글
- 좌/우 패널 독립 탐색, 독립 뷰 모드
- `Ctrl+Tab`으로 패널 전환
- 패널별 독립 미리보기 패널
- 상태 저장/복원

---

## 미리보기 패널

- `Ctrl+P`로 토글, `Space`로 Quick Look (모달 다이얼로그)
- **지원 포맷**:
  - 이미지: JPEG, PNG, GIF, BMP, WebP, TIFF 등
  - 비디오: MP4, MKV, AVI, MOV, WMV, WEBM 등 + 메타데이터
  - 오디오: MP3, AAC, M4A 등 (Artist, Album, Duration)
  - 텍스트: TXT, JSON, XML, CSV, MD 등 (30+ 확장자)
  - PDF: 첫 페이지 미리보기
  - 바이너리: Hex 덤프 뷰어 (첫 512바이트, 16진수 + ASCII 표시)
  - 폰트: 폰트 파일 미리보기
- 이미지/파일 메타데이터 (해상도, 크기, 날짜, 생성일, 타입)
- 200ms 디바운싱 (빠른 선택 최적화)
- 클라우드 전용 파일: 캐시된 썸네일만 사용 (다운로드 방지)
- FTP/SFTP 원격 파일 미리보기 지원

---

## 클라우드 동기화 상태

- **Cloud Files API (cfapi)** 기반 파일 속성 감지
- 상태별 원형 배지 오버레이:
  - 🔵 **CloudOnly**: 클라우드에만 존재 (파란 원 + 구름 아이콘)
  - 🟢 **Synced**: 로컬 동기화 완료 (초록 원 + 체크 아이콘)
  - 🟠 **PendingUpload**: 업로드 대기 (주황 원 + 업로드 아이콘)
  - 🔵 **Syncing**: 동기화 중 (파란 원 + 동기화 아이콘)
- 지원 프로바이더: OneDrive, iCloud, Dropbox (cfapi 기반)
- On-demand 주입: 보이는 항목에만 상태 계산 (ContainerContentChanging)

---

## 컨텍스트 메뉴

- **Windows Shell 네이티브 메뉴**: 셸 확장 프로그램 메뉴 100% 지원
- 셸 Verb 다국어 번역 (한국어, 일본어)
- **파일/폴더 메뉴**: 열기, 연결 프로그램, 잘라내기, 복사, 붙여넣기, 이름 변경, 삭제, 영구 삭제, 복제, 바로가기 생성, 속성
- **빈 영역 메뉴**: 새 파일/폴더, 정렬(서브메뉴), 그룹화(서브메뉴), 탐색기에서 열기
- **추가 항목**: 터미널 열기, 경로 복사, 빠른 액세스에 고정, ZIP 압축/해제
- **즐겨찾기 메뉴**: 탐색, 고정 해제, 이름 편집
- **정렬 서브메뉴**: 이름/날짜/크기/종류, 오름차순/내림차순, 그룹화 옵션

---

## 드래그 & 드롭

- **내부**: Span 내 파일 이동/복사 (Ctrl=복사, Shift=이동)
- **외부 → Span**: Windows Explorer, Desktop에서 파일 드롭
- **Span → 외부**: Windows Explorer, Desktop으로 파일 드래그
- **즐겨찾기 드롭**: 사이드바에 파일 드롭으로 즐겨찾기 추가
- **분할 뷰 간 드래그**: 좌↔우 패널 간 파일 이동/복사
- **Spring-loaded 폴더**: 드래그 중 폴더 위 호버 시 자동 열림 (800ms 지연)
- StorageItems 지연 로딩 지원
- 수정키: `Shift`=이동 강제, `Ctrl`=복사 강제 (같은 드라이브=이동, 다른 드라이브=복사 기본)

---

## 선택

- 단일 선택, `Ctrl+Click` 다중 선택, `Shift+Click` 범위 선택
- `Ctrl+A` 전체, `Ctrl+Shift+A` 해제, `Ctrl+I` 반전
- 러버밴드 (마우스 드래그) 범위 선택
- 체크박스 모드 (설정에서 토글)

---

## 썸네일

- On-demand 로드: 보이는 아이템만 (ContainerContentChanging)
- 동시 로딩 제한: 최대 6개 (SemaphoreSlim)
- 로컬 이미지: 직접 디코딩 (BitmapImage)
- 비디오/클라우드: Shell Thumbnail API (캐시만 사용)
- 대용량 스킵: 20MB 초과 파일 제외
- 메모리 관리: 컬럼 제거 시 썸네일 해제

---

## 상태 표시줄

- 아이템 수 / 선택 수 표시
- 디스크 여유 공간 / 전체 용량 표시
- 선택 파일 크기 합산 표시
- 검색 결과 수 표시 ("Search: N results")
- 재귀 검색 진행 상황 표시 ("검색 중... N개 발견 (M개 폴더 탐색)")
- InfoBar 알림 (배치 이름 변경 충돌, 작업 결과 등)

---

## 검색

- **Type-Ahead**: 밀러 컬럼 내 문자 입력 즉시 필터링 (800ms 버퍼)
- **검색 박스**: `Ctrl+F`로 포커스, `Enter`로 재귀 검색 실행, `Esc`로 취소/초기화

### 재귀 검색 (RecursiveSearchService)

- **검색 범위**: 네비게이션 루트(Columns[0]) 하위 전체 폴더를 BFS로 순회 (macOS Finder 방식)
  - Miller Columns에서 D:\ → Projects → src 로 깊이 진입해도 D:\ 전체 검색
- **Channel 기반 Producer-Consumer 패턴**: UI 프리징 없는 백그라운드 검색
  - Producer: `Task.Run`으로 백그라운드 스레드에서 BFS 파일 시스템 순회
  - Consumer: UI 스레드에서 `ReadAllAsync`로 배치 수신 → `ObservableCollection`에 추가
  - `BoundedChannel(16)`: 배압(backpressure) 제어로 메모리 보호
  - 배치 크기 50개: 매 배치 후 `Task.Yield()`로 UI 양보
- **`EnumerationOptions.IgnoreInaccessible = true`**: 접근 불가 항목 자동 건너뜀 (전체 루프 중단 방지)
- **`AttributesToSkip`**: 숨김 파일 필터링을 열거자 레벨에서 처리
- 최대 **10,000개** 결과 제한 (메모리 보호)
- 20폴더마다 진행 상황 보고 (`IProgress<SearchProgress>`)
- 검색 중 상태바에 "검색 중... N개 발견 (M개 폴더 탐색)" 실시간 표시
- `Escape`로 검색 취소 → 원래 Columns/Path 상태 즉시 복원 (`_preSearchColumns` 저장/복원)
- Details 뷰에서 **Location 컬럼** 자동 표시/숨김 (검색 루트 기준 상대 경로)
- 검색 결과에서 폴더 더블클릭 → 검색 취소 + 해당 경로로 이동
- 검색 결과에서 파일 더블클릭 → 파일 실행 (검색 유지)
- 가상 FolderViewModel "검색 결과: {폴더명}" 동적 생성 (`MarkAsManuallyPopulated()`)

### 와일드카드 검색

- `*.exe`, `*.mp3`, `report*`, `test?.doc` 등
- `*` = 0개 이상 문자, `?` = 정확히 1개 문자
- 내부적으로 `Regex` 변환: `Regex.Escape` → `\*` → `.*`, `\?` → `.` → `^...$` 앵커 (전체 파일명 매칭)
- 와일드카드 없는 일반 텍스트 = `String.Contains` 부분일치 (대소문자 무시)

### 고급 검색 쿼리 문법 (SearchQueryParser)

- **정확 구문 검색**: `"quarterly report"`, `'hello world'` — 단일/이중 따옴표 모두 지원
- **kind: 필터** — 파일 종류별 필터 (30개+ 별칭 지원)
  - `kind:image` (= photo, photos, picture, pictures, pic, img, images)
  - `kind:video` (= videos, movie, movies, film)
  - `kind:audio` (= music, sound, sounds, song, songs)
  - `kind:document` (= documents, doc, docs, text)
  - `kind:archive` (= archives, zip, compressed)
  - `kind:code` (= source, script, scripts, program)
  - `kind:exe` (= executable, executables, app, application)
  - `kind:font` (= fonts)
  - 폴더는 kind 필터에서 자동 제외 (파일만 매칭)
- **size: 필터** — 크기 비교
  - 연산자: `size:>1MB`, `size:<100KB`, `size:>=500B`, `size:<=2GB`, `size:=10MB`
  - 단위: B / KB / MB / GB / TB (대소문자 무시)
  - 연산자 생략 시 `>=` 기본값 (예: `size:1GB` = `size:>=1GB`)
  - 소수점 지원: `size:>1.5MB`
  - 프리셋: `empty`(=0B), `tiny`(<16KB), `small`(<1MB), `medium`(≥1MB), `large`(>128MB), `huge`/`gigantic`(>1GB)
  - 폴더는 size 필터에서 자동 제외
- **date: 필터** — 수정일 비교
  - 연산자: `date:>2024-01-01`, `date:<2024-12-31`, `date:>=`, `date:<=`, `date:=`
  - 연산자 생략 시 `>=` 기본값
  - `date:=` 는 날짜 부분만 비교 (시간 무시)
  - 프리셋: `today`, `yesterday`, `thisweek`(월요일 기준), `lastweek`, `thismonth`, `lastmonth`, `thisyear`, `lastyear`
- **ext: 필터** — 확장자
  - 단일: `ext:.pdf`, `ext:txt` (점 자동 추가)
  - 다중: `ext:jpg;png;gif` (세미콜론 구분, 하나라도 일치 시 통과)
  - 폴더는 ext 필터에서 자동 제외
- **복합 쿼리**: 여러 조건 AND 결합 (예: `kind:image size:>1MB`, `*.pdf date:thisweek`)
- **엣지 케이스**: `kind:`, `size:`, `ext:` 빈 값은 일반 텍스트로 처리, `date:notadate`도 이름 토큰으로 폴백

---

## 폴더 크기 계산

- Details 뷰에서 백그라운드 비동기 계산 (FolderSizeService)
- 계산 완료 시 UI 자동 업데이트 (SizeCalculated 이벤트)
- 결과 캐싱 (폴더 경로 기준)
- 최대 탐색 깊이: 8단계
- 심볼릭 링크(reparse point) 자동 제외
- 사람이 읽기 쉬운 형식 (B/KB/MB/GB/TB)

---

## 파일 시스템 모니터링

- FileSystemWatcher 기반 실시간 변경 감지 (Created, Deleted, Renamed 이벤트)
- 300ms 디바운싱으로 빠른 변경 배치 처리
- 버퍼 오버플로우 시 1000ms 대기 후 워처 재생성
- 외부 프로그램 변경 자동 반영
- 네트워크/원격 경로 자동 제외
- USB 장치 연결/분리 감지 (WM_DEVICECHANGE)

---

## 설정

### 외관

| 항목 | 옵션 |
|------|------|
| 테마 | Light / Dark / System / Dracula / Tokyo Night / Catppuccin / Gruvbox |
| 레이아웃 밀도 | Compact / Comfortable / Spacious |
| 폰트 | Segoe UI Variable (기본) + 시스템 폰트 선택 가능 |
| 아이콘 팩 | Remix / Phosphor / Tabler |
| 언어 | System / English / 한국어 / 日本語 |

### 탐색 동작

| 항목 | 옵션 |
|------|------|
| 시작 동작 | Home 화면 / 마지막 세션 복원 |
| 숨김 파일 표시 | On / Off |
| 파일 확장자 표시 | On / Off |
| 선택 체크박스 | On / Off |
| Miller 클릭 동작 | Single / Double |
| 썸네일 표시 | On / Off |
| Quick Look (Space) | On / Off |
| 삭제 확인 대화 | On / Off |
| 실행 취소 히스토리 | 1~100 (기본 50) |

### 도구 & 통합

| 항목 | 옵션 |
|------|------|
| 기본 터미널 | wt / cmd / powershell / custom |
| 컨텍스트 메뉴 표시 | On / Off |
| Git 통합 표시 | On / Off |
| Hex 미리보기 표시 | On / Off |
| Windows Shell 확장 | On / Off |
| Copilot 메뉴 표시 | On / Off (Windows 셸 Copilot 항목 필터) |
| 개발자 메뉴 표시 | On / Off |

### 창 & 세션

| 항목 | 옵션 |
|------|------|
| 창 위치 기억 | On / Off (기본 On) |
| 시스템 트레이 최소화 | On / Off |
| 즐겨찾기 트리 | On / Off |
| 탭 세션 저장 | On / Off |

---

## 다국어 지원

- 한국어, 일본어, 영어 UI
- Shell 컨텍스트 메뉴 Verb/Text 번역
- 한국어 키보드 스캔코드 폴백 (Ctrl+`, Ctrl+')

---

## Git 통합

- **레포지토리 자동 감지**: 현재 폴더가 Git 저장소인지 자동 판별
- **파일 상태 배지**: Modified (M), Added (A), Deleted (D), Untracked (?) 아이콘 표시
- **브랜치 정보**: 현재 브랜치명 표시
- **3단 캐시**:
  - Tier 1: 파일별 마지막 커밋 (git log -1)
  - Tier 2: 레포 대시보드 (브랜치, 최근 커밋, 변경 파일)
  - Tier 3: 폴더 전체 파일 상태 (30초 캐시)
- **타임아웃 보호**: Git 명령 8초 제한 (느린 네트워크 대응)
- 설정에서 On/Off 가능

---

## 정렬 & 그룹화

- **정렬 기준**: 이름 / 날짜 / 크기 / 종류 (오름차순/내림차순)
- **전체 뷰 공통 정렬**: Miller / Icon / List 뷰 모두 동일 정렬 적용
- **정렬 설정 저장**: 앱 재시작 시 마지막 정렬 유지
- **컨텍스트 메뉴 정렬**: 빈 영역 우클릭 → Sort 서브메뉴
- **툴바 정렬 버튼**: 정렬 필드/방향 아이콘 표시
- **Group By**: None / Name / Type / Date Modified / Size
  - Details / Icon / List 뷰에서 그룹 헤더 표시
  - 컨텍스트 메뉴 + 툴바 정렬 버튼 드롭다운에서 접근 가능
  - 설정 저장/복원

---

## 창 위치 저장

- 앱 종료 시 창 위치/크기 자동 저장
- 다음 실행 시 마지막 위치로 복원 (깜빡임 없이 즉시 적용)
- 최소/최대화 상태에서는 저장 안 함
- 최소 크기 보장 (400x300)
- 설정에서 ON/OFF 가능 (기본: ON)

---

## 멀티 윈도우

- 동시 여러 창 실행 (App.RegisterWindow/UnregisterWindow)
- 탭 Tear-Off (새 창으로 분리)
- 마지막 창 닫으면 앱 종료

---

## 성능 최적화

- **배치 업데이트**: Children 컬렉션 1회 교체 (14,000회 → 1회 PropertyChanged)
- **디바운스 선택**: 150ms 지연 (캐시 히트 시 스킵)
- **비동기 I/O**: 모든 파일 시스템 작업 백그라운드 스레드
- **취소 토큰**: 빠른 네비게이션 시 이전 로드 취소
- **폴더 캐시**: 재방문 폴더 즉시 로드 (FolderContentCache)
- **드라이브 로드 타임아웃**: 500ms (응답 없는 드라이브 스킵)

---

## 에러 처리

- 접근 거부 폴더: 에러 아이콘 + 메시지 표시
- MAX_PATH 초과 (260자): 에러 표시 + 안내
- 네트워크 연결 끊김 감지
- 에러 재시도 버튼
- 탭 복원 시 경로 존재 검증

---

## 기술 스택

- **프레임워크**: WinUI 3 (Windows App SDK 1.8)
- **언어**: C# (.NET 8)
- **아키텍처**: MVVM (CommunityToolkit.Mvvm)
- **DI**: Microsoft.Extensions.DependencyInjection
- **타겟**: net8.0-windows10.0.19041.0
- **플랫폼**: x86, x64, ARM64
