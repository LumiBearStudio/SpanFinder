<h1 align="center">
  SPAN Finder
</h1>

<p align="center">
  <strong>Les colonnes Miller de macOS Finder, enfin sur Windows.</strong><br>
  Pour ceux qui ont migré vers Windows mais n'ont jamais pu se passer de la vue en colonnes du Finder.
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://img.shields.io/badge/Microsoft_Store-Download-blue?style=for-the-badge&logo=microsoft" alt="Microsoft Store"></a>
  <a href="https://github.com/LumiBearStudio/SpanFinder/releases/latest"><img src="https://img.shields.io/github/v/release/LumiBearStudio/SpanFinder?style=for-the-badge&label=Latest" alt="Latest Release"></a>
  <a href="../LICENSE"><img src="https://img.shields.io/github/license/LumiBearStudio/SpanFinder?style=for-the-badge" alt="License"></a>
  <a href="https://github.com/sponsors/LumiBearStudio"><img src="https://img.shields.io/badge/Sponsor-%E2%9D%A4-ff69b4?style=for-the-badge&logo=github-sponsors" alt="Sponsor"></a>
</p>

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL"><img src="https://get.microsoft.com/images/en-us%20dark.svg" width="200" alt="Télécharger depuis le Microsoft Store"></a>
</p>

<p align="center">
  <a href="../README.md">English</a> | <a href="README.ko.md">한국어</a> | <a href="README.ja.md">日本語</a> | <a href="README.zh-CN.md">中文(简体)</a> | <a href="README.zh-TW.md">中文(繁體)</a> | <a href="README.de.md">Deutsch</a> | <a href="README.es.md">Español</a> | Français | <a href="README.pt.md">Português</a>
</p>

---

![SPAN Finder — Navigation en colonnes Miller](miller-columns.gif)

> **La navigation de fichiers, comme elle devrait vraiment être.**
> Cliquez sur un dossier et son contenu s'affiche dans la colonne suivante. Où vous êtes, d'où vous venez, où vous allez — tout est visible en un seul écran. Plus besoin d'appuyer sur le bouton retour.

---

## Pourquoi SPAN Finder ?

| | Explorateur Windows | SPAN Finder |
|---|---|---|
| **Colonnes Miller** | Absent | Navigation hiérarchique multi-colonnes |
| **Multi-onglets** | Windows 11 uniquement (basique) | Détachement, duplication et restauration de session complète |
| **Vue scindée** | Absent | Double panneau avec modes de vue indépendants |
| **Panneau d'aperçu** | Basique | 10+ types — images, vidéo, audio, code, Hex, polices, PDF |
| **Navigation clavier** | Limitée | 30+ raccourcis, recherche avec auto-complétion, conception clavier d'abord |
| **Renommage par lot** | Absent | Regex, préfixe/suffixe, numérotation séquentielle |
| **Annuler/Rétablir** | Limité | Historique complet des opérations (profondeur configurable) |
| **Thèmes personnalisés** | Absent | 10 thèmes — Dracula, Tokyo Night, Catppuccin, Gruvbox, Nord, etc. |
| **Intégration Git** | Absent | Branche, statut, commits en un coup d'oeil |
| **Connexions distantes** | Absent | FTP, FTPS, SFTP — identifiants sauvegardés |
| **Espaces de travail** | Absent | Enregistrer et restaurer les dispositions d'onglets |
| **Statut cloud** | Overlay basique | Badges de synchronisation en temps réel (OneDrive, iCloud, Dropbox) |
| **Vitesse de démarrage** | Lent sur les grands répertoires | Chargement asynchrone + annulation — aucun délai |

---

## Fonctionnalités

### Colonnes Miller — Tout voir d'un coup

Naviguez dans des hiérarchies profondes de dossiers sans jamais perdre le contexte. Chaque colonne représente un niveau de dossier : cliquez sur un dossier et son contenu apparaît dans la colonne suivante. Vous savez toujours où vous êtes et quel chemin vous avez parcouru.

- Séparateurs de colonnes redimensionnables par glisser-déposer
- Égalisation des colonnes (Ctrl+Shift+=) ou ajustement au contenu (Ctrl+Shift+-)
- Défilement horizontal fluide pour garder la colonne active toujours visible

### Quatre modes de vue

- **Colonnes Miller** (Ctrl+1) — Navigation hiérarchique, la signature de SPAN Finder
- **Détails** (Ctrl+2) — Tableau triable avec colonnes nom, date, type, taille
- **Liste** (Ctrl+3) — Mise en page dense multi-colonnes pour parcourir les grands dossiers
- **Icônes** (Ctrl+4) — Vue grille avec miniatures jusqu'à 256×256 en 4 niveaux de taille

![Quatre modes de vue](view-modes.gif)

### Multi-onglets + Restauration de session complète

- Onglets illimités — chaque onglet possède son propre chemin, mode de vue et historique de navigation
- **Détachement d'onglet** : glissez un onglet pour créer une nouvelle fenêtre — état entièrement préservé
- **Duplication d'onglet** : dupliquez un onglet avec le chemin et les paramètres exacts
- Sauvegarde automatique de session : fermez et rouvrez l'application — tous vos onglets sont restaurés

### Vue scindée — Un vrai double panneau

- Navigation indépendante gauche-droite
- Modes de vue différents par panneau (Miller à gauche, Détails à droite)
- Panneau d'aperçu individuel pour chaque côté
- Glisser-déposer entre panneaux pour copier/déplacer

![Vue scindée avec plus de 14 000 éléments](2.jpg)

### Panneau d'aperçu — Voir avant d'ouvrir

![Aperçu de code + Informations Git](5.jpg)

Appuyez sur **Espace** pour Quick Look (style macOS Finder) :

- **Images** : JPEG, PNG, GIF, BMP, WebP, TIFF — résolution et métadonnées
- **Vidéo** : MP4, MKV, AVI, MOV, WEBM — contrôles de lecture
- **Audio** : MP3, AAC, M4A — artiste, album, durée
- **Texte & Code** : 30+ extensions — coloration syntaxique
- **PDF** : Aperçu de la première page
- **Polices** : Échantillon de glyphes + métadonnées
- **Hex binaire** : Vue des octets bruts pour les développeurs
- **Dossier** : Taille, nombre d'éléments, date de création
- **Hash de fichier** : Somme de contrôle SHA256 + copie en un clic (à activer dans les paramètres)

### Conception clavier d'abord

Plus de 30 raccourcis pour ceux qui ne quittent jamais le clavier :

| Raccourci | Action |
|----------|--------|
| Touches fléchées | Navigation dans les colonnes et les éléments |
| Entrée | Ouvrir un dossier ou exécuter un fichier |
| Espace | Basculer le panneau d'aperçu |
| Ctrl+L / Alt+D | Modifier la barre d'adresse |
| Ctrl+F | Rechercher |
| Ctrl+C / X / V | Copier / Couper / Coller |
| Ctrl+Z / Y | Annuler / Rétablir |
| Ctrl+Shift+N | Nouveau dossier |
| F2 | Renommer (renommage par lot en sélection multiple) |
| Ctrl+T / W | Nouvel onglet / Fermer l'onglet |
| Ctrl+1-4 | Changer de mode de vue |
| Ctrl+Shift+S | Enregistrer l'espace de travail |
| Ctrl+Shift+W | Ouvrir la palette des espaces de travail |
| Ctrl+Shift+E | Basculer la vue scindée |
| Suppr | Envoyer à la corbeille |

### Thèmes & Personnalisation

![Thèmes & Personnalisation](themes.gif)

- **10 thèmes** : Light, Dark, Dracula, Tokyo Night, Catppuccin, Gruvbox, Solarized, Nord, One Dark, Monokai
- **6 niveaux de hauteur de ligne** et **6 niveaux de taille police/icône** — contrôle indépendant
- **10 polices** : Segoe UI Variable, Consolas, Cascadia Code/Mono, D2Coding, JetBrains Mono, Fira Code, etc. — chaîne de polices de substitution CJK
- **3 packs d'icônes** : Remix Icon, Phosphor Icons, Tabler Icons
- **9 langues** : Français, English, 한국어, 日本語, 中文(简体/繁體), Deutsch, Español, Português

### Outils développeur

![Visionneuse Hex binaire](4.jpg)

- **Badges de statut Git** : Modified, Added, Deleted, Untracked par fichier
- **Visionneuse Hex dump** : Les 512 premiers octets en hexadécimal + ASCII
- **Intégration terminal** : Ctrl+` pour ouvrir un terminal au chemin actuel
- **Connexions distantes** : FTP/FTPS/SFTP — identifiants chiffrés sauvegardés

### Stockage cloud

- **Badges de statut de synchronisation** : Cloud uniquement, Synchronisé, En attente d'envoi, En cours de synchronisation
- **OneDrive, iCloud, Dropbox** détectés automatiquement
- **Miniatures intelligentes** : Utilise les aperçus en cache — évite les téléchargements inutiles

### Recherche intelligente

- **Requêtes structurées** : `type:image`, `size:>100MB`, `date:today`, `ext:.pdf`
- **Auto-complétion** : Commencez à taper dans n'importe quelle colonne pour un filtrage instantané
- **Traitement en arrière-plan** : La recherche ne bloque jamais l'interface

### Espaces de travail — Enregistrer et restaurer les dispositions d'onglets *(v1.2.1.0)*

- **Enregistrer les onglets actuels** : Clic droit sur un onglet → "Enregistrer la disposition..." ou Ctrl+Shift+S
- **Restauration instantanée** : Bouton Espaces de travail dans la barre latérale ou Ctrl+Shift+W
- **Gestion des espaces de travail** : Restaurer, renommer, supprimer depuis le menu
- Idéal pour changer de contexte — "Développement", "Retouche photo", "Organisation de documents"

### Fonctionnalités avancées

- **Collage de fichiers virtuels** : Collez avec Ctrl+V depuis des sessions distantes RDP, des pièces jointes Outlook et autres sources de fichiers virtuels

---

## Performances

Conçu pour la vitesse. Testé avec plus de 14 000 éléments par dossier.

- E/S asynchrones — ne bloque jamais le thread UI
- Mises à jour des propriétés par lots avec un minimum de surcharge
- Sélection avec temporisation pour éviter les opérations redondantes lors d'une navigation rapide
- Cache par onglet — changement d'onglet instantané, sans re-rendu
- Chargement concurrent des miniatures avec limitation par SemaphoreSlim

---

## Configuration requise

| | |
|---|---|
| **Système** | Windows 10 version 1903 ou ultérieure / Windows 11 |
| **Architecture** | x64, ARM64 |
| **Runtime** | Windows App SDK 1.8 (.NET 8) |
| **Recommandé** | Windows 11 pour l'arrière-plan Mica |

---

## Compiler depuis les sources

```bash
# Prérequis : Visual Studio 2022 + .NET Desktop + charge de travail WinUI 3

# Cloner
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# Compiler
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Exécuter les tests unitaires
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **Remarque** : Les applications WinUI 3 ne peuvent pas être lancées via `dotnet run`. Utilisez **Visual Studio F5** (packaging MSIX requis).

---

## Contribuer

Vous avez trouvé un bug ? Vous avez une idée de fonctionnalité ? [Ouvrez une issue](https://github.com/LumiBearStudio/SpanFinder/issues) — tous les retours sont les bienvenus.

Pour la configuration de build, les conventions de code et les directives de PR, consultez [CONTRIBUTING.md](../CONTRIBUTING.md).

---

## Soutenir le projet

Si SPAN Finder vous est utile :

- **[Sponsorisez sur GitHub](https://github.com/sponsors/LumiBearStudio)** — offrez-nous un café, un hamburger ou un steak
- **Mettez une Star** sur ce dépôt pour aider d'autres personnes à le découvrir
- **Partagez** avec un collègue qui regrette le Finder de macOS
- **Signalez un bug** — chaque rapport rend SPAN Finder plus stable
- **[Téléchargez depuis le Microsoft Store](https://apps.microsoft.com/detail/9P7NJ351X9TL)** — les avis sur le Store améliorent grandement la visibilité

---

## Confidentialité & Télémétrie

SPAN Finder utilise [Sentry](https://sentry.io) **uniquement pour les rapports de crash**, et vous pouvez le désactiver.

- **Ce que nous collectons** : Type d'exception, trace de pile, version du système d'exploitation, version de l'application
- **Ce que nous NE collectons PAS** : Noms de fichiers, chemins de dossiers, historique de navigation, informations personnelles
- **Aucune analyse d'utilisation, aucun suivi, aucune publicité**
- Tous les chemins de fichiers dans les rapports de crash sont automatiquement nettoyés avant envoi
- `SendDefaultPii = false` — aucune adresse IP ni identifiant utilisateur n'est collecté
- **Désactivable** : Paramètres > Avancé > Désactiver « Rapports de crash »
- Le code source est ouvert — vérifiez par vous-même dans [`CrashReportingService.cs`](../src/Span/Span/Services/CrashReportingService.cs)

Pour plus de détails, consultez la [Politique de Confidentialité](../PRIVACY.md).

---

## Licence

Ce projet est distribué sous la [GNU General Public License v3.0](../LICENSE).

**Exception Microsoft Store** : Le détenteur des droits d'auteur (LumiBear Studio) est autorisé à distribuer les binaires officiels via le Microsoft Store selon ses conditions d'utilisation, lesquelles ne constituent pas des "restrictions supplémentaires" au sens de l'article 7 de la GPL v3. Cette exception s'applique uniquement à la distribution officielle et non aux forks tiers.

**Marque déposée** : Le nom "SPAN Finder" et le logo officiel sont des marques de LumiBear Studio. Les forks doivent utiliser un nom et un logo différents. Pour la politique complète concernant les marques, consultez [LICENSE.md](../LICENSE.md).

---

<p align="center">
  <a href="https://apps.microsoft.com/detail/9P7NJ351X9TL">Microsoft Store</a> ·
  <a href="../PRIVACY.md">Politique de Confidentialité</a> ·
  <a href="../OpenSourceLicenses.md">Licences Open Source</a> ·
  <a href="https://github.com/LumiBearStudio/SpanFinder/issues">Signaler un bug & Demander une fonctionnalité</a>
</p>
