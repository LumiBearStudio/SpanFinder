# Plan: Initial Project Setup & Organization (initial-setup)

## 1. Overview
현재 루트에 흩어져 있는 기획 문서와 디자인 프로토타입을 정리하고, 프로젝트의 PDCA 워크플로우를 위한 폴더 구조를 확립하여 본격적인 개발(Phase 1)에 착수할 수 있는 상태를 만드는 것이 목표입니다.

## 2. Goals
- [ ] 프로젝트 핵심 문서(`Project Span.md`, `Setting Span.md`)를 `docs/00-context/`로 이동 및 구조화
- [ ] 디자인 폴더(`design/`) 내의 HTML 프로토타입을 `docs/03-mockup/` 또는 별도 백업 폴더로 정리
- [ ] PDCA 표준 폴더 구조(`docs/01-plan`, `docs/02-design`, `docs/03-analysis`, `docs/04-report`) 확립
- [ ] 프로젝트 루트에 전체 안내를 위한 `README.md` 생성
- [ ] 개발 환경(WinUI 3, .NET 9) 준비 사항 확인

## 3. Tasks
- [ ] **Task 1: 문서 구조화**
  - `docs/00-context/` 폴더 생성
  - `Project Span.md` -> `docs/00-context/requirements.md`
  - `Setting Span.md` -> `docs/00-context/settings-spec.md`
- [ ] **Task 2: 디자인 에셋 정리**
  - 현재 `design/` 폴더의 HTML/CSS/JS는 초기 목업이므로 `docs/03-mockup/prototype-v1/`로 이동 검토
- [ ] **Task 3: PDCA 폴더 구조 생성**
  - `docs/01-plan/features/`
  - `docs/02-design/features/`
  - `docs/03-analysis/`
  - `docs/04-report/`
- [ ] **Task 4: 루트 README.md 작성**
  - 프로젝트 이름, 설명, 기술 스택, 현재 진행 상태 요약
- [ ] **Task 5: .gitignore 설정**
  - `.vs`, `bin`, `obj`, `docs/.pdca-snapshots` 등 제외 설정

## 4. Schedule
- 시작일: 2026-02-11
- 목표 완료일: 2026-02-11

## 5. Resources
- `Project Span.md`: 핵심 요구사항 및 로드맵
- `Setting Span.md`: 설정 UI 기획
- `design/`: HTML/CSS UI 프로토타입
