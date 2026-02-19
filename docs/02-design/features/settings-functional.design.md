# Design: Settings Functional Implementation

## Feature: `settings-functional`
## Created: 2026-02-17

---

## 1. Architecture Overview

```
SettingsDialog.xaml ──(x:Name)──> SettingsDialog.xaml.cs
                                       │
                                  Load/Save via
                                       │
                                       ▼
                              SettingsService (Singleton)
                              ├─ LocalSettings persistence
                              ├─ SettingsChanged event
                              └─ Default values
                                       │
                           ┌───────────┼───────────┐
                           ▼           ▼           ▼
                    FileSystemService  MainWindow  ExplorerViewModel
                    (ShowHidden)       (Theme)     (ClickBehavior)
```

## 2. SettingsService Design

### Keys & Defaults
| Key | Type | Default | Section |
|-----|------|---------|---------|
| `Theme` | string | `"system"` | Appearance |
| `Density` | string | `"comfortable"` | Appearance |
| `FontFamily` | string | `"Segoe UI Variable"` | Appearance |
| `ShowHiddenFiles` | bool | `false` | Browsing |
| `ShowFileExtensions` | bool | `true` | Browsing |
| `ShowCheckboxes` | bool | `false` | Browsing |
| `MillerClickBehavior` | string | `"single"` | Browsing |
| `ShowThumbnails` | bool | `true` | Browsing |
| `EnableQuickLook` | bool | `true` | Browsing |
| `ConfirmDelete` | bool | `true` | Browsing |
| `UndoHistorySize` | int | `50` | Browsing |
| `DefaultTerminal` | string | `"wt"` | Tools |
| `ShowContextMenu` | bool | `true` | Tools |
| `StartupBehavior` | int | `0` | General |
| `LastSessionPath` | string | `""` | General |
| `Language` | string | `"system"` | General |

### API
```csharp
public class SettingsService
{
    public event Action<string, object?>? SettingChanged;

    public T Get<T>(string key, T defaultValue);
    public void Set<T>(string key, T value);

    // Typed accessors (convenience)
    public string Theme { get; set; }
    public bool ShowHiddenFiles { get; set; }
    public bool ConfirmDelete { get; set; }
    // ... etc
}
```

## 3. XAML Binding Strategy

코드비하인드에서 직접 컨트롤 값을 읽고 쓰는 방식. ViewModel 없이 간단하게:

```csharp
// Dialog 열릴 때: SettingsService → UI Controls
ShowHiddenToggle.IsOn = _settings.ShowHiddenFiles;
ThemeSystem.IsChecked = _settings.Theme == "system";

// UI 변경 시: UI Controls → SettingsService
ShowHiddenToggle.Toggled += (s, e) => _settings.ShowHiddenFiles = ShowHiddenToggle.IsOn;
ThemeSystem.Checked += (s, e) => _settings.Theme = "system";
```

## 4. Consumer Integration

### FileSystemService — ShowHiddenFiles
```csharp
// 기존: if ((d.Attributes & FileAttributes.Hidden) != 0) continue;
// 변경: if (!_settings.ShowHiddenFiles && (d.Attributes & FileAttributes.Hidden) != 0) continue;
```

### MainWindow — Theme
```csharp
// SettingsChanged event handler
private void ApplyTheme(string theme)
{
    var root = Content as FrameworkElement;
    root.RequestedTheme = theme switch {
        "light" => ElementTheme.Light,
        "dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
```

### MainWindow — ConfirmDelete
```csharp
// HandleDelete에서:
if (_settings.ConfirmDelete) { /* show dialog */ }
else { /* direct delete */ }
```

## 5. Implementation Phases

### Phase 1: SettingsService
- New: `Services/SettingsService.cs`
- Modified: `App.xaml.cs` (DI registration)

### Phase 2: XAML x:Name binding
- Modified: `Views/SettingsDialog.xaml` (add x:Name to all controls)
- Modified: `Views/SettingsDialog.xaml.cs` (load/save logic)

### Phase 3: Consumer integration
- Modified: `Services/FileSystemService.cs` (ShowHidden)
- Modified: `MainWindow.xaml.cs` (Theme, ConfirmDelete, DefaultTerminal)

### Phase 4: Build verification
- 0 errors, 0 XAML warnings
