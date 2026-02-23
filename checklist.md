# Span - 차후 개선 사항 체크리스트

## 우클릭 컨텍스트 메뉴 정리

### Shell 확장 항목 필터링
- [ ] **Give access to** 제거 (네트워크 공유 — Span 사용 목적과 무관)
- [ ] **Restore previous versions** 제거 (컨텍스트 메뉴에서 제거, 별도 기능으로 구현 검토)
- [ ] **Include in library** 제거 (Windows 라이브러리 — 사용 빈도 낮음)
- [ ] **Pin to Start** 제거 (시작 메뉴 고정 — Span과 무관)
- [ ] **Copy as path** 제거 (Span 자체 "경로 복사"와 중복)
- [ ] **Pin to Quick access** 제거 (Span 자체 "즐겨찾기에 추가"와 중복)

### 한국어 번역 누락
- [ ] **Send to** → "보내기"로 한글 표시
- [ ] **속성 대화상자** 한글 윈도우에서 영문 표시되는 문제 조사/수정
- [ ] **Verb 기반 번역 테이블** 구현 — Windows 기본 셸 항목(Send to, Properties 등)을 Span 설정 언어로 오버라이드
  - `GetCommandString(GCS_VERBW)`로 verb 식별 → 한글 번역 딕셔너리 매핑
  - 서드파티 확장(Git, VS 등)은 제어 불가 — 해당 앱 언어 설정에 의존

## 즐겨찾기 기능 개선

### Windows 탐색기 즐겨찾기(Quick Access) 공유
- [ ] 설정 > 일반: "파일 탐색기와 즐겨찾기 공유" ON/OFF 토글 추가 (기본값: ON)
- [ ] ON: Windows Quick Access에서 고정 폴더 읽기 (`Shell.Application` COM, `shell:::{679f85cb-0220-4080-b29b-5540cc05aab6}`)
- [ ] ON: Span에서 즐겨찾기 추가/제거 시 Quick Access에도 반영 (`pintohome`/`unpinfromhome`)
- [ ] ON: Quick Access 변경 감시 (`FileSystemWatcher` — `f01b4d95cf55d32a.automaticDestinations-ms`)
- [ ] OFF: 기존 방식 유지 (Span LocalSettings에 독립 저장)

## F3: 폴더 크기 표시 UI 개선
- [ ] Details 뷰에서 폴더 크기 계산 중 로딩 표시 (예: "..." 또는 스피너)
- [ ] 계산 완료 후 크기 표시 애니메이션/전환 효과
- [ ] 폴더 크기 포맷 일관성 확인 (파일 크기와 동일한 단위/정렬)
- [ ] 대용량 폴더 계산 시간이 긴 경우 사용자 피드백 (진행 상태)
- [ ] 폴더 크기 컬럼 정렬 지원 확인

## 파일 이전 버전 복원
- [ ] "Restore previous versions" 기능을 별도 UI로 구현 검토 (파일 속성 패널 또는 우클릭 하위 메뉴)
- [ ] Windows Volume Shadow Copy / File History API 조사
