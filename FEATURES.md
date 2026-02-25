# Span - Feature Reference

> Windows용 고성능 Miller Columns 파일 탐색기
> 최종 업데이트: 2026-02-25

---

## 뷰 모드 (4종 + 서브모드)

| 뷰 | 단축키 | 설명 |
|-----|--------|------|
| Miller Columns | `Ctrl+1` | macOS Finder 스타일 계층 탐색, 컬럼 폭 드래그 조절 |
| Details | `Ctrl+2` | 테이블 뷰 (Name, Date, Type, Size), 컬럼 정렬/필터 |
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
| `F2` | 이름 변경 (다중 선택 시 배치 이름 변경 다이얼로그) |
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
- **실행 취소/다시 실행**: 최대 50개 히스토리 (설정 가능)
- **새 파일/폴더 생성**
- **인라인 이름 변경**: F2로 편집, Enter 확정, Esc 취소
- **배치 이름 변경**: 찾기/바꾸기 (정규식), 접두사/접미사, 번호 매기기
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
- SMB, FTP, FTPS, SFTP 프로토콜 지원
- 연결 정보 저장/관리 (ConnectionManager)

---

## 분할 뷰 (Split View)

- `Ctrl+E`로 토글
- 좌/우 패널 독립 탐색, 독립 뷰 모드
- `Ctrl+Tab`으로 패널 전환
- 패널별 독립 미리보기 패널
- 상태 저장/복원

---

## 미리보기 패널

- `Ctrl+P`로 토글, `Space`로 Quick Look
- **지원 포맷**:
  - 이미지: JPEG, PNG, GIF, BMP, WebP, TIFF 등
  - 비디오: MP4, MKV, AVI, MOV, WMV, WEBM 등 + 메타데이터
  - 오디오: MP3, AAC, M4A 등 (Artist, Album, Duration)
  - 텍스트: TXT, JSON, XML, CSV, MD 등 (30+ 확장자)
  - PDF: 첫 페이지 미리보기
- 이미지/파일 메타데이터 (해상도, 크기, 날짜)
- 200ms 디바운싱 (빠른 선택 최적화)
- 클라우드 전용 파일: 캐시된 썸네일만 사용 (다운로드 방지)

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
- 추가 항목: 터미널 열기, 경로 복사, 빠른 액세스에 고정, 압축/해제

---

## 드래그 & 드롭

- **내부**: Span 내 파일 이동/복사 (Ctrl=복사, Shift=이동)
- **외부 → Span**: Windows Explorer, Desktop에서 파일 드롭
- **Span → 외부**: Windows Explorer, Desktop으로 파일 드래그
- **즐겨찾기 드롭**: 사이드바에 파일 드롭으로 즐겨찾기 추가
- StorageItems 지연 로딩 지원

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
- 토스트 알림 (작업 완료/에러)

---

## 검색

- **Type-Ahead**: 밀러 컬럼 내 문자 입력 즉시 필터링
- **검색 박스**: `Ctrl+F`로 포커스
- **SearchQueryParser**: 구조화된 검색 쿼리 파싱
- **SearchFilter**: 검색 조건 필터링

---

## 폴더 크기 계산

- Details 뷰에서 백그라운드 계산 (FolderSizeService)
- 계산 완료 시 UI 자동 업데이트
- 결과 캐싱
- 사람이 읽기 쉬운 형식 (B/KB/MB/GB/TB)

---

## 파일 시스템 모니터링

- FileSystemWatcher 기반 실시간 변경 감지
- 외부 프로그램 변경 자동 반영
- USB 장치 연결/분리 감지 (WM_DEVICECHANGE)

---

## 설정

| 항목 | 옵션 |
|------|------|
| 테마 | Light / Dark / System |
| 레이아웃 밀도 | Compact / Comfortable / Spacious |
| 폰트 | Segoe UI Variable (기본) + 시스템 폰트 |
| 아이콘 팩 | Remix / Phosphor / Tabler |
| 언어 | System / English / 한국어 / 日本語 |
| 시작 동작 | Home 화면 / 마지막 세션 복원 |
| 숨김 파일 표시 | On / Off |
| 파일 확장자 표시 | On / Off |
| 선택 체크박스 | On / Off |
| Miller 클릭 동작 | Single / Double |
| 썸네일 표시 | On / Off |
| Quick Look (Space) | On / Off |
| 삭제 확인 대화 | On / Off |
| 실행 취소 히스토리 | 1~100 (기본 50) |
| 기본 터미널 | wt / cmd / powershell / custom |
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
