# File Preview - Plan vs Design Gap Analysis Report

**분석일**: 2026-02-16
**Match Rate**: 85% → 수정 후 93%
**Plan**: `docs/01-plan/features/file-preview.plan.md`
**Design**: `docs/02-design/features/file-preview.design.md`

---

## 주요 갭 (7개)

| # | 갭 | 영향도 | 조치 |
|---|---|:---:|---|
| 1 | `Models/FilePreviewInfo.cs` 누락 (Design에서 인라인) | 낮 | Design 유지 (서비스 내 record로 충분) |
| 2 | Preview 너비 저장 로직 누락 | 중 | Design 수정: SavePreviewWidth() 추가 |
| 3 | MinWidth 200px 미지정 | 중 | Design 수정: MinWidth 추가 |
| 4 | MaxWidth 50% 미지정 | 중 | Design 수정: MaxWidth 로직 추가 |
| 5 | 메타데이터 그룹 구분선 누락 | 낮 | 구현 시 추가 |
| 6 | 레이아웃 과밀 완화 없음 | 중 | Design 수정: 최소 너비 제한으로 해결 |
| 7 | Risk 섹션 없음 | 낮 | Plan 참조로 충분 |

## 버그 발견

- `.svg` 포함 → `BitmapImage`는 SVG 렌더링 불가 → **제거 필요**

## 긍정적 추가 (Design > Plan)

- 텍스트 확장자 30+개 (Plan은 5개)
- 미디어 포맷 6개 추가 (AAC, M4A, M4V, MOV, WMV, WEBM)
- 별도 PdfPreview 프로퍼티
- Details/Icon 모드 선택 추적 상세 설계
- HasContent 빈 상태 관리

## 수정 후 예상 Match Rate: 93%
