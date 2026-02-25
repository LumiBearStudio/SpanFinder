# Git Integration Plan (Preview Panel + Details View)

**작성일**: 2026-02-25 (v2 — 미리보기 패널 중심으로 전면 개편)
**작성자**: Agent Team + 사용자 피드백 반영
**목표**: Span 탐색 속도 100% 보장하면서 Git 정보를 자연스럽게 통합

---

## 1. 핵심 철학: "탐색 속도 Zero Impact"

### 기존 Plan (v1) 문제점
- Miller Column 모든 아이템에 뱃지 주입 → `ContainerContentChanging`마다 git 조회
- 스크롤할 때마다 git 로직 개입 → 탐색 속도 저하 리스크
- 작은 뱃지(M/A/D)로는 정보 밀도가 낮음

### 새 접근법 (v2): 미리보기 패널 중심
```
Miller Column 스크롤 / 방향키 이동 → Git 로직 개입 0%
파일 선택 → 기존 미리보기 + Git 마지막 커밋 1줄 (비동기)
폴더 선택 (Git 레포) → 미리보기 패널이 Git 대시보드로 전환
Details 뷰 → 선택적 Git 상태 컬럼 (폴더 단위 1회 실행)
```

---

## 2. 기능 설계: 3-Tier Git 정보 표시

### Tier 1: 미리보기 패널 — 파일 선택 시 (git log 1줄)

파일의 기존 미리보기(이미지/텍스트/PDF 등) 아래에 Git 이력 1줄 추가:

```
┌─────────────────────────────┐
│  [기존 미리보기 콘텐츠]       │
│  (이미지/텍스트/PDF/...)     │
│                             │
│  ─────────────────────────  │
│  종류    C# Source          │
│  크기    12.4 KB            │
│  수정일  2026-02-25 14:30   │
│                             │
│  ─────────── Git ────────── │
│  🌱 3일 전 (Fix login bug)  │
│     by 김개발               │
└─────────────────────────────┘
```

**명령**: `git log -1 --format="%cr|%s|%an" -- <파일 상대경로>`
- 실행 시간: ~20ms (단일 파일, 매우 빠름)
- 기존 200ms 디바운스 + CancellationToken 그대로 활용

### Tier 2: 미리보기 패널 — Git 레포 폴더 선택 시 (대시보드)

폴더가 Git 레포의 루트이거나 내부 폴더일 때, 미리보기 패널 전체를 Git 대시보드로 전환:

```
┌─────────────────────────────┐
│  📁 Span_Project            │
│  ─────────────────────────  │
│                             │
│  🌿 브랜치: main            │
│  ✨ 3개 수정됨, 1개 미추적   │
│                             │
│  ─── 최근 커밋 ───────────  │
│  • 8f3a2b1 - (1시간 전)     │
│    Miller Columns 성능 최적화│
│  • 2c9e4d5 - (어제)         │
│    미리보기 패널 디바운싱 추가 │
│  • 5a1b3c2 - (어제)         │
│    README 업데이트           │
│                             │
│  ─── 변경 파일 ───────────  │
│  M  src/Services/Preview.cs │
│  M  src/ViewModels/Main.cs  │
│  A  src/Models/GitState.cs  │
│  ?  temp/draft.txt          │
│                             │
│  ─────────────────────────  │
│  항목 수: 23개              │
│  수정일  2026-02-25 14:30   │
└─────────────────────────────┘
```

**명령**:
- `git -C {repoRoot} status -sb --no-optional-locks` → 브랜치 + 상태 요약
- `git -C {repoRoot} log -5 --oneline --no-optional-locks` → 최근 커밋 5개
- 두 명령 병렬 실행, 합계 ~50ms

### Tier 3: Details 뷰 — Git 상태 컬럼 (선택적)

Details 뷰에서 기존 컬럼(Name/Date/Type/Size) 옆에 Git 상태 컬럼 추가:

```
 Icon │ Name              │ Date Modified │ Type    │ Size  │ Git
 ─────┼───────────────────┼───────────────┼─────────┼───────┼─────
 📄   │ MainWindow.cs     │ 2시간 전       │ C#      │ 45KB  │  M
 📄   │ App.xaml.cs       │ 어제          │ C#      │ 12KB  │  M
 📁   │ Models            │ 3일 전         │ 폴더    │       │  M
 📄   │ NewFile.cs        │ 방금          │ C#      │ 1KB   │  ?
 📄   │ README.md         │ 1주 전         │ MD      │ 3KB   │
```

**명령**: `git -C {repoRoot} status --porcelain=v1 -z --no-optional-locks`
- 폴더 진입 시 1회 실행 → 결과를 Dictionary로 캐시
- Details 뷰 아이템에 상태 매핑 (ContainerContentChanging에서 주입)
- Git 상태 컬럼 기본 숨김, 헤더 우클릭 메뉴에서 활성화

---

## 3. 핵심 설계 결정

### 3.1 git.exe CLI 사용 (변경 없음)

NuGet 의존성 0, 네이티브 성능, 사용자 시스템의 git 활용.

### 3.2 Settings — 독립 "개발자" 섹션

```
[설정]
├── 외관
├── 탐색
├── 도구
└── 개발자                     ← 독립 섹션 (항상 표시)
    ├── 개발자 컨텍스트 메뉴     [토글]  (기존 ShowDeveloperMenu 이동)
    └── Git 통합                [토글]  (신규 ShowGitIntegration)
        └── git 미설치 시 Disabled + "Git이 설치되어 있지 않습니다"
```

**2중 게이트**: `ShowGitIntegration == true` + `git.exe 감지됨`

### 3.3 IDeveloperSettings 인터페이스 신규

```csharp
public interface IDeveloperSettings
{
    bool ShowDeveloperMenu { get; set; }    // 기존 (도구 → 개발자로 이동)
    bool ShowGitIntegration { get; set; }   // 신규
}
```

---

## 4. 아키텍처 설계

### 4.1 신규/수정 파일 구조

```
Models/
  └── GitFileState.cs              # Git 상태 열거형 (M/A/D/R/U/?/!)
  └── GitRepoInfo.cs               # 레포 정보 (브랜치, 상태 요약, 최근 커밋)

Services/
  └── GitStatusService.cs          # git.exe 실행, 파싱, 캐시

ViewModels/
  └── PreviewPanelViewModel.cs     # Git 정보 속성 추가 (Tier 1 + 2)
  └── FolderViewModel.cs           # Git 레포 감지 + Details 뷰 상태 캐시 (Tier 3)

Views/
  └── PreviewPanelView.xaml        # Git 섹션 UI 추가
  └── DetailsModeView.xaml         # Git 컬럼 추가
```

### 4.2 GitFileState 열거형

```csharp
public enum GitFileState
{
    None,           // Git 레포가 아님
    Clean,          // 추적 중, 변경 없음
    Modified,       // M  — 수정됨
    Added,          // A  — 스테이징 추가됨
    Deleted,        // D  — 삭제됨
    Renamed,        // R  — 이름 변경됨
    Untracked,      // ?  — 미추적
    Conflicted,     // U  — 병합 충돌
    Ignored,        // !  — 무시됨
}
```

### 4.3 GitRepoInfo 모델 (Tier 2 대시보드용)

```csharp
public record GitRepoInfo
{
    public string Branch { get; init; }            // "main", "feature/xyz"
    public int ModifiedCount { get; init; }
    public int UntrackedCount { get; init; }
    public int StagedCount { get; init; }
    public List<GitCommitSummary> RecentCommits { get; init; }
    public List<GitChangedFile> ChangedFiles { get; init; }
}

public record GitCommitSummary(string Hash, string RelativeTime, string Subject);
public record GitChangedFile(string Path, GitFileState State);
```

### 4.4 GitStatusService

```csharp
public class GitStatusService : IDisposable
{
    // ── 감지 ──
    private string? _gitExePath;
    public bool IsAvailable { get; }
    public string? GitVersion { get; }

    // ── 캐시 ──
    private readonly ConcurrentDictionary<string, GitRepoCache> _repoCache;

    // ── Tier 1: 파일 마지막 커밋 ──
    Task<GitLastCommit?> GetLastCommitAsync(string filePath, CancellationToken ct);
    // → git log -1 --format="%cr|%s|%an" -- <path>

    // ── Tier 2: 레포 대시보드 ──
    Task<GitRepoInfo?> GetRepoInfoAsync(string repoRoot, CancellationToken ct);
    // → git status -sb + git log -5 --oneline (병렬)

    // ── Tier 3: 폴더 파일 상태 (Details 뷰) ──
    Task<Dictionary<string, GitFileState>?> GetFolderStatesAsync(
        string repoRoot, CancellationToken ct);
    // → git status --porcelain=v1 -z

    // ── 유틸 ──
    string? FindRepoRoot(string path);             // .git 역방향 탐색
    bool IsNetworkOrRemovable(string path);         // 네트워크/이동식 체크
    void Dispose();
}
```

### 4.5 PreviewPanelViewModel 확장

```csharp
// ── Git 정보 (Tier 1: 파일 선택 시) ──
[ObservableProperty] private string _gitLastCommitInfo = "";
[ObservableProperty] private bool _hasGitInfo;

// ── Git 레포 대시보드 (Tier 2: 폴더 선택 시) ──
[ObservableProperty] private string _gitBranch = "";
[ObservableProperty] private string _gitStatusSummary = "";
[ObservableProperty] private string _gitRecentCommits = "";
[ObservableProperty] private string _gitChangedFiles = "";
[ObservableProperty] private bool _isGitRepo;

// ── Computed ──
public bool IsGitSectionVisible => HasGitInfo;
public bool IsGitDashboardVisible => IsGitRepo && IsFolderVisible;
```

### 4.6 UpdatePreviewAsync 흐름 확장

```
기존 흐름:
  SetBasicInfo → GetPreviewType → Load content (Image/Text/PDF...)

Git 확장 흐름:
  SetBasicInfo → GetPreviewType → Load content
                                → LoadGitInfoAsync (비동기, 병렬)

파일 선택 시:
  1. 기존 미리보기 로딩 (변경 없음)
  2. 동시에 git log -1 실행 (20ms)
  3. 결과 도착 → GitLastCommitInfo 업데이트 → UI 반영

폴더 선택 시 (Git 레포):
  1. 기존 폴더 정보 (항목 수)
  2. 동시에 git status + git log 실행 (50ms)
  3. 결과 도착 → 대시보드 속성 업데이트 → UI 반영
```

---

## 5. PreviewPanelView XAML 확장

### 5.1 Tier 1: 파일 Git 정보 섹션

```xml
<!-- 기존 Metadata Section 아래에 추가 -->
<!-- Git Info (파일 선택 시, git log 결과) -->
<StackPanel Visibility="{Binding IsGitSectionVisible, Converter={StaticResource VisConv}}"
            Spacing="4" Margin="0,4,0,0">
    <Rectangle Height="1" Fill="{ThemeResource SpanBorderSubtleBrush}"/>
    <TextBlock Text="Git" FontSize="11" FontWeight="SemiBold"
               Foreground="{ThemeResource SpanTextTertiaryBrush}"/>
    <TextBlock Text="{Binding GitLastCommitInfo}"
               FontSize="12" TextWrapping="Wrap" LineHeight="18"
               Foreground="{ThemeResource SpanTextSecondaryBrush}"/>
</StackPanel>
```

### 5.2 Tier 2: 폴더 Git 대시보드

```xml
<!-- Folder Info 아래에 추가 (Git 레포 폴더일 때만 표시) -->
<StackPanel Visibility="{Binding IsGitDashboardVisible, Converter={StaticResource VisConv}}"
            Spacing="8">

    <!-- 브랜치 + 상태 요약 -->
    <StackPanel Background="{ThemeResource SpanBgLayer1Brush}"
                CornerRadius="4" Padding="12" Spacing="4">
        <TextBlock FontSize="13" FontWeight="SemiBold">
            <Run Text="🌿 "/>
            <Run Text="{Binding GitBranch}"/>
        </TextBlock>
        <TextBlock Text="{Binding GitStatusSummary}" FontSize="12"
                   Foreground="{ThemeResource SpanTextSecondaryBrush}"/>
    </StackPanel>

    <!-- 최근 커밋 -->
    <StackPanel Background="{ThemeResource SpanBgLayer1Brush}"
                CornerRadius="4" Padding="12" Spacing="2">
        <TextBlock Text="최근 커밋" FontSize="11" FontWeight="SemiBold"
                   Foreground="{ThemeResource SpanTextTertiaryBrush}" Margin="0,0,0,4"/>
        <TextBlock Text="{Binding GitRecentCommits}" FontFamily="Consolas"
                   FontSize="11" LineHeight="18" TextWrapping="Wrap"
                   Foreground="{ThemeResource SpanTextSecondaryBrush}"/>
    </StackPanel>

    <!-- 변경 파일 목록 -->
    <StackPanel Background="{ThemeResource SpanBgLayer1Brush}"
                CornerRadius="4" Padding="12" Spacing="2">
        <TextBlock Text="변경 파일" FontSize="11" FontWeight="SemiBold"
                   Foreground="{ThemeResource SpanTextTertiaryBrush}" Margin="0,0,0,4"/>
        <TextBlock Text="{Binding GitChangedFiles}" FontFamily="Consolas"
                   FontSize="11" LineHeight="16" TextWrapping="NoWrap"
                   Foreground="{ThemeResource SpanTextSecondaryBrush}"/>
    </StackPanel>
</StackPanel>
```

---

## 6. Details 뷰 Git 컬럼 (Tier 3)

### 6.1 헤더 확장

```xml
<!-- 기존 Size 컬럼 뒤에 추가 -->
<ColumnDefinition Width="Auto"/>  <!-- Splitter -->
<ColumnDefinition x:Name="GitColumnDef" Width="50" MinWidth="40"/>

<!-- Git Header -->
<Button Content="Git" Click="OnHeaderClick" Tag="Git" .../>
```

### 6.2 아이템 템플릿 확장

```xml
<!-- Size Cell 뒤에 추가 -->
<Border Grid.Column="9" x:Name="GitCell" Width="50">
    <TextBlock Text="{x:Bind GitStatusText, Mode=OneWay}"
               FontFamily="Consolas" FontSize="11" FontWeight="Bold"
               Foreground="{x:Bind GitStatusBrush, Mode=OneWay}"
               VerticalAlignment="Center" Padding="6,0"/>
</Border>
```

### 6.3 상태 표시 컨벤션

| GitFileState | Text | Color |
|-------------|------|-------|
| Modified | M | #E2A52E (주황) |
| Added | A | #73C991 (초록) |
| Deleted | D | #F44747 (빨강) |
| Renamed | R | #C586C0 (보라) |
| Untracked | ? | #73C991 (초록) |
| Conflicted | ! | #FF0000 (빨강진) |
| Clean | — | (빈 문자열) |
| None | — | (빈 문자열) |

### 6.4 FileSystemViewModel Git 속성 (Tier 3용)

```csharp
// Details 뷰 컬럼용 (간략 표시)
[ObservableProperty] private GitFileState _gitState = GitFileState.None;

public string GitStatusText => _gitState switch
{
    GitFileState.Modified => "M",
    GitFileState.Added => "A",
    GitFileState.Deleted => "D",
    GitFileState.Renamed => "R",
    GitFileState.Untracked => "?",
    GitFileState.Conflicted => "!",
    _ => ""
};

public Brush GitStatusBrush => _gitState switch { ... };
```

### 6.5 Git 컬럼 표시/숨김

- `ShowGitIntegration == false` → Git 컬럼 완전 숨김 (Width=0)
- `ShowGitIntegration == true` + Git 레포 아님 → 컬럼 빈 상태
- `ShowGitIntegration == true` + Git 레포 → 상태 표시
- 헤더 우클릭 메뉴에서도 토글 가능 (기존 Date/Type/Size 숨기기와 동일)

---

## 7. 성능 분석

### 각 Tier별 비용

| Tier | 트리거 | git 명령 | 예상 시간 | 빈도 |
|------|--------|---------|----------|------|
| Tier 1 | 파일 선택 | `git log -1` | ~20ms | 파일 선택마다 (디바운스 200ms) |
| Tier 2 | 폴더 선택 (Git 레포) | `status -sb` + `log -5` | ~50ms | 폴더 선택마다 (디바운스 200ms) |
| Tier 3 | 폴더 진입 (Details 뷰) | `status --porcelain` | ~100ms | 폴더 변경마다 1회 |

### Miller Column 탐색 영향: **ZERO**

```
방향키 ↑↓ 이동 → SelectionChanged → 미리보기 디바운스 200ms → git 명령
→ Miller Column 스크롤과 완전 독립, 절대 차단하지 않음

방향키 ←→ 컬럼 이동 → 새 폴더 로딩 → git 명령은 미리보기 패널에서만
→ 컬럼 로딩 자체에 git 로직 개입 0%
```

### 기존 인프라 100% 재사용

```
PreviewPanelViewModel.OnSelectionChanged()
  → 200ms 디바운스 (Timer)
  → CancellationTokenSource 교체 (이전 git 프로세스 자동 취소)
  → UpdatePreviewAsync() 내에서 기존 미리보기 + Git 병렬 로딩
  → 선택이 빠르게 바뀌면 git 프로세스 자동 Kill
```

---

## 8. GitStatusService 상세

### 8.1 git.exe 감지

```csharp
// 시작 시 1회만 실행
private static string? DetectGitExe()
{
    // 1. PATH에서 검색
    var result = RunProcess("where", "git", timeoutMs: 3000);
    if (result.ExitCode == 0)
        return result.StdOut.Split('\n')[0].Trim();

    // 2. 기본 설치 경로 폴백
    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
    var defaultPath = Path.Combine(programFiles, "Git", "cmd", "git.exe");
    if (File.Exists(defaultPath)) return defaultPath;

    return null;
}
```

### 8.2 레포 루트 탐색

```csharp
public string? FindRepoRoot(string path)
{
    // 캐시 확인
    var dir = Path.GetDirectoryName(path) ?? path;
    for (int i = 0; i < 15; i++)  // 최대 15레벨
    {
        if (Directory.Exists(Path.Combine(dir, ".git")))
            return dir;
        var parent = Path.GetDirectoryName(dir);
        if (parent == null || parent == dir) break;
        dir = parent;
    }
    return null;
}
```

### 8.3 git status 파싱 (Tier 3)

```csharp
// --porcelain=v1 -z 출력 파싱
// "XY path\0" 또는 "XY old\0new\0" (rename)
private Dictionary<string, GitFileState> ParsePorcelainOutput(string output)
{
    var result = new Dictionary<string, GitFileState>(StringComparer.OrdinalIgnoreCase);
    var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    for (int i = 0; i < entries.Length; i++)
    {
        var entry = entries[i];
        if (entry.Length < 4) continue;  // "XY " + path

        char x = entry[0], y = entry[1];
        var path = entry[3..];
        var state = ParseXY(x, y);
        result[path] = state;

        // Rename: 다음 항목은 원래 경로 → 건너뜀
        if (x == 'R' || x == 'C') i++;
    }
    return result;
}
```

### 8.4 프로세스 실행 유틸리티

```csharp
private async Task<ProcessResult> RunGitAsync(
    string repoRoot, string arguments, CancellationToken ct, int timeoutMs = 8000)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = _gitExePath!,
        Arguments = $"-C \"{repoRoot}\" {arguments}",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
    };

    process.Start();

    // 타임아웃 + 취소 통합
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(timeoutMs);

    try
    {
        var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);
        return new(process.ExitCode, stdout, "");
    }
    catch (OperationCanceledException)
    {
        try { process.Kill(entireProcessTree: true); } catch { }
        throw;
    }
}
```

---

## 9. 안전장치

### Layer 1: 게이트 체크
```
✗ ShowGitIntegration == false (기본값) → 전체 비활성
✗ git.exe 미감지 → 전체 비활성
✗ 네트워크 경로 (\\server\) → 해당 경로 스킵
✗ 이동식 드라이브 → 해당 경로 스킵
```

### Layer 2: 실행 안전
```
- git 명령 타임아웃 8초 → 초과 시 Process.Kill
- CancellationToken 연동 → 선택 변경 시 즉시 취소
- 200ms 디바운스 → 빠른 탐색 시 불필요한 프로세스 생성 차단
- stdout 128KB 제한 → 초과 분 절사
```

### Layer 3: 대형 레포 방어
```
- .git/index > 5MB → Tier 3 (Details 컬럼) 건너뜀
- Tier 1/2는 단일 파일/요약이므로 대형 레포에서도 빠름
- git log -1 은 인덱스 크기와 무관 (~20ms)
```

### Layer 4: Fail-safe
```
- 모든 git 실패 → 해당 Tier만 빈 상태 (크래시 없음)
- Git 섹션이 보이지 않을 뿐, 기존 미리보기 기능에 영향 0
```

---

## 10. 구현 계획

### Phase 1: 기반 서비스 (P0)

#### Step 1-1: 모델
- `Models/GitFileState.cs` — 열거형 + 색상/텍스트 매핑
- `Models/GitRepoInfo.cs` — 대시보드용 레코드

#### Step 1-2: GitStatusService
- git.exe 감지
- `RunGitAsync()` 유틸리티
- `FindRepoRoot()` — .git 역방향 탐색
- `GetLastCommitAsync()` — Tier 1
- `GetRepoInfoAsync()` — Tier 2
- `GetFolderStatesAsync()` — Tier 3

#### Step 1-3: Settings 확장
- `IDeveloperSettings` 인터페이스 신규
- `ShowDeveloperMenu` 이동 + `ShowGitIntegration` 추가
- DI 등록

### Phase 2: 미리보기 패널 통합 (P0)

#### Step 2-1: PreviewPanelViewModel 확장
- Git 관련 속성 추가
- `UpdatePreviewAsync()` 내 Git 로딩 병렬 추가

#### Step 2-2: PreviewPanelView.xaml 확장
- Tier 1: 파일 Git 정보 섹션
- Tier 2: 폴더 Git 대시보드

### Phase 3: Details 뷰 + Settings UI (P1)

#### Step 3-1: Details 뷰 Git 컬럼
- 헤더에 Git 컬럼 추가
- 아이템 템플릿에 Git 상태 셀 추가
- FolderViewModel에서 상태 캐시 + 주입

#### Step 3-2: Settings 개발자 섹션 UI
- "개발자" 섹션 신규
- ShowDeveloperMenu 토글 이동
- ShowGitIntegration 토글 + Git 버전 표시

---

## 11. 수정 파일 요약

| 파일 | 작업 | Phase |
|------|------|-------|
| `Models/GitFileState.cs` | **신규** — 상태 열거형 + 매핑 | 1 |
| `Models/GitRepoInfo.cs` | **신규** — 레포 대시보드 모델 | 1 |
| `Services/GitStatusService.cs` | **신규** — git.exe 실행/파싱/캐시 | 1 |
| `Services/ISettingsService.cs` | **수정** — IDeveloperSettings 추가 | 1 |
| `Services/SettingsService.cs` | **수정** — ShowGitIntegration 추가 | 1 |
| `App.xaml.cs` | **수정** — GitStatusService DI 등록 | 1 |
| `ViewModels/PreviewPanelViewModel.cs` | **수정** — Git 속성 + 로딩 로직 | 2 |
| `Views/PreviewPanelView.xaml` | **수정** — Git 섹션 UI | 2 |
| `Views/DetailsModeView.xaml` | **수정** — Git 컬럼 추가 | 3 |
| `Views/DetailsModeView.xaml.cs` | **수정** — Git 상태 주입 | 3 |
| `ViewModels/FileSystemViewModel.cs` | **수정** — GitState 속성 | 3 |
| `ViewModels/FolderViewModel.cs` | **수정** — Git 레포 감지 + 캐시 | 3 |
| `Views/SettingsModeView.xaml` | **수정** — 개발자 섹션 UI | 3 |

---

## 12. 검증 체크리스트

### Phase 1 (기반)
- [ ] git.exe 미설치: 토글 Disabled + 안내 메시지
- [ ] ShowGitIntegration=false (기본값): Git 로직 완전 스킵
- [ ] git.exe 감지 성공: 버전 표시
- [ ] 빌드 성공 (git 미설치 환경에서도)

### Phase 2 (미리보기 패널)
- [ ] 파일 선택: 미리보기 아래에 "3일 전 (커밋 메시지) by 작성자" 표시
- [ ] 비-Git 파일 선택: Git 섹션 미표시 (기존 미리보기만)
- [ ] Git 레포 폴더 선택: 브랜치 + 상태 + 최근 커밋 + 변경 파일 대시보드
- [ ] 비-Git 폴더 선택: 기존 폴더 정보만 표시
- [ ] 빠른 방향키 이동 시: 디바운스로 불필요한 git 프로세스 없음
- [ ] 미리보기 패널 닫힌 상태: git 명령 실행 안 함

### Phase 3 (Details + Settings)
- [ ] Details 뷰: Git 컬럼에 M/A/D/?/! 상태 표시
- [ ] Details 뷰: 비-Git 폴더에서 Git 컬럼 빈 상태
- [ ] 헤더 우클릭: Git 컬럼 표시/숨김 토글
- [ ] Settings 개발자 섹션: 두 토글 독립 동작
- [ ] Git 미설치 + Settings: 토글 비활성 + 안내

---

## 13. 리스크 & 완화

| 리스크 | 영향 | 완화 방안 |
|--------|------|----------|
| git.exe 미설치 | 낮 | 감지 실패 → 기능 비활성, 크래시 없음 |
| git 명령 hang (서버 인증 프롬프트 등) | 중 | 8초 타임아웃 + Process.Kill |
| 대형 레포 git status 느림 | 중 | Tier 3만 .git/index 크기 체크, Tier 1/2는 빠름 |
| 빠른 선택 변경 시 프로세스 누적 | 중 | CancellationToken + Process.Kill + 디바운스 |
| .git 디렉토리 없는 bare repo | 낮 | FindRepoRoot 실패 → 기능 비활성 |
| 한글 경로 인코딩 | 낮 | UTF-8 인코딩 명시 |

---

## 14. 향후 확장

- **주소 바 브랜치 표시**: 현재 경로가 Git 레포일 때 브랜치명 표시
- **Stash 인디케이터**: 대시보드에 stash 개수 표시
- **Diff 미리보기**: 파일 선택 시 변경 내용 diff 표시 (Tier 1 확장)
- **Stage/Unstage 액션**: 대시보드에서 파일 스테이징/언스테이징
- **Git Blame**: 미리보기 패널에서 blame 정보 (텍스트 파일 전용)

---

## 15. 핵심 원칙 요약

1. **탐색 속도 Zero Impact**: Miller Column 스크롤/이동에 Git 로직 개입 0%
2. **미리보기 패널 = Git 정보 허브**: 선택된 1개 아이템만 조회 → 최소 비용
3. **기존 인프라 100% 재사용**: 200ms 디바운스, CancellationToken, PreviewService 패턴
4. **2중 게이트**: ShowGitIntegration + git.exe 감지 (ShowDeveloperMenu와 독립)
5. **Fail-safe**: 모든 실패 → Git 섹션만 숨김, 기존 기능에 영향 0
