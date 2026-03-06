# SPAN Finder

**Un explorateur de fichiers Miller Columns ultra-rapide pour Windows, conçu pour les utilisateurs avancés qui refusent les compromis.**

[English](../README.md) | [한국어](README.ko.md) | [日本語](README.ja.md) | [中文(简体)](README.zh-CN.md) | [中文(繁體)](README.zh-TW.md) | [Deutsch](README.de.md) | [Español](README.es.md) | Français | [Português](README.pt.md)

SPAN Finder réinvente la navigation de fichiers sous Windows. Inspiré par l'élégance de la vue en colonnes du macOS Finder et enrichi de fonctionnalités que l'Explorateur Windows n'a jamais eues — multi-onglets, vue scindée, opérations asynchrones et flux de travail au clavier.

> **Pourquoi se contenter de l'Explorateur Windows ?**

---

## Pourquoi SPAN Finder ?

| | Explorateur Windows | SPAN Finder |
|---|---|---|
| **Miller Columns** | Non | Oui — navigation hiérarchique multi-colonnes |
| **Multi-onglets** | Windows 11 uniquement (basique) | Onglets complets avec détachement, duplication, restauration |
| **Vue scindée** | Non | Double panneau avec modes de vue indépendants |
| **Panneau d'aperçu** | Basique | 10+ types — images, vidéo, audio, code, Hex, polices, PDF |
| **Navigation clavier** | Limitée | 30+ raccourcis, recherche prédictive, conception keyboard-first |
| **Renommage par lot** | Non | Regex, préfixe/suffixe, numérotation séquentielle |
| **Annuler/Rétablir** | Limité | Historique complet des opérations (profondeur configurable) |
| **Thèmes personnalisés** | Non | 10 thèmes — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord et plus |
| **Intégration Git** | Non | Branche, statut, commits en un coup d'œil |
| **Connexions distantes** | Non | FTP, FTPS, SFTP avec identifiants sauvegardés |
| **Statut cloud** | Overlay basique | Badges de synchronisation en temps réel (OneDrive, iCloud, Dropbox) |
| **Vitesse de démarrage** | Lent sur les grands répertoires | Chargement asynchrone + annulation — aucun délai |

---

## Fonctionnalités principales

### Miller Columns — Tout voir d'un coup

Naviguez dans les hiérarchies profondes de dossiers sans perdre le contexte. Chaque colonne représente un niveau — cliquez sur un dossier et son contenu apparaît dans la colonne suivante.

### Quatre modes de vue

- **Miller Columns** (Ctrl+1) — Navigation hiérarchique
- **Détails** (Ctrl+2) — Tableau triable
- **Liste** (Ctrl+3) — Mise en page multi-colonnes haute densité
- **Icônes** (Ctrl+4) — Vue grille avec 4 options de taille

### Panneau d'aperçu — Voir avant d'ouvrir

Appuyez sur **Espace** pour Quick Look (style macOS Finder) :

- Images, vidéo, audio, texte/code, PDF, polices, Hex binaire, infos dossier

### Thèmes et personnalisation

- **10 Thèmes** : Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 niveaux de hauteur de ligne** et **6 niveaux de taille police/icône**
- **10 polices** : Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono et plus — avec fallback CJK automatique
- **9 langues** : English, 한국어, 日本語, 中文(简/繁), Deutsch, Español, Français, Português

### Outils développeur

- Badges de statut Git, visionneuse Hex dump, intégration terminal, connexions FTP/SFTP

---

## Configuration requise

| | |
|---|---|
| **Système** | Windows 10 version 1903+ / Windows 11 |
| **Architecture** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |

---

## Compiler depuis les sources

```bash
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

> **Note** : Les applications WinUI 3 ne peuvent pas être lancées via `dotnet run`. Utilisez **Visual Studio F5** (packaging MSIX requis).

---

## Contribuer

Consultez [CONTRIBUTING.md](CONTRIBUTING.md).

## Licence

[GNU General Public License v3.0](LICENSE) (avec exception de distribution Microsoft Store)

Le nom « SPAN Finder » et le logo officiel sont des marques de LumiBear Studio. Voir [LICENSE.md](LICENSE.md).

---

[Microsoft Store](https://www.microsoft.com/store/apps/9P42MFRMH07X) | [GitHub](https://github.com/LumiBearStudio/SpanFinder) | [Signaler un bug](https://github.com/LumiBearStudio/SpanFinder/issues) | [Confidentialité](https://github.com/LumiBearStudio/SpanFinder/blob/main/github-docs/PRIVACY.md)
