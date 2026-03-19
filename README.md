<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>macOS Finder's Miller Columns, reimagined for Windows.</strong><br>
  For power users who switched to Windows but never stopped missing column view.
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Download from Microsoft Store"></a>
</p>

<p align="center">
  English | <a href="README/README.ko.md">한국어</a> | <a href="README/README.ja.md">日本語</a> | <a href="README/README.zh-CN.md">中文(简体)</a> | <a href="README/README.zh-TW.md">中文(繁體)</a> | <a href="README/README.de.md">Deutsch</a> | <a href="README/README.es.md">Español</a> | <a href="README/README.fr.md">Français</a> | <a href="README/README.pt.md">Português</a>
</p>

---

![SPAN Finder — Miller Columns in Action](README/miller-columns.gif)

> **Navigate folder hierarchies the way they were meant to be navigated.**
> Click a folder, its contents appear in the next column. You always see where you are, where you came from, and where you're going — all at once. No more clicking back and forth.

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

![Details View with Code Preview](README/3.jpg)

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

![Split View with 14K+ Items](README/2.jpg)

### Preview Panel — Know Before You Open

![Code Preview with Git Info](README/5.jpg)

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

![Themes & Customization](README/themes.gif)

- **10 Themes**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6-Level Row Height** and **6-Level Font/Icon Size** — independent controls
- **10 Font Options**: Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Code, and more — with CJK fallback font chain
- **3 Icon Packs**: Remix Icon, Phosphor Icons, Tabler Icons
- **9 Languages**: English, Korean, Japanese, Chinese (Simplified/Traditional), German, Spanish, French, Portuguese

### Developer Tools

![Hex Binary Viewer](README/4.jpg)

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

Found a bug? Have a feature request? [Open an issue](https://github.com/LumiBearStudio/SpanFinder/issues) — all feedback welcome.

See [CONTRIBUTING.md](CONTRIBUTING.md) for build setup, coding conventions, and PR guidelines.

---

## Support the Project

If SPAN Finder makes your file management better, consider:

- **Star this repo** to help others discover it
- **Share** with colleagues who miss macOS Finder on Windows
- **Report bugs** — every issue report makes SPAN Finder more stable
- **[Download from Microsoft Store](https://apps.microsoft.com/detail/9P7NJ351X9TL)** — Store reviews help visibility

---

## License

This project is licensed under the [GNU General Public License v3.0](LICENSE).

**Microsoft Store Exception**: The copyright holder (LumiBear Studio) may distribute official binaries through the Microsoft Store under its terms, without those terms being considered "additional restrictions" under GPL v3 Section 7. This exception applies only to the official distribution and does not extend to third-party forks.

**Trademark**: The "SPAN Finder" name and official logo are trademarks of LumiBear Studio. Forks must use a different name and logo. See [LICENSE.md](LICENSE.md) for full trademark policy.

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="PRIVACY.md">Privacy Policy</a> ·
  <a href="OpenSourceLicenses.md">Open Source Licenses</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">Bug Reports & Feature Requests</a>
</p>
