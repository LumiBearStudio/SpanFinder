/* ══════════════════════════════════════════════
   Span Onboarding — client script
   ══════════════════════════════════════════════ */

(function () {
  'use strict';

  const params = new URLSearchParams(location.search);
  const LANG = params.get('lang') || 'en';

  const I18N = {
    en: {
      welcome_subtitle: 'Your file explorer, reimagined.',
      miller_title: 'Miller Columns',
      miller_desc: 'Drill down through folders with a cascading column view.\nPress → to dive in, ← to go back.',
      mc_this_pc: 'This PC',
      mc_documents: 'Documents',
      mc_downloads: 'Downloads',
      mc_desktop: 'Desktop',
      mc_projects: 'Projects',
      mc_span: 'Span',
      mc_notes: 'Notes',
      mc_readme: 'README.md',
      mc_main: 'MainWindow.xaml',
      mc_app: 'App.xaml',
      mc_assets: 'Assets',
      tabs_title: 'Tabs & Tear-Off',
      tabs_desc: 'Open multiple folders in tabs.\nDrag a tab out to break it into its own window.',
      shelf_title: 'Shelf & Split View',
      shelf_desc: 'Park files on the Shelf (right edge), then open a Split View to move them across folders in a single window.',
      sv_src: 'Source',
      sv_dst: 'Destination',
      shelf_label: 'Shelf',
      theme_title: 'Pick a Theme',
      theme_desc: 'You can change this anytime in Settings.',
      theme_system: 'System',
      theme_light: 'Light',
      theme_dark: 'Dark',
      theme_hint: 'Defaults to System — matches your Windows preference.',
      dfm_title: 'Set SPAN Finder as my default file explorer',
      dfm_desc: 'Replace the built-in Windows Explorer with SPAN. Requires admin (UAC) approval.',
      shortcuts_title: 'Handy Shortcuts',
      shortcuts_start: 'Get Started',
      nav_next: 'Next',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look preview' },
        { keys: ['Ctrl+,'], label: 'Open Settings' },
        { keys: ['Ctrl+T'], label: 'New tab' },
        { keys: ['Ctrl+Shift+E'], label: 'Toggle split view' },
        { keys: ['F2'], label: 'Rename' },
        { keys: ['Ctrl+F'], label: 'Search in folder' },
        { keys: ['Alt+←/→'], label: 'Back / Forward' },
        { keys: ['F1'], label: 'All shortcuts' },
      ]
    },
    ko: {
      welcome_subtitle: '\ud30c\uc77c \ud0d0\uc0c9\uae30\ub97c \uc0c8\ub86d\uac8c \uc0c1\uc0c1\ud558\ub2e4.',
      miller_title: '\ubc00\ub7ec \ucef4\ub7fc',
      miller_desc: '\uacc4\ub2e8\uc2dd \ucef4\ub7fc \ubdf0\ub85c \ud3f4\ub354\ub97c \ub4dc\ub9b4\ub2e4\uc6b4.\n\u2192 \ud0a4\ub85c \uc9c4\uc785, \u2190 \ud0a4\ub85c \ub4a4\ub85c \uac00\uae30.',
      mc_this_pc: '\ub0b4 PC',
      mc_documents: '\ubb38\uc11c',
      mc_downloads: '\ub2e4\uc6b4\ub85c\ub4dc',
      mc_desktop: '\ubc14\ud0d5 \ud654\uba74',
      mc_projects: '\ud504\ub85c\uc81d\ud2b8',
      mc_span: 'Span',
      mc_notes: '\uba54\ubaa8',
      mc_readme: 'README.md',
      mc_main: 'MainWindow.xaml',
      mc_app: 'App.xaml',
      mc_assets: 'Assets',
      tabs_title: '\ud0ed & \ubd84\ub9ac',
      tabs_desc: '\uc5ec\ub7ec \ud3f4\ub354\ub97c \ud0ed\uc73c\ub85c \uc5f4\uc5b4\ub450\uace0,\n\ud0ed\uc744 \ubc14\uae65\uc73c\ub85c \ub4dc\ub798\uadf8\ud558\uba74 \uc0c8 \ucc3d\uc73c\ub85c \ubd84\ub9ac\ub429\ub2c8\ub2e4.',
      shelf_title: 'Shelf & \ubd84\ud560 \ubdf0',
      shelf_desc: '\ud30c\uc77c\uc744 Shelf(\uc6b0\uce21 \uac00\uc7a5\uc790\ub9ac)\uc5d0 \uc7a0\uc2dc \uac78\uc5b4\ub450\uace0,\n\ubd84\ud560 \ubdf0\ub85c \ud55c \ucc3d \uc548\uc5d0\uc11c \ubc14\ub85c \uc774\ub3d9\ud558\uc138\uc694.',
      sv_src: '\uc6d0\ubcf8',
      sv_dst: '\ub300\uc0c1',
      shelf_label: 'Shelf',
      theme_title: '테마 선택',
      theme_desc: '언제든 설정에서 변경할 수 있습니다.',
      theme_system: '시스템',
      theme_light: '라이트',
      theme_dark: '다크',
      theme_hint: '기본값은 시스템 — Windows 설정을 따라갑니다.',
      dfm_title: 'SPAN Finder를 기본 파일 탐색기로 사용',
      dfm_desc: 'Windows 기본 탐색기를 SPAN으로 대체합니다. 관리자 승인(UAC)이 필요해요.',
      shortcuts_title: '\uc720\uc6a9\ud55c \ub2e8\ucd95\ud0a4',
      shortcuts_start: '\uc2dc\uc791\ud558\uae30',
      nav_next: '\ub2e4\uc74c',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look \ubbf8\ub9ac\ubcf4\uae30' },
        { keys: ['Ctrl+,'], label: '\uc124\uc815 \uc5f4\uae30' },
        { keys: ['Ctrl+T'], label: '\uc0c8 \ud0ed' },
        { keys: ['Ctrl+Shift+E'], label: '\ubd84\ud560 \ubdf0 \ud1a0\uae00' },
        { keys: ['F2'], label: '\uc774\ub984 \ubcc0\uacbd' },
        { keys: ['Ctrl+F'], label: '\ud3f4\ub354 \ub0b4 \uac80\uc0c9' },
        { keys: ['Alt+\u2190/\u2192'], label: '\ub4a4\ub85c / \uc55e\uc73c\ub85c' },
        { keys: ['F1'], label: '\ubaa8\ub4e0 \ub2e8\ucd95\ud0a4' },
      ]
    },
    ja: {
      welcome_subtitle: 'ファイルエクスプローラーを、再構築する。',
      miller_title: 'ミラーカラム',
      miller_desc: 'カスケード式カラムビューでフォルダを掘り下げる。\n→キーで進む、←キーで戻る。',
      mc_this_pc: 'PC', mc_documents: 'ドキュメント', mc_downloads: 'ダウンロード', mc_desktop: 'デスクトップ',
      mc_projects: 'プロジェクト', mc_span: 'Span', mc_notes: 'メモ',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: 'タブ & 切り離し',
      tabs_desc: '複数のフォルダをタブで開き、\nタブを外にドラッグして別ウィンドウへ分離。',
      shelf_title: 'Shelf & 分割ビュー',
      shelf_desc: 'ファイルをShelf（右端）に一時保管し、\n分割ビューで一つのウィンドウから移動。',
      sv_src: 'コピー元', sv_dst: 'コピー先', shelf_label: 'Shelf',
      theme_title: 'テーマを選択',
      theme_desc: '設定からいつでも変更できます。',
      theme_system: 'システム', theme_light: 'ライト', theme_dark: 'ダーク',
      theme_hint: 'デフォルトはシステム — Windowsの設定に従います。',
      dfm_title: 'SPAN Finderを既定のファイルエクスプローラーに設定',
      dfm_desc: 'Windowsの既定エクスプローラーをSPANに置き換えます。管理者権限（UAC）が必要です。',
      shortcuts_title: '便利なショートカット', shortcuts_start: '使い始める', nav_next: '次へ',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look プレビュー' },
        { keys: ['Ctrl+,'], label: '設定を開く' },
        { keys: ['Ctrl+T'], label: '新しいタブ' },
        { keys: ['Ctrl+Shift+E'], label: '分割ビュー切替' },
        { keys: ['F2'], label: '名前変更' },
        { keys: ['Ctrl+F'], label: 'フォルダ内検索' },
        { keys: ['Alt+\u2190/\u2192'], label: '戻る / 進む' },
        { keys: ['F1'], label: 'すべてのショートカット' },
      ]
    },
    'zh-Hans': {
      welcome_subtitle: '重新构想的文件资源管理器。',
      miller_title: 'Miller 列',
      miller_desc: '通过级联列视图深入文件夹。\n按 → 进入，按 ← 返回。',
      mc_this_pc: '此电脑', mc_documents: '文档', mc_downloads: '下载', mc_desktop: '桌面',
      mc_projects: '项目', mc_span: 'Span', mc_notes: '笔记',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: '标签页与分离',
      tabs_desc: '在标签页中打开多个文件夹，\n将标签页拖出可分离为新窗口。',
      shelf_title: 'Shelf 与分割视图',
      shelf_desc: '将文件暂存到 Shelf（右侧边缘），\n分割视图让你在同一窗口内移动文件。',
      sv_src: '源', sv_dst: '目标', shelf_label: 'Shelf',
      theme_title: '选择主题',
      theme_desc: '可以随时在设置中更改。',
      theme_system: '系统', theme_light: '浅色', theme_dark: '深色',
      theme_hint: '默认为系统 — 跟随 Windows 设置。',
      dfm_title: '将 SPAN Finder 设为默认文件资源管理器',
      dfm_desc: '用 SPAN 替换 Windows 内置资源管理器。需要管理员权限（UAC）。',
      shortcuts_title: '常用快捷键', shortcuts_start: '开始使用', nav_next: '下一步',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look 预览' },
        { keys: ['Ctrl+,'], label: '打开设置' },
        { keys: ['Ctrl+T'], label: '新标签页' },
        { keys: ['Ctrl+Shift+E'], label: '切换分割视图' },
        { keys: ['F2'], label: '重命名' },
        { keys: ['Ctrl+F'], label: '在文件夹中搜索' },
        { keys: ['Alt+\u2190/\u2192'], label: '后退 / 前进' },
        { keys: ['F1'], label: '所有快捷键' },
      ]
    },
    'zh-Hant': {
      welcome_subtitle: '重新構想的檔案總管。',
      miller_title: 'Miller 欄',
      miller_desc: '透過層疊欄視圖深入資料夾。\n按 → 進入，按 ← 返回。',
      mc_this_pc: '本機', mc_documents: '文件', mc_downloads: '下載', mc_desktop: '桌面',
      mc_projects: '專案', mc_span: 'Span', mc_notes: '筆記',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: '分頁與分離',
      tabs_desc: '在分頁中開啟多個資料夾，\n將分頁拖出可分離為新視窗。',
      shelf_title: 'Shelf 與分割檢視',
      shelf_desc: '將檔案暫存到 Shelf（右側邊緣），\n分割檢視讓你在同一視窗內移動檔案。',
      sv_src: '來源', sv_dst: '目標', shelf_label: 'Shelf',
      theme_title: '選擇主題',
      theme_desc: '可以隨時在設定中變更。',
      theme_system: '系統', theme_light: '淺色', theme_dark: '深色',
      theme_hint: '預設為系統 — 跟隨 Windows 設定。',
      dfm_title: '將 SPAN Finder 設為預設檔案總管',
      dfm_desc: '以 SPAN 取代 Windows 內建檔案總管。需要系統管理員權限（UAC）。',
      shortcuts_title: '常用快捷鍵', shortcuts_start: '開始使用', nav_next: '下一步',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look 預覽' },
        { keys: ['Ctrl+,'], label: '開啟設定' },
        { keys: ['Ctrl+T'], label: '新分頁' },
        { keys: ['Ctrl+Shift+E'], label: '切換分割檢視' },
        { keys: ['F2'], label: '重新命名' },
        { keys: ['Ctrl+F'], label: '在資料夾中搜尋' },
        { keys: ['Alt+\u2190/\u2192'], label: '上一頁 / 下一頁' },
        { keys: ['F1'], label: '所有快捷鍵' },
      ]
    },
    de: {
      welcome_subtitle: 'Dein Datei-Explorer, neu gedacht.',
      miller_title: 'Miller-Spalten',
      miller_desc: 'Tauche durch Ordner mit kaskadierender Spaltenansicht.\nDrücke → zum Eintauchen, ← zum Zurück.',
      mc_this_pc: 'Dieser PC', mc_documents: 'Dokumente', mc_downloads: 'Downloads', mc_desktop: 'Desktop',
      mc_projects: 'Projekte', mc_span: 'Span', mc_notes: 'Notizen',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: 'Tabs & Abreißen',
      tabs_desc: 'Öffne mehrere Ordner in Tabs.\nZiehe einen Tab heraus, um ihn als eigenes Fenster zu lösen.',
      shelf_title: 'Shelf & Geteilte Ansicht',
      shelf_desc: 'Lege Dateien in der Shelf (rechter Rand) ab und\nverschiebe sie mit der geteilten Ansicht in einem Fenster.',
      sv_src: 'Quelle', sv_dst: 'Ziel', shelf_label: 'Shelf',
      theme_title: 'Design wählen',
      theme_desc: 'Du kannst dies jederzeit in den Einstellungen ändern.',
      theme_system: 'System', theme_light: 'Hell', theme_dark: 'Dunkel',
      theme_hint: 'Standard ist System — folgt deiner Windows-Einstellung.',
      dfm_title: 'SPAN Finder als Standard-Datei-Explorer festlegen',
      dfm_desc: 'Ersetzt den Windows-Explorer durch SPAN. Erfordert Administratorrechte (UAC).',
      shortcuts_title: 'Praktische Tastenkürzel', shortcuts_start: 'Loslegen', nav_next: 'Weiter',
      shortcuts: [
        { keys: ['Space'], label: 'Quick Look Vorschau' },
        { keys: ['Ctrl+,'], label: 'Einstellungen öffnen' },
        { keys: ['Ctrl+T'], label: 'Neuer Tab' },
        { keys: ['Ctrl+Shift+E'], label: 'Geteilte Ansicht umschalten' },
        { keys: ['F2'], label: 'Umbenennen' },
        { keys: ['Ctrl+F'], label: 'Im Ordner suchen' },
        { keys: ['Alt+\u2190/\u2192'], label: 'Zurück / Vorwärts' },
        { keys: ['F1'], label: 'Alle Tastenkürzel' },
      ]
    },
    es: {
      welcome_subtitle: 'Tu explorador de archivos, reinventado.',
      miller_title: 'Columnas Miller',
      miller_desc: 'Explora carpetas con una vista de columnas en cascada.\nPulsa → para entrar, ← para volver.',
      mc_this_pc: 'Este PC', mc_documents: 'Documentos', mc_downloads: 'Descargas', mc_desktop: 'Escritorio',
      mc_projects: 'Proyectos', mc_span: 'Span', mc_notes: 'Notas',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: 'Pestañas y Desacoplar',
      tabs_desc: 'Abre varias carpetas en pestañas.\nArrastra una pestaña fuera para desacoplarla en su propia ventana.',
      shelf_title: 'Shelf y Vista dividida',
      shelf_desc: 'Coloca archivos en el Shelf (borde derecho) y\nmuévelos entre carpetas con la vista dividida en una sola ventana.',
      sv_src: 'Origen', sv_dst: 'Destino', shelf_label: 'Shelf',
      theme_title: 'Elige un tema',
      theme_desc: 'Puedes cambiarlo en cualquier momento desde Ajustes.',
      theme_system: 'Sistema', theme_light: 'Claro', theme_dark: 'Oscuro',
      theme_hint: 'Por defecto Sistema — sigue tu preferencia de Windows.',
      dfm_title: 'Establecer SPAN Finder como explorador de archivos predeterminado',
      dfm_desc: 'Sustituye el Explorador de Windows por SPAN. Requiere aprobación de administrador (UAC).',
      shortcuts_title: 'Atajos útiles', shortcuts_start: 'Empezar', nav_next: 'Siguiente',
      shortcuts: [
        { keys: ['Space'], label: 'Vista previa Quick Look' },
        { keys: ['Ctrl+,'], label: 'Abrir ajustes' },
        { keys: ['Ctrl+T'], label: 'Nueva pestaña' },
        { keys: ['Ctrl+Shift+E'], label: 'Alternar vista dividida' },
        { keys: ['F2'], label: 'Renombrar' },
        { keys: ['Ctrl+F'], label: 'Buscar en carpeta' },
        { keys: ['Alt+\u2190/\u2192'], label: 'Atrás / Adelante' },
        { keys: ['F1'], label: 'Todos los atajos' },
      ]
    },
    fr: {
      welcome_subtitle: 'Votre explorateur de fichiers, réinventé.',
      miller_title: 'Colonnes Miller',
      miller_desc: 'Explorez les dossiers avec une vue en colonnes en cascade.\nAppuyez sur → pour entrer, ← pour revenir.',
      mc_this_pc: 'Ce PC', mc_documents: 'Documents', mc_downloads: 'Téléchargements', mc_desktop: 'Bureau',
      mc_projects: 'Projets', mc_span: 'Span', mc_notes: 'Notes',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: 'Onglets et Détacher',
      tabs_desc: 'Ouvrez plusieurs dossiers dans des onglets.\nFaites glisser un onglet pour le détacher dans sa propre fenêtre.',
      shelf_title: 'Shelf et Vue partagée',
      shelf_desc: 'Déposez des fichiers dans le Shelf (bord droit) et\ndéplacez-les entre dossiers avec la vue partagée dans une seule fenêtre.',
      sv_src: 'Source', sv_dst: 'Destination', shelf_label: 'Shelf',
      theme_title: 'Choisir un thème',
      theme_desc: 'Vous pouvez le modifier à tout moment dans les paramètres.',
      theme_system: 'Système', theme_light: 'Clair', theme_dark: 'Sombre',
      theme_hint: 'Par défaut Système — suit votre préférence Windows.',
      dfm_title: 'Définir SPAN Finder comme explorateur de fichiers par défaut',
      dfm_desc: 'Remplace l\u2019Explorateur Windows par SPAN. Nécessite l\u2019approbation administrateur (UAC).',
      shortcuts_title: 'Raccourcis pratiques', shortcuts_start: 'Commencer', nav_next: 'Suivant',
      shortcuts: [
        { keys: ['Space'], label: 'Aperçu Quick Look' },
        { keys: ['Ctrl+,'], label: 'Ouvrir les paramètres' },
        { keys: ['Ctrl+T'], label: 'Nouvel onglet' },
        { keys: ['Ctrl+Shift+E'], label: 'Basculer la vue partagée' },
        { keys: ['F2'], label: 'Renommer' },
        { keys: ['Ctrl+F'], label: 'Rechercher dans le dossier' },
        { keys: ['Alt+\u2190/\u2192'], label: 'Précédent / Suivant' },
        { keys: ['F1'], label: 'Tous les raccourcis' },
      ]
    },
    'pt-BR': {
      welcome_subtitle: 'Seu explorador de arquivos, reimaginado.',
      miller_title: 'Colunas Miller',
      miller_desc: 'Explore pastas com uma visualização em colunas em cascata.\nPressione → para entrar, ← para voltar.',
      mc_this_pc: 'Este PC', mc_documents: 'Documentos', mc_downloads: 'Downloads', mc_desktop: 'Área de Trabalho',
      mc_projects: 'Projetos', mc_span: 'Span', mc_notes: 'Notas',
      mc_readme: 'README.md', mc_main: 'MainWindow.xaml', mc_app: 'App.xaml', mc_assets: 'Assets',
      tabs_title: 'Abas e Destacar',
      tabs_desc: 'Abra várias pastas em abas.\nArraste uma aba para fora para destacá-la em uma nova janela.',
      shelf_title: 'Shelf e Visualização dividida',
      shelf_desc: 'Coloque arquivos no Shelf (borda direita) e\nmova-os entre pastas com a visualização dividida em uma única janela.',
      sv_src: 'Origem', sv_dst: 'Destino', shelf_label: 'Shelf',
      theme_title: 'Escolha um tema',
      theme_desc: 'Você pode alterar isso a qualquer momento nas configurações.',
      theme_system: 'Sistema', theme_light: 'Claro', theme_dark: 'Escuro',
      theme_hint: 'Padrão é Sistema — segue a preferência do Windows.',
      dfm_title: 'Definir SPAN Finder como gerenciador de arquivos padrão',
      dfm_desc: 'Substitui o Explorador do Windows pelo SPAN. Requer aprovação de administrador (UAC).',
      shortcuts_title: 'Atalhos úteis', shortcuts_start: 'Começar', nav_next: 'Próximo',
      shortcuts: [
        { keys: ['Space'], label: 'Visualização Quick Look' },
        { keys: ['Ctrl+,'], label: 'Abrir configurações' },
        { keys: ['Ctrl+T'], label: 'Nova aba' },
        { keys: ['Ctrl+Shift+E'], label: 'Alternar visualização dividida' },
        { keys: ['F2'], label: 'Renomear' },
        { keys: ['Ctrl+F'], label: 'Pesquisar na pasta' },
        { keys: ['Alt+\u2190/\u2192'], label: 'Voltar / Avançar' },
        { keys: ['F1'], label: 'Todos os atalhos' },
      ]
    }
  };

  let currentPage = 0;
  const TOTAL_PAGES = 6;

  const slider = document.getElementById('slider');
  const pages = slider.querySelectorAll('.page');
  const dots = document.querySelectorAll('.dot');
  const nextBtn = document.getElementById('nextBtn');
  const startBtn = document.getElementById('startBtn');

  let selectedTheme = 'system';   // default
  let selectedDfm = false;        // default off (explicit consent)

  function init() {
    applyI18n();
    buildShortcuts();
    setupThemeSelector();
    setupDfmToggle();
    setupNav();
    onPageEnter(0);
  }

  function setupThemeSelector() {
    const opts = document.querySelectorAll('.theme-option');
    opts.forEach(function (btn) {
      // pre-mark default
      if (btn.getAttribute('data-theme') === selectedTheme) btn.classList.add('active');

      btn.addEventListener('click', function () {
        const theme = btn.getAttribute('data-theme');
        if (!theme || theme === selectedTheme) return;
        selectedTheme = theme;
        opts.forEach(function (o) { o.classList.toggle('active', o === btn); });
        // notify host immediately for live preview of the main window
        postMessage('theme:' + theme);
      });
    });
  }

  function setupDfmToggle() {
    const tog = document.getElementById('dfmToggle');
    if (!tog) return;
    tog.addEventListener('click', function () {
      selectedDfm = !selectedDfm;
      tog.setAttribute('aria-checked', selectedDfm ? 'true' : 'false');
    });
  }

  function applyI18n() {
    const s = I18N[LANG] || I18N.en;
    document.querySelectorAll('[data-i18n]').forEach(function (el) {
      const key = el.getAttribute('data-i18n');
      if (s[key] && typeof s[key] === 'string') el.textContent = s[key];
    });
  }

  function buildShortcuts() {
    const list = document.getElementById('shortcutList');
    if (!list) return;
    const s = I18N[LANG] || I18N.en;
    s.shortcuts.forEach(function (sc) {
      const item = document.createElement('div');
      item.className = 'shortcut-item';
      const keysDiv = document.createElement('div');
      keysDiv.className = 'shortcut-keys';
      (sc.keys || []).forEach(function (k) {
        const span = document.createElement('span');
        span.className = 'key';
        span.textContent = k;
        keysDiv.appendChild(span);
      });
      const label = document.createElement('span');
      label.className = 'shortcut-label';
      label.textContent = sc.label;
      item.appendChild(keysDiv);
      item.appendChild(label);
      list.appendChild(item);
    });
  }


  function finishOnboarding() {
    // 단일 메시지로 합침 — 분리하면 host에서 UAC awaiting 도중 window가 먼저 닫혀버림 (race)
    postMessage(selectedDfm ? 'complete:dfm' : 'complete');
  }

  function setupNav() {
    nextBtn.addEventListener('click', function () { goTo(currentPage + 1); });
    if (startBtn) startBtn.addEventListener('click', finishOnboarding);
    dots.forEach(function (dot) {
      dot.addEventListener('click', function () {
        const idx = parseInt(dot.getAttribute('data-idx'), 10);
        if (!isNaN(idx)) goTo(idx);
      });
    });
    document.addEventListener('keydown', function (e) {
      if (e.key === 'ArrowRight' || e.key === 'Enter') {
        if (currentPage < TOTAL_PAGES - 1) goTo(currentPage + 1);
        else finishOnboarding();
      }
      if (e.key === 'ArrowLeft' && currentPage > 0) goTo(currentPage - 1);
      if (e.key === 'Escape') finishOnboarding();
    });
  }

  function goTo(idx) {
    if (idx < 0 || idx >= TOTAL_PAGES || idx === currentPage) return;
    const direction = idx > currentPage ? 1 : -1;
    const oldPage = pages[currentPage];
    const newPage = pages[idx];

    onPageLeave(currentPage);
    oldPage.classList.remove('active');
    oldPage.style.transform = 'translateX(' + (-80 * direction) + 'px)';

    newPage.style.transition = 'none';
    newPage.style.transform = 'translateX(' + (80 * direction) + 'px)';
    newPage.style.opacity = '0';
    void newPage.offsetHeight;
    newPage.style.transition = '';
    newPage.style.transform = '';
    newPage.style.opacity = '';
    newPage.classList.add('active');

    currentPage = idx;
    dots.forEach(function (d, i) { d.classList.toggle('active', i === idx); });
    nextBtn.classList.toggle('hidden', idx === TOTAL_PAGES - 1);
    onPageEnter(idx);
  }

  function onPageEnter(idx) { /* reserved for future per-page hooks */ }
  function onPageLeave(idx) { /* reserved for future per-page hooks */ }

  function postMessage(msg) {
    if (window.chrome && window.chrome.webview) {
      window.chrome.webview.postMessage(msg);
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', init);
  } else {
    init();
  }
})();
