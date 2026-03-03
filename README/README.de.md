# SPAN Finder

**Ein blitzschneller Miller-Columns-Dateiexplorer für Windows, entwickelt für Power-User, die keine Kompromisse eingehen.**

[English](../README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | Deutsch | [Español](README.es.md) | [Français](README.fr.md) | [Português](README.pt.md)

SPAN Finder erfindet die Dateinavigation unter Windows neu. Inspiriert von der Eleganz der Spaltenansicht des macOS Finder und ausgestattet mit Funktionen, die der Windows Explorer nie hatte — Multi-Tab, geteilte Ansicht, asynchrone Operationen und tastaturgesteuerte Workflows.

> **Warum sich mit dem Windows Explorer zufriedengeben?**

---

## Warum SPAN Finder?

| | Windows Explorer | SPAN Finder |
|---|---|---|
| **Miller Columns** | Nein | Ja — hierarchische Mehrspalten-Navigation |
| **Multi-Tab** | Nur Windows 11 (Basis) | Volle Tabs mit Abreißen, Duplizieren, Sitzungswiederherstellung |
| **Geteilte Ansicht** | Nein | Zwei-Fenster mit unabhängigen Ansichtsmodi |
| **Vorschau-Panel** | Basis | 10+ Dateitypen — Bilder, Video, Audio, Code, Hex, Schriften, PDF |
| **Tastaturnavigation** | Eingeschränkt | 30+ Shortcuts, Vorauseingabe-Suche, Tastatur-First-Design |
| **Batch-Umbenennung** | Nein | Regex, Präfix/Suffix, sequentielle Nummerierung |
| **Rückgängig/Wiederholen** | Eingeschränkt | Vollständige Operationshistorie (konfigurierbare Tiefe) |
| **Benutzerdefinierte Themes** | Nein | 10 Themes — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord u.v.m. |
| **Git-Integration** | Nein | Branch, Status, Commits auf einen Blick |
| **Remote-Verbindungen** | Nein | FTP, FTPS, SFTP mit gespeicherten Zugangsdaten |
| **Cloud-Status** | Basis-Overlay | Echtzeit-Sync-Badges (OneDrive, iCloud, Dropbox) |
| **Startgeschwindigkeit** | Langsam bei großen Verzeichnissen | Asynchrones Laden + Abbruch — keine Verzögerung |

---

## Hauptfunktionen

### Miller Columns — Alles auf einen Blick

Navigieren Sie tiefe Ordnerhierarchien, ohne den Kontext zu verlieren. Jede Spalte repräsentiert eine Ebene — klicken Sie auf einen Ordner und sein Inhalt erscheint in der nächsten Spalte.

### Vier Ansichtsmodi

- **Miller Columns** (Strg+1) — Hierarchische Navigation
- **Details** (Strg+2) — Sortierbare Tabelle
- **Liste** (Strg+3) — Kompaktes Mehrspaltenlayout
- **Symbole** (Strg+4) — Rasteransicht mit 4 Größenoptionen

### Vorschau-Panel — Vor dem Öffnen sehen

**Leertaste** für Quick Look (macOS Finder-Stil):

- Bilder, Video, Audio, Text/Code, PDF, Schriften, Hex-Binär, Ordnerinfos

### Themes & Anpassung

- **10 Themes**: Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6-stufige Zeilenhöhe** & **6-stufige Schrift-/Symbolgröße**
- **9 Sprachen**: English, 한국어, 日本語, 中文(简/繁), Deutsch, Español, Français, Português

### Entwickler-Tools

- Git-Status-Badges, Hex-Dump-Viewer, Terminal-Integration, FTP/SFTP-Verbindungen

---

## Systemanforderungen

| | |
|---|---|
| **Betriebssystem** | Windows 10 Version 1903+ / Windows 11 |
| **Architektur** | x64, ARM64 |
| **Laufzeit** | Windows App SDK 1.8 (.NET 8) |

---

## Aus Quellcode bauen

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **Hinweis**: WinUI 3-Apps können nicht über `dotnet run` gestartet werden. Verwenden Sie **Visual Studio F5** (MSIX-Paketierung erforderlich).

---

## Mitwirken

Siehe [CONTRIBUTING.md](CONTRIBUTING.md).

## Lizenz

[GNU General Public License v3.0](LICENSE) (mit Microsoft Store-Vertriebsausnahme)

„SPAN Finder" und das offizielle Logo sind Marken von LumiBear Studio. Details unter [LICENSE.md](LICENSE.md).
