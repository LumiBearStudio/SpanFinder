# SPAN Finder

**타협 없는 파워 유저를 위한, 초고속 밀러 컬럼(Miller Columns) 파일 탐색기.**

[English](../README.md) | 한국어 | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder는 Windows의 파일 탐색을 새롭게 정의합니다. macOS Finder 컬럼 뷰의 우아함에서 영감을 받아, Windows 탐색기에는 없었던 기능들을 탑재했습니다 — 멀티 탭, 분할 뷰, 비동기 작업, 키보드 중심 워크플로우로 파일 관리가 한결 수월해집니다.

> **Windows 탐색기에 만족하고 계신가요?**

[![Microsoft Store에서 다운로드](https://get.microsoft.com/images/ko-kr%20dark.svg)](https://apps.microsoft.com/detail/9P7NJ351X9TL)

![SPAN Finder — 밀러 컬럼 + 미리보기 패널](1.jpg)

---

## 왜 SPAN Finder인가?

| | Windows 탐색기 | SPAN Finder |
|---|---|---|
| **밀러 컬럼** | 없음 | 계층형 멀티 컬럼 탐색 |
| **멀티 탭** | Windows 11만 (기본) | 탭 분리, 복제, 세션 복원 완전 지원 |
| **분할 뷰** | 없음 | 독립 뷰 모드의 듀얼 패널 |
| **미리보기 패널** | 기본 | 10종 이상 — 이미지, 동영상, 오디오, 코드, Hex, 폰트, PDF |
| **키보드 탐색** | 제한적 | 30개 이상 단축키, 자동 완성 검색, 키보드 우선 설계 |
| **일괄 이름 변경** | 없음 | 정규식, 접두사/접미사, 순번 매기기 |
| **실행 취소/다시 실행** | 제한적 | 전체 작업 이력 (깊이 설정 가능) |
| **커스텀 테마** | 없음 | 10가지 테마 — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord 등 |
| **Git 연동** | 없음 | 브랜치, 상태, 커밋을 한눈에 |
| **원격 접속** | 없음 | FTP, FTPS, SFTP — 자격 증명 저장 |
| **클라우드 상태** | 기본 오버레이 | 실시간 동기화 뱃지 (OneDrive, iCloud, Dropbox) |
| **시작 속도** | 대용량 폴더에서 느림 | 비동기 로딩 + 취소 지원 — 지연 없음 |

---

## 기능

### 밀러 컬럼 — 모든 것을 한눈에

깊은 폴더 계층을 탐색하면서도 맥락을 잃지 않습니다. 각 컬럼이 하나의 폴더 레벨을 나타내며, 폴더를 클릭하면 다음 컬럼에 내용이 나타납니다. 현재 위치와 경로를 항상 확인할 수 있습니다.

- 드래그 가능한 컬럼 구분선으로 너비 조절
- 컬럼 균등화 (Ctrl+Shift+=) 또는 내용에 맞춤 (Ctrl+Shift+-)
- 활성 컬럼이 항상 보이도록 부드러운 가로 스크롤

### 네 가지 뷰 모드

- **밀러 컬럼** (Ctrl+1) — 계층형 탐색, SPAN Finder의 시그니처
- **상세 보기** (Ctrl+2) — 이름, 날짜, 유형, 크기 컬럼이 있는 정렬 가능한 테이블
- **목록 보기** (Ctrl+3) — 대용량 폴더 스캔을 위한 밀집 다단 레이아웃
- **아이콘 보기** (Ctrl+4) — 최대 256×256 썸네일까지 4단계 크기의 그리드 뷰

![상세 뷰 + 코드 미리보기](3.jpg)

### 멀티 탭 + 완전한 세션 복원

- 무제한 탭 — 각 탭마다 독립된 경로, 뷰 모드, 탐색 이력
- **탭 분리**: 탭을 드래그하여 새 창으로 — 상태 완전 보존
- **탭 복제**: 정확한 경로와 설정으로 탭 복제
- 세션 자동 저장: 앱을 닫았다 다시 열어도 — 모든 탭이 그대로

### 분할 뷰 — 진정한 듀얼 패널

- 독립 탐색이 가능한 좌우 파일 브라우징
- 각 패널에 서로 다른 뷰 모드 사용 가능 (왼쪽 밀러, 오른쪽 상세)
- 각 패널의 개별 미리보기 패널
- 패널 간 드래그로 복사/이동 작업

![14,000개 이상 항목의 분할 뷰](2.jpg)

### 미리보기 패널 — 열기 전에 확인

![코드 미리보기 + Git 정보](5.jpg)

**Space** 키로 Quick Look (macOS Finder 스타일):

- **이미지**: JPEG, PNG, GIF, BMP, WebP, TIFF — 해상도 및 메타데이터
- **동영상**: MP4, MKV, AVI, MOV, WEBM — 재생 컨트롤
- **오디오**: MP3, AAC, M4A — 아티스트, 앨범, 재생 시간 정보
- **텍스트 & 코드**: 30개 이상 확장자 — 구문 강조 표시
- **PDF**: 첫 페이지 미리보기
- **폰트**: 글리프 샘플 + 메타데이터
- **Hex 바이너리**: 개발자를 위한 원시 바이트 뷰
- **폴더**: 크기, 항목 수, 생성 날짜

### 키보드 우선 설계

키보드에서 손을 떼지 않는 사용자를 위한 30개 이상의 단축키:

| 단축키 | 동작 |
|----------|--------|
| 방향키 | 컬럼 및 항목 탐색 |
| Enter | 폴더 열기 또는 파일 실행 |
| Space | 미리보기 패널 토글 |
| Ctrl+L / Alt+D | 주소 표시줄 편집 |
| Ctrl+F | 검색 |
| Ctrl+C / X / V | 복사 / 잘라내기 / 붙여넣기 |
| Ctrl+Z / Y | 실행 취소 / 다시 실행 |
| Ctrl+Shift+N | 새 폴더 |
| F2 | 이름 변경 (다중 선택 시 일괄 변경) |
| Ctrl+T / W | 새 탭 / 탭 닫기 |
| Ctrl+1-4 | 뷰 모드 전환 |
| Ctrl+Shift+E | 분할 뷰 토글 |
| Delete | 휴지통으로 이동 |

### 테마 & 커스터마이징

![테마 & 커스터마이징 설정](6.jpg)

- **10가지 테마**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6단계 행 높이** 및 **6단계 폰트/아이콘 크기** — 독립 제어
- **10종 폰트**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Code 등 — CJK 대체 폰트 체인
- **3종 아이콘 팩**: Remix Icon, Phosphor Icons, Tabler Icons
- **9개 언어**: 한국어, English, 日本語, 中文(简体/繁體), Deutsch, Español, Français, Português

### 개발자 도구

![Hex 바이너리 뷰어](4.jpg)

- **Git 상태 뱃지**: 파일별 Modified, Added, Deleted, Untracked
- **Hex 덤프 뷰어**: 첫 512바이트를 16진수 + ASCII로 표시
- **터미널 연동**: Ctrl+`로 현재 경로에서 터미널 실행
- **원격 접속**: FTP/FTPS/SFTP — 암호화된 자격 증명 저장

### 클라우드 스토리지 연동

- **동기화 상태 뱃지**: 클라우드 전용, 동기화 완료, 업로드 대기, 동기화 중
- **OneDrive, iCloud, Dropbox** 자동 감지
- **스마트 썸네일**: 캐시된 미리보기 사용 — 불필요한 다운로드 방지

### 스마트 검색

- **구조화된 쿼리**: `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **자동 완성**: 아무 컬럼에서 입력을 시작하면 즉시 필터링
- **백그라운드 처리**: 검색이 UI를 멈추지 않음

---

## 성능

속도를 위해 설계되었습니다. 폴더당 14,000개 이상 항목으로 테스트 완료.

- 비동기 I/O — UI 스레드를 차단하지 않음
- 최소 오버헤드로 배치 속성 업데이트
- 빠른 탐색 시 중복 작업 방지를 위한 디바운스 선택
- 탭별 캐싱 — 즉각적인 탭 전환, 재렌더링 없음
- SemaphoreSlim 스로틀링을 통한 동시 썸네일 로딩

---

## 시스템 요구사항

| | |
|---|---|
| **OS** | Windows 10 버전 1903 이상 / Windows 11 |
| **아키텍처** | x64, ARM64 |
| **런타임** | Windows App SDK 1.8 (.NET 8) |
| **권장** | Mica 배경을 위해 Windows 11 |

---

## 소스에서 빌드

```bash
# 사전 조건: Visual Studio 2022 + .NET Desktop + WinUI 3 워크로드

# 클론
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# 빌드
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# 단위 테스트 실행
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **참고**: WinUI 3 앱은 `dotnet run`으로 실행할 수 없습니다. **Visual Studio F5** (MSIX 패키징 필요)를 사용하세요.

---

## 기여하기

빌드 설정, 코딩 규칙, PR 가이드라인은 [CONTRIBUTING.md](../CONTRIBUTING.md)를 참조하세요.

---

## 라이선스

이 프로젝트는 [GNU General Public License v3.0](../LICENSE)에 따라 라이선스됩니다.

**Microsoft Store 예외**: 저작권자(LumiBear Studio)는 Microsoft Store 약관에 따라 공식 바이너리를 배포할 수 있으며, 해당 약관은 GPL v3 제7조에 따른 "추가 제한"으로 간주되지 않습니다. 이 예외는 공식 배포에만 적용되며 서드파티 포크에는 적용되지 않습니다.

**상표**: "SPAN Finder" 이름과 공식 로고는 LumiBear Studio의 상표입니다. 포크는 다른 이름과 로고를 사용해야 합니다. 전체 상표 정책은 [LICENSE.md](../LICENSE.md)를 참조하세요.

---

## 링크

- [Microsoft Store](https://www.microsoft.com/store/apps/9P42MFRMH07X)
- [개인정보처리방침](../github-docs/PRIVACY.md)
- [오픈소스 라이선스](../OpenSourceLicenses.md)
- [버그 제보 & 기능 요청](https://github.com/LumiBearStudio/SpanFinder/issues)
