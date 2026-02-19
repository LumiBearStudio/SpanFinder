# Design: Explorer UX Polish

## Feature Name
`explorer-ux-polish`

## Created
2026-02-17

## Status
Design

---

## 1. Overview

Span 파일 탐색기의 마지막 미구현 UX 기능 2개를 구현:
1. **Address Bar Autocomplete** - 경로 입력 시 폴더 자동완성
2. **Quick Look (Space Key)** - 파일 선택 후 Space로 빠른 미리보기

---

## 2. Feature A: Address Bar Autocomplete

### Current State
- `AddressBarTextBox` = 일반 TextBox
- Ctrl+L 또는 빈 공간 클릭으로 편집 모드 전환
- Enter로 경로 네비게이션, Esc로 취소
- 자동완성/제안 기능 없음

### Design
- TextBox를 **AutoSuggestBox**로 교체
- TextChanged에서 현재 입력 디렉토리의 하위 폴더 목록을 제안
- 환경 변수 확장 지원 (%APPDATA%, %USERPROFILE% 등)
- QuerySubmitted로 최종 네비게이션

### Implementation

#### XAML 변경 (MainWindow.xaml)
```xml
<!-- 기존 TextBox 제거, AutoSuggestBox로 교체 -->
<AutoSuggestBox x:Name="AddressBarAutoSuggest"
    Grid.Column="0" Visibility="Collapsed"
    VerticalAlignment="Center" Height="28" FontSize="12"
    PlaceholderText="경로 입력..."
    QueryIcon="Forward"
    TextChanged="OnAddressBarTextChanged"
    SuggestionChosen="OnAddressBarSuggestionChosen"
    QuerySubmitted="OnAddressBarQuerySubmitted"
    KeyDown="OnAddressBarKeyDown"
    LostFocus="OnAddressBarLostFocus"/>
```

#### 코드 (MainWindow.xaml.cs)
```csharp
// TextChanged → 하위 폴더 제안
private void OnAddressBarTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
{
    if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
    var text = sender.Text;
    // 환경 변수 확장
    text = Environment.ExpandEnvironmentVariables(text);
    // 디렉토리 구분자 기준으로 부모 경로 추출
    var dir = Path.GetDirectoryName(text) ?? text;
    var prefix = Path.GetFileName(text);
    // 하위 폴더 목록 필터
    var suggestions = GetFolderSuggestions(dir, prefix);
    sender.ItemsSource = suggestions;
}
```

---

## 3. Feature B: Quick Look (Space Key)

### Current State
- Preview Panel은 Ctrl+Shift+P로 사이드바에 표시 (항상 열린 상태)
- Space 키는 type-ahead 검색의 문자로 사용됨
- macOS Finder 스타일의 팝업 Quick Look 미구현

### Design
- Space 키 단독 입력 시 Quick Look 팝업 표시
- ContentDialog 기반의 경량 프리뷰 오버레이
- 이미지/텍스트/PDF/미디어 타입별 렌더링
- Space 다시 누르거나 Esc로 닫기
- 기존 PreviewService 재활용

### Implementation

#### 핵심 로직 (MainWindow.xaml.cs)
```csharp
// OnMillerKeyDown에 Space 핸들러 추가
case Windows.System.VirtualKey.Space:
    if (_settings.EnableQuickLook)
    {
        HandleQuickLook(activeIndex);
        e.Handled = true;
    }
    else
    {
        HandleTypeAhead(e, activeIndex); // 기존 동작
    }
    break;
```

#### Quick Look 팝업
- ContentDialog with custom content
- 최대 800x600 크기, 파일 타입별 내용:
  - Image: Image control
  - Text/Code: ScrollViewer + TextBlock
  - Media: MediaPlayerElement
  - Other: 파일 아이콘 + 메타데이터

---

## 4. Files to Modify

| File | Changes |
|------|---------|
| `MainWindow.xaml` | TextBox → AutoSuggestBox 교체 |
| `MainWindow.xaml.cs` | 자동완성 핸들러, Quick Look 핸들러 |

---

## 5. Implementation Order

```
Phase 1: Address Bar Autocomplete (TextBox → AutoSuggestBox)
    ↓
Phase 2: Quick Look (Space key popup preview)
    ↓
Phase 3: Build verification
```
