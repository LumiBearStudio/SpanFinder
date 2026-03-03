# SPAN Finder

**A blazing-fast Miller Columns file explorer for Windows, built for power users who refuse to compromise.**

English | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder reimagines file navigation on Windows. Inspired by the elegance of macOS Finder's column view and supercharged with features Windows Explorer never had — multi-tab, split view, async operations, and keyboard-driven workflows that make file management feel effortless.

> **Why settle for Windows Explorer when you can fly?**

<!-- Screenshots: Replace these with actual screenshots after building the app -->
<!-- Recommended: 1920x1080 or 1600x900 PNG, saved in docs/images/ -->

![SPAN Finder — Miller Columns + Split View](docs/images/screenshot-main.png)

---

## Why SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | No | Yes — hierarchical multi-column navigation |
| **Multi-Tab** | Windows 11 only (basic) | Full tabs with tear-off, duplication, session restore |
| **Split View** | No | Dual-pane with independent view modes |
| **Preview Panel** | Basic | 10+ file types — images, video, audio, code, hex, fonts, PDF |
| **Keyboard Navigation** | Limited | 30+ shortcuts, type-ahead search, full keyboard-first design |
| **Batch Rename** | No | Regex, prefix/suffix, sequential numbering |
| **Undo/Redo** | Limited | Full operation history (configurable depth) |
| **Custom Themes** | No | 10 themes — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord, and more |
| **Git Integration** | No | Branch, status, commits at a glance |
| **Remote Connections** | No | FTP, FTPS, SFTP with saved credentials |
| **Cloud Status** | Basic overlay | Real-time sync badges (OneDrive, iCloud, Dropbox) |
| **Startup Speed** | Slow on large directories | Async loading with cancellation — zero lag |

---

## Features

### Miller Columns — See Everything at Once

Navigate deep folder hierarchies without losing context. Each column represents one level — click a folder and its contents appear in the next column. You always see where you are and where you came from.

- Draggable column separators for custom widths
- Auto-equalize columns (Ctrl+Shift+=) or auto-fit to content (Ctrl+Shift+-)
- Smooth horizontal scrolling to keep the active column visible

### Four View Modes

- **Miller Columns** (Ctrl+1) — Hierarchical navigation, SPAN Finder's signature
- **Details** (Ctrl+2) — Sortable table with name, date, type, size columns
- **List** (Ctrl+3) — Dense multi-column layout for scanning large directories
- **Icons** (Ctrl+4) — Grid view with 4 size options up to 256x256 thumbnails

### Multi-Tab with Full Session Restore

- Open unlimited tabs, each with its own path, view mode, and history
- **Tab tear-off**: Drag a tab out to create a new window — full state preserved
- **Tab duplication**: Clone a tab with its exact path and settings
- Session auto-save: Close the app, reopen it — every tab exactly where you left it

### Split View — True Dual-Pane

- Side-by-side file browsing with independent navigation per pane
- Each pane can use a different view mode (Miller left, Details right)
- Separate preview panels for each pane
- Drag files between panes for copy/move operations

### Preview Panel — Know Before You Open

Press **Space** for Quick Look (macOS Finder style):

- **Images**: JPEG, PNG, GIF, BMP, WebP, TIFF with resolution and metadata
- **Video**: MP4, MKV, AVI, MOV, WEBM with playback controls
- **Audio**: MP3, AAC, M4A with artist, album, duration info
- **Text & Code**: 30+ extensions with syntax display
- **PDF**: First page preview
- **Fonts**: Glyph samples with metadata
- **Hex Binary**: Raw byte view for developers
- **Folders**: Size, item count, creation date

### Keyboard-First Design

30+ keyboard shortcuts for users who keep their hands on the keyboard:

| Shortcut | Action |
|----------|--------|
| Arrow Keys | Navigate columns and items |
| Enter | Open folder or execute file |
| Space | Toggle preview panel |
| Ctrl+L / Alt+D | Edit address bar |
| Ctrl+F | Search |
| Ctrl+C / X / V | Copy / Cut / Paste |
| Ctrl+Z / Y | Undo / Redo |
| Ctrl+Shift+N | New folder |
| F2 | Rename (batch rename if multi-select) |
| Ctrl+T / W | New tab / Close tab |
| Ctrl+1-4 | Switch view mode |
| Ctrl+Shift+E | Toggle split view |
| Delete | Move to Recycle Bin |

### Themes & Customization

![Themes — Dracula, Tokyo Night, Catppuccin, Nord](docs/images/screenshot-themes.png)

- **10 Themes**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6-Level Row Height** and **6-Level Font/Icon Size** — independent controls
- **3 Icon Packs**: Remix Icon, Phosphor Icons, Tabler Icons
- **9 Languages**: English, Korean, Japanese, Chinese (Simplified/Traditional), German, Spanish, French, Portuguese

### Developer Tools

![Git Badges & Hex Viewer](docs/images/screenshot-dev-tools.png)

- **Git status badges**: Modified, Added, Deleted, Untracked per file
- **Hex dump viewer**: First 512 bytes in hex + ASCII
- **Terminal integration**: Ctrl+` opens terminal at current path
- **Remote connections**: FTP/FTPS/SFTP with encrypted credential storage

### Cloud Storage Integration

- **Sync status badges**: Cloud-only, Synced, Pending Upload, Syncing
- **OneDrive, iCloud, Dropbox** detection out of the box
- **Smart thumbnails**: Uses cached previews — never triggers unwanted downloads

### Smart Search

- **Structured queries**: `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **Type-ahead**: Start typing in any column to filter instantly
- **Background processing**: Search never freezes the UI

---

## Performance

Engineered for speed. Tested with 14,000+ items per folder.

- Async I/O everywhere — nothing blocks the UI thread
- Batch property updates with minimal overhead
- Debounced selection prevents redundant work during rapid navigation
- Per-tab caching — instant tab switching, no re-rendering
- Concurrent thumbnail loading with SemaphoreSlim throttling

---

## System Requirements

| | |
|---|---|
| **OS** | Windows 10 version 1903+ / Windows 11 |
| **Architecture** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |
| **Recommended** | Windows 11 for Mica backdrop |

---

## Build from Source

```bash
# Prerequisites: Visual Studio 2022 with .NET Desktop + WinUI 3 workloads

# Clone
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# Build
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Run unit tests
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **Note**: WinUI 3 apps cannot be launched via `dotnet run`. Use **Visual Studio F5** (MSIX packaging required).

---

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for build setup, coding conventions, and PR guidelines.

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

**Microsoft Store Exception**: The copyright holder (LumiBear Studio) may distribute official binaries through the Microsoft Store under its terms, without those terms being considered "additional restrictions" under GPL v3 Section 7. This exception applies only to the official distribution and does not extend to third-party forks.

**Trademark**: The "SPAN Finder" name and official logo are trademarks of LumiBear Studio. Forks must use a different name and logo. See [LICENSE.md](LICENSE.md) for full trademark policy.

---

## Links

- [Privacy Policy](github-docs/PRIVACY.md)
- [Open Source Licenses](OpenSourceLicenses.md)
- [Bug Reports & Feature Requests](https://github.com/LumiBearStudio/SpanFinder/issues)
