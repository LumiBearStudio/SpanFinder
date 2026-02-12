/* ============================================================
   Span — Design Prototype Interactions
   ============================================================ */

document.addEventListener('DOMContentLoaded', () => {

    // ===================== TAB SWITCHING =====================
    const tabBar = document.getElementById('tabBar');
    tabBar.addEventListener('click', (e) => {
        const tab = e.target.closest('.tab');
        if (!tab) return;

        // Close button
        if (e.target.closest('.tab-close')) {
            const tabs = tabBar.querySelectorAll('.tab');
            if (tabs.length <= 1) return; // keep at least one
            const wasActive = tab.classList.contains('active');
            tab.style.transition = 'opacity 120ms, max-width 200ms';
            tab.style.opacity = '0';
            tab.style.maxWidth = '0';
            tab.style.overflow = 'hidden';
            tab.style.padding = '0';
            setTimeout(() => {
                tab.remove();
                if (wasActive) {
                    const remaining = tabBar.querySelectorAll('.tab');
                    if (remaining.length) remaining[0].classList.add('active');
                }
            }, 200);
            return;
        }

        // Activate tab
        tabBar.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        tab.classList.add('active');
    });

    // New tab
    document.querySelector('.tab-new').addEventListener('click', () => {
        const count = tabBar.querySelectorAll('.tab').length;
        const newTab = document.createElement('div');
        newTab.className = 'tab active';
        newTab.dataset.tab = count;
        newTab.innerHTML = `
      <i class="ri-folder-3-fill tab-icon"></i>
      <span class="tab-label">새 탭</span>
      <button class="tab-close" title="닫기"><i class="ri-close-line"></i></button>
    `;
        tabBar.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
        tabBar.insertBefore(newTab, document.querySelector('.tab-new'));
    });

    // ===================== MILLER COLUMN ITEM CLICK =====================
    // NOTE: Main click handler with multi-select (Ctrl+Click) is defined
    // in the MULTI-SELECT section below. This section only captures the
    // reference to millerColumns for use by other features.
    const millerColumns = document.getElementById('millerColumns');

    // ===================== SPLIT VIEW TOGGLE =====================
    const splitToggle = document.getElementById('splitToggle');
    const splitB = document.getElementById('splitB');

    splitToggle.addEventListener('click', () => {
        splitToggle.classList.toggle('active');
        splitB.style.display = splitToggle.classList.contains('active') ? 'flex' : 'none';
    });

    // ===================== VIEW MODE TOGGLE =====================
    const viewToggle = document.getElementById('viewToggle');
    const viewModes = ['miller', 'list', 'grid'];
    let currentView = 0;

    viewToggle.addEventListener('click', (e) => {
        e.stopPropagation();

        // Remove existing dropdown
        document.querySelectorAll('.view-dropdown').forEach(d => d.remove());

        const dropdown = document.createElement('div');
        dropdown.className = 'view-dropdown';
        dropdown.innerHTML = `
      <div class="view-option ${currentView === 0 ? 'active' : ''}" data-view="0">
        <i class="ri-layout-column-line"></i>밀러 컬럼
      </div>
      <div class="view-option ${currentView === 1 ? 'active' : ''}" data-view="1">
        <i class="ri-list-unordered"></i>자세히 보기
      </div>
      <div class="view-option ${currentView === 2 ? 'active' : ''}" data-view="2">
        <i class="ri-grid-fill"></i>아이콘 보기
      </div>
    `;

        viewToggle.style.position = 'relative';
        viewToggle.appendChild(dropdown);

        dropdown.addEventListener('click', (ev) => {
            const option = ev.target.closest('.view-option');
            if (!option) return;
            currentView = parseInt(option.dataset.view);
            applyViewMode(viewModes[currentView]);
            dropdown.remove();
        });

        // Close dropdown on outside click
        setTimeout(() => {
            document.addEventListener('click', function close() {
                dropdown.remove();
                document.removeEventListener('click', close);
            }, { once: true });
        }, 10);
    });

    function applyViewMode(mode) {
        const mc = document.getElementById('millerColumns');
        mc.classList.remove('view-list', 'view-grid');
        if (mode === 'list') mc.classList.add('view-list');
        if (mode === 'grid') mc.classList.add('view-grid');

        // Update toggle icon
        const icons = {
            miller: 'ri-layout-column-line',
            list: 'ri-list-unordered',
            grid: 'ri-grid-fill'
        };
        const labels = {
            miller: '밀러 컬럼',
            list: '자세히 보기',
            grid: '아이콘 보기'
        };
        viewToggle.querySelector('i').className = icons[mode];
        document.querySelector('.view-mode-indicator i').className = icons[mode];
        document.querySelector('.view-mode-indicator').innerHTML = `<i class="${icons[mode]}"></i> ${labels[mode]}`;
    }

    // ===================== CONTEXT MENU =====================
    document.addEventListener('contextmenu', (e) => {
        e.preventDefault();
        // Remove existing
        document.querySelectorAll('.context-menu').forEach(m => m.remove());

        const menu = document.createElement('div');
        menu.className = 'context-menu';
        menu.innerHTML = `
      <div class="ctx-item"><i class="ri-folder-add-line"></i>새 폴더<span class="shortcut">Ctrl+Shift+N</span></div>
      <div class="ctx-item"><i class="ri-file-add-line"></i>새 파일</div>
      <div class="ctx-separator"></div>
      <div class="ctx-item ctx-smart-run"><i class="ri-flashlight-line"></i>Smart Run<span class="shortcut">Ctrl+Enter</span></div>
      <div class="ctx-separator"></div>
      <div class="ctx-item"><i class="ri-scissors-cut-line"></i>잘라내기<span class="shortcut">Ctrl+X</span></div>
      <div class="ctx-item"><i class="ri-file-copy-line"></i>복사<span class="shortcut">Ctrl+C</span></div>
      <div class="ctx-item"><i class="ri-clipboard-line"></i>붙여넣기<span class="shortcut">Ctrl+V</span></div>
      <div class="ctx-separator"></div>
      <div class="ctx-item"><i class="ri-pencil-line"></i>이름 바꾸기<span class="shortcut">F2</span></div>
      <div class="ctx-item ctx-danger"><i class="ri-delete-bin-6-line"></i>삭제<span class="shortcut">Del</span></div>
      <div class="ctx-separator"></div>
      <div class="ctx-item"><i class="ri-information-line"></i>속성<span class="shortcut">Alt+Enter</span></div>
      <div class="ctx-separator ctx-more-separator"></div>
      <div class="ctx-item ctx-more-options"><i class="ri-arrow-right-s-line"></i>더 많은 옵션 보기<span class="shortcut">Shift+F10</span></div>
    `;

        // Position
        let x = e.clientX, y = e.clientY;
        document.body.appendChild(menu);
        const rect = menu.getBoundingClientRect();
        if (x + rect.width > window.innerWidth) x = window.innerWidth - rect.width - 8;
        if (y + rect.height > window.innerHeight) y = window.innerHeight - rect.height - 8;
        menu.style.left = x + 'px';
        menu.style.top = y + 'px';

        // Close on click outside
        setTimeout(() => {
            document.addEventListener('click', function close() {
                menu.remove();
                document.removeEventListener('click', close);
            }, { once: true });
        }, 10);
    });

    // ===================== SIDEBAR CLICK =====================
    document.getElementById('sidebar').addEventListener('click', (e) => {
        const item = e.target.closest('.sidebar-item');
        if (!item) return;
        document.querySelectorAll('.sidebar-item').forEach(i => i.classList.remove('active'));
        item.classList.add('active');
    });

    // ===================== HELPERS =====================

    const mockFolders = {
        'default': [
            { name: 'bin', type: 'folder' },
            { name: 'obj', type: 'folder' },
            { name: 'Properties', type: 'folder' },
            { name: 'Controllers', type: 'folder' },
            { name: 'Program.cs', type: 'file', icon: 'file-cs' },
            { name: 'Startup.cs', type: 'file', icon: 'file-cs' },
            { name: 'appsettings.json', type: 'file', icon: 'file-json' },
        ],
        'Converters': [
            { name: 'BoolToVisibility.cs', type: 'file', icon: 'file-cs' },
            { name: 'FileSizeConverter.cs', type: 'file', icon: 'file-cs' },
            { name: 'IconConverter.cs', type: 'file', icon: 'file-cs' },
        ],
        'Helpers': [
            { name: 'FileSystemHelper.cs', type: 'file', icon: 'file-cs' },
            { name: 'NativeInterop.cs', type: 'file', icon: 'file-cs' },
            { name: 'IconExtractor.cs', type: 'file', icon: 'file-cs' },
            { name: 'AsyncEnumerator.cs', type: 'file', icon: 'file-cs' },
        ],
        'Services': [
            { name: 'FileService.cs', type: 'file', icon: 'file-cs' },
            { name: 'NavigationService.cs', type: 'file', icon: 'file-cs' },
            { name: 'TabService.cs', type: 'file', icon: 'file-cs' },
            { name: 'PreviewService.cs', type: 'file', icon: 'file-cs' },
            { name: 'SearchService.cs', type: 'file', icon: 'file-cs' },
        ],
        'ViewModels': [
            { name: 'MainViewModel.cs', type: 'file', icon: 'file-cs' },
            { name: 'MillerColumnViewModel.cs', type: 'file', icon: 'file-cs' },
            { name: 'FileItemViewModel.cs', type: 'file', icon: 'file-cs' },
            { name: 'TabViewModel.cs', type: 'file', icon: 'file-cs' },
            { name: 'SidebarViewModel.cs', type: 'file', icon: 'file-cs' },
        ],
        'Views': [
            { name: 'MainWindow.xaml', type: 'file', icon: 'file-xaml' },
            { name: 'MainWindow.xaml.cs', type: 'file', icon: 'file-cs' },
            { name: 'MillerColumnView.xaml', type: 'file', icon: 'file-xaml' },
            { name: 'MillerColumnView.xaml.cs', type: 'file', icon: 'file-cs' },
            { name: 'PreviewPane.xaml', type: 'file', icon: 'file-xaml' },
            { name: 'SettingsPage.xaml', type: 'file', icon: 'file-xaml' },
        ],
        'Properties': [
            { name: 'launchSettings.json', type: 'file', icon: 'file-json' },
            { name: 'AssemblyInfo.cs', type: 'file', icon: 'file-cs' },
        ],
        'Controllers': [
            { name: 'HomeController.cs', type: 'file', icon: 'file-cs' },
            { name: 'ApiController.cs', type: 'file', icon: 'file-cs' },
        ],
        'Assets': [
            { name: 'Icons', type: 'folder' },
            { name: 'Fonts', type: 'folder' },
            { name: 'app-icon.png', type: 'file', icon: 'file-md' },
            { name: 'splash.png', type: 'file', icon: 'file-md' },
        ],
        'Tests': [
            { name: 'UnitTests', type: 'folder' },
            { name: 'IntegrationTests', type: 'folder' },
            { name: 'Span.Tests.csproj', type: 'file', icon: 'file-sln' },
        ],
        'bin': [
            { name: 'Debug', type: 'folder' },
            { name: 'Release', type: 'folder' },
        ],
        'obj': [
            { name: 'Debug', type: 'folder' },
            { name: 'Release', type: 'folder' },
        ]
    };

    function createMockColumn(depth, folderName) {
        const col = document.createElement('div');
        col.className = 'miller-column';
        col.dataset.depth = depth;

        const items = mockFolders[folderName] || mockFolders['default'];
        const itemsDiv = document.createElement('div');
        itemsDiv.className = 'column-items';

        items.forEach(item => {
            const el = document.createElement('div');
            el.className = `miller-item ${item.type}`;

            if (item.type === 'folder') {
                el.innerHTML = `<i class="ri-folder-fill item-icon folder-icon"></i><span>${item.name}</span>`;
            } else {
                const iconClass = item.icon || 'file-cs';
                el.innerHTML = `<i class="ri-code-s-slash-fill item-icon ${iconClass}"></i><span>${item.name}</span>`;
            }

            itemsDiv.appendChild(el);
        });

        col.appendChild(itemsDiv);
        return col;
    }

    function createPreviewPane(fileName) {
        const ext = fileName.split('.').pop().toLowerCase();
        const typeMap = {
            'cs': { label: 'C# 소스 파일', icon: 'file-cs' },
            'xaml': { label: 'XAML 마크업', icon: 'file-xaml' },
            'json': { label: 'JSON 파일', icon: 'file-json' },
            'md': { label: 'Markdown 문서', icon: 'file-md' },
            'sln': { label: 'Visual Studio 솔루션', icon: 'file-sln' },
            'csproj': { label: 'C# 프로젝트', icon: 'file-sln' },
            'png': { label: 'PNG 이미지', icon: 'file-md' },
        };

        const info = typeMap[ext] || { label: '파일', icon: 'file-cs' };
        const size = getFileSize(fileName);

        const pane = document.createElement('div');
        pane.className = 'preview-pane';
        pane.id = 'previewPane';
        pane.innerHTML = `
      <div class="preview-header">
        <i class="ri-code-s-slash-fill preview-icon ${info.icon}"></i>
        <span>${fileName}</span>
      </div>
      <div class="preview-meta">
        <div class="meta-row"><span class="meta-label">형식</span><span class="meta-value">${info.label}</span></div>
        <div class="meta-row"><span class="meta-label">크기</span><span class="meta-value">${size}</span></div>
        <div class="meta-row"><span class="meta-label">수정한 날짜</span><span class="meta-value">2026년 2월 10일 오후 3:42</span></div>
        <div class="meta-row"><span class="meta-label">만든 날짜</span><span class="meta-value">2026년 2월 8일 오전 10:15</span></div>
      </div>
    `;

        return pane;
    }

    function getFileSize(name) {
        const sizes = ['1.2 KB', '2.4 KB', '3.8 KB', '640 B', '5.1 KB', '12.3 KB', '890 B', '4.7 KB'];
        let hash = 0;
        for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) & 0xffff;
        return sizes[hash % sizes.length];
    }

    function updateBreadcrumb(depth, folderName) {
        const bc = document.getElementById('breadcrumb');
        // Remove crumbs deeper than parent depth
        const crumbs = bc.querySelectorAll('.crumb');
        const seps = bc.querySelectorAll('.crumb-sep');

        // keep first (depth + 1) pairs, then add the new one if needed
        // For simplicity: just append
        const sep = document.createElement('i');
        sep.className = 'ri-arrow-right-s-line crumb-sep';
        const crumb = document.createElement('span');
        crumb.className = 'crumb active';
        crumb.textContent = folderName;

        // Remove 'active' from others
        crumbs.forEach(c => c.classList.remove('active'));

        bc.appendChild(sep);
        bc.appendChild(crumb);
    }

    function updateStatus(totalCount, selectedCount, size) {
        const left = document.querySelector('.status-left');
        let html = '';
        if (totalCount !== null) {
            html += `<span class="status-item"><i class="ri-file-list-3-line"></i> ${totalCount}개 항목</span>`;
        } else {
            const existing = left.querySelector('.status-item');
            html += existing ? existing.outerHTML : '';
        }

        if (selectedCount > 0) {
            html += `<span class="status-sep">|</span>`;
            html += `<span class="status-item">${selectedCount}개 선택됨</span>`;
            if (size) {
                html += `<span class="status-sep">|</span>`;
                html += `<span class="status-item">${size}</span>`;
            }
        }

        left.innerHTML = html;
    }

    // ===================== ADDRESS BAR INPUT MODE (Ctrl+L) =====================
    const addressBar = document.getElementById('addressBar');
    const addressTextField = document.getElementById('addressTextField');
    const addressAutocomplete = document.getElementById('addressAutocomplete');
    const breadcrumbEl = document.getElementById('breadcrumb');

    function enterAddressInputMode() {
        addressBar.classList.add('input-mode');
        addressTextField.focus();
        addressTextField.select();
        addressAutocomplete.style.display = 'block';
    }

    function exitAddressInputMode() {
        addressBar.classList.remove('input-mode');
        addressAutocomplete.style.display = 'none';
    }

    // Click breadcrumb bar → enter input mode
    breadcrumbEl.addEventListener('click', (e) => {
        if (e.target.closest('.crumb')) {
            // Allow normal crumb clicks, but double-click enters input mode
            return;
        }
        enterAddressInputMode();
    });

    breadcrumbEl.addEventListener('dblclick', () => {
        enterAddressInputMode();
    });

    // Escape or Enter exits input mode
    addressTextField.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            exitAddressInputMode();
        }
        if (e.key === 'Enter') {
            exitAddressInputMode();
        }
    });

    // Click outside exits
    addressTextField.addEventListener('blur', () => {
        setTimeout(() => exitAddressInputMode(), 150);
    });

    // Typing shows autocomplete
    addressTextField.addEventListener('input', () => {
        addressAutocomplete.style.display = addressTextField.value.length > 0 ? 'block' : 'none';
    });

    // ===================== MULTI-SELECT (Ctrl+Click) =====================
    // Override the miller click to support Ctrl+Click
    const _origMillerClick = millerColumns.onclick;
    millerColumns.removeEventListener('click', _origMillerClick);

    millerColumns.addEventListener('click', (e) => {
        const item = e.target.closest('.miller-item');
        if (!item) return;

        const column = item.closest('.miller-column');
        const depth = parseInt(column.dataset.depth);

        // Ctrl+Click: toggle multi-select on this item
        if (e.ctrlKey) {
            item.classList.toggle('multi-selected');

            // Count multi-selected items
            const multiCount = column.querySelectorAll('.miller-item.multi-selected').length;
            if (multiCount > 0) {
                // Calculate total size
                let totalSize = 0;
                column.querySelectorAll('.miller-item.multi-selected').forEach(mi => {
                    const name = mi.querySelector('span')?.textContent || '';
                    totalSize++;
                });
                updateStatus(null, multiCount, `${multiCount}개 파일`);
            }
            return;
        }

        // Clear multi-select when normal click
        document.querySelectorAll('.miller-item.multi-selected').forEach(mi => {
            mi.classList.remove('multi-selected');
        });

        // Normal click: original behavior
        column.querySelectorAll('.miller-item').forEach(i => i.classList.remove('selected'));
        item.classList.add('selected');

        // Remove deeper columns
        const allCols = millerColumns.querySelectorAll('.miller-column');
        allCols.forEach(col => {
            if (parseInt(col.dataset.depth) > depth) col.remove();
        });

        // Remove preview pane from split-pane
        const splitPane = millerColumns.closest('.split-pane');
        const preview = splitPane.querySelector('.preview-pane');
        if (preview) preview.remove();

        // If folder, spawn new column
        if (item.classList.contains('folder')) {
            item.querySelector('.item-arrow')?.remove();
            const arrow = document.createElement('i');
            arrow.className = 'ri-arrow-right-s-line item-arrow';
            item.appendChild(arrow);

            const folderName = item.querySelector('span').textContent;
            const newCol = createMockColumn(depth + 1, folderName);
            millerColumns.appendChild(newCol);

            setTimeout(() => {
                newCol.scrollIntoView({ behavior: 'smooth', inline: 'end', block: 'nearest' });
            }, 50);

            updateBreadcrumb(depth + 1, folderName);

            const count = newCol.querySelectorAll('.miller-item').length;
            updateStatus(count, 0, '');
        } else {
            // File selected — show preview docked to right edge
            const fileName = item.querySelector('span').textContent;
            const previewPane = createPreviewPane(fileName);
            splitPane.appendChild(previewPane);
            setTimeout(() => {
                previewPane.scrollIntoView({ behavior: 'smooth', inline: 'end', block: 'nearest' });
            }, 50);

            updateStatus(null, 1, getFileSize(fileName));
        }
    });

    // ===================== INLINE RENAME (F2) =====================
    function startInlineRename() {
        const selected = document.querySelector('.miller-item.selected');
        if (!selected) return;

        selected.classList.add('renaming');
        const nameSpan = selected.querySelector('span');
        const name = nameSpan.textContent;

        const input = document.createElement('input');
        input.type = 'text';
        input.className = 'rename-input';
        input.value = name;
        selected.appendChild(input);

        input.focus();
        // Select filename without extension
        const dotIndex = name.lastIndexOf('.');
        if (dotIndex > 0) {
            input.setSelectionRange(0, dotIndex);
        } else {
            input.select();
        }

        function finishRename(save) {
            if (save && input.value.trim()) {
                nameSpan.textContent = input.value.trim();
            }
            selected.classList.remove('renaming');
            input.remove();
        }

        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') { e.preventDefault(); finishRename(true); }
            if (e.key === 'Escape') { e.preventDefault(); finishRename(false); }
            e.stopPropagation();
        });

        input.addEventListener('blur', () => finishRename(true));
    }

    // ===================== QUICK LOOK (Space) =====================
    function showQuickLook() {
        const selected = document.querySelector('.miller-item.selected');
        if (!selected || selected.classList.contains('folder')) return;

        const fileName = selected.querySelector('span').textContent;
        const ext = fileName.split('.').pop().toLowerCase();
        const size = getFileSize(fileName);

        const typeMap = {
            'cs': { label: 'C# 소스 파일', icon: 'file-cs', iconClass: 'ri-code-s-slash-fill' },
            'xaml': { label: 'XAML 마크업', icon: 'file-xaml', iconClass: 'ri-code-box-fill' },
            'json': { label: 'JSON 파일', icon: 'file-json', iconClass: 'ri-braces-fill' },
            'md': { label: 'Markdown 문서', icon: 'file-md', iconClass: 'ri-markdown-fill' },
            'sln': { label: 'Visual Studio 솔루션', icon: 'file-sln', iconClass: 'ri-folder-settings-fill' },
            'csproj': { label: 'C# 프로젝트', icon: 'file-sln', iconClass: 'ri-folder-settings-fill' },
            'png': { label: 'PNG 이미지', icon: 'file-md', iconClass: 'ri-image-fill' },
        };
        const info = typeMap[ext] || { label: '파일', icon: 'file-cs', iconClass: 'ri-file-fill' };

        const isImage = ['png', 'jpg', 'jpeg', 'gif', 'svg', 'bmp'].includes(ext);
        const isCode = ['cs', 'xaml', 'json', 'js', 'ts', 'py', 'css', 'html'].includes(ext);

        let previewContent = '';
        if (isImage) {
            previewContent = `<div class="ql-preview-image"><i class="ri-image-2-fill"></i></div>`;
        } else if (isCode) {
            previewContent = `
            <div class="ql-preview-code">
                <pre><code><span class="kw">namespace</span> Span.Models;

<span class="kw">public class</span> <span class="type">${fileName.replace('.' + ext, '')}</span>
{
    <span class="kw">public</span> <span class="type">string</span> Name { <span class="kw">get</span>; <span class="kw">set</span>; }
    <span class="kw">public</span> <span class="type">string</span> Path { <span class="kw">get</span>; <span class="kw">set</span>; }
    <span class="kw">public</span> <span class="type">long</span> Size { <span class="kw">get</span>; <span class="kw">set</span>; }
    <span class="kw">public</span> <span class="type">DateTime</span> Modified { <span class="kw">get</span>; }
}</code></pre>
            </div>`;
        } else {
            previewContent = `<div class="ql-preview-image" style="font-size:32px; padding:60px;"><i class="ri-file-text-fill"></i></div>`;
        }

        const overlay = document.createElement('div');
        overlay.className = 'quicklook-overlay';
        overlay.id = 'quicklookOverlay';
        overlay.innerHTML = `
            <div class="quicklook-card">
                <div class="quicklook-header">
                    <i class="${info.iconClass} ${info.icon}"></i>
                    <span class="ql-title">${fileName}</span>
                    <button class="ql-close" title="닫기"><i class="ri-close-line"></i></button>
                </div>
                <div class="quicklook-body">
                    <div class="ql-meta">
                        <span class="ql-meta-label">형식</span><span class="ql-meta-value">${info.label}</span>
                        <span class="ql-meta-label">크기</span><span class="ql-meta-value">${size}</span>
                        <span class="ql-meta-label">수정한 날짜</span><span class="ql-meta-value">2026년 2월 10일 오후 3:42</span>
                        <span class="ql-meta-label">만든 날짜</span><span class="ql-meta-value">2026년 2월 8일 오전 10:15</span>
                        <span class="ql-meta-label">경로</span><span class="ql-meta-value">C:\Users\Dev\Span\Source\</span>
                    </div>
                    ${previewContent}
                </div>
                <div class="quicklook-footer">
                    <div class="ql-hint"><kbd>Esc</kbd> 닫기</div>
                    <div class="ql-hint"><kbd>Space</kbd> 닫기</div>
                </div>
            </div>
        `;

        document.body.appendChild(overlay);

        // Close handlers
        overlay.querySelector('.ql-close').addEventListener('click', () => overlay.remove());
        overlay.addEventListener('click', (e) => {
            if (e.target === overlay) overlay.remove();
        });
    }

    function closeQuickLook() {
        const ql = document.getElementById('quicklookOverlay');
        if (ql) ql.remove();
    }

    // ===================== UNDO TOAST =====================
    function showUndoToast(message) {
        // Remove existing
        document.querySelectorAll('.undo-toast').forEach(t => t.remove());

        const toast = document.createElement('div');
        toast.className = 'undo-toast';
        toast.innerHTML = `
            <i class="ri-checkbox-circle-fill"></i>
            <span>${message}</span>
            <button class="undo-btn">Ctrl+Z 되돌리기</button>
        `;
        document.body.appendChild(toast);

        toast.querySelector('.undo-btn').addEventListener('click', () => {
            toast.classList.add('hiding');
            setTimeout(() => toast.remove(), 200);
        });

        // Auto-dismiss after 4s
        setTimeout(() => {
            if (toast.parentElement) {
                toast.classList.add('hiding');
                setTimeout(() => toast.remove(), 200);
            }
        }, 4000);
    }

    // ===================== FILE CONFLICT DIALOG =====================
    function showConflictDialog() {
        const overlay = document.createElement('div');
        overlay.className = 'conflict-overlay';
        overlay.innerHTML = `
            <div class="conflict-dialog">
                <div class="conflict-header">
                    <i class="ri-error-warning-fill"></i>
                    <div>
                        <h3>파일이 이미 존재합니다</h3>
                        <p>대상 폴더에 동일한 이름의 파일이 있습니다.</p>
                    </div>
                </div>
                <div class="conflict-files">
                    <div class="conflict-file">
                        <div class="conflict-file-label">원본 (소스)</div>
                        <div class="conflict-file-name">FileItem.cs</div>
                        <div class="conflict-file-meta">2.4 KB · 2026-02-10 15:42</div>
                    </div>
                    <div class="conflict-file">
                        <div class="conflict-file-label">대상 (기존)</div>
                        <div class="conflict-file-name">FileItem.cs</div>
                        <div class="conflict-file-meta">1.8 KB · 2026-02-08 10:15</div>
                    </div>
                </div>
                <div class="conflict-actions">
                    <button class="conflict-btn primary" data-action="replace">
                        <i class="ri-refresh-line"></i>덮어쓰기 (Replace)
                        <span class="btn-desc">기존 파일을 대체</span>
                    </button>
                    <button class="conflict-btn" data-action="skip">
                        <i class="ri-skip-forward-line"></i>건너뛰기 (Skip)
                        <span class="btn-desc">이 파일 무시</span>
                    </button>
                    <button class="conflict-btn" data-action="keep-both">
                        <i class="ri-file-copy-2-line"></i>둘 다 유지 (Keep Both)
                        <span class="btn-desc">FileItem (1).cs</span>
                    </button>
                    <label class="conflict-checkbox">
                        <input type="checkbox"> 나머지 항목에 모두 적용
                    </label>
                </div>
            </div>
        `;
        document.body.appendChild(overlay);

        // Close on any button click
        overlay.querySelectorAll('.conflict-btn').forEach(btn => {
            btn.addEventListener('click', () => overlay.remove());
        });
    }

    // ===================== PROGRESS FLYOUT =====================
    function showProgressFlyout() {
        // Remove existing
        document.querySelectorAll('.progress-flyout').forEach(f => f.remove());

        const flyout = document.createElement('div');
        flyout.className = 'progress-flyout';
        flyout.innerHTML = `
            <div class="progress-header">
                <div class="progress-header-left">
                    <i class="ri-file-transfer-line"></i>
                    파일 복사 중…
                </div>
                <button class="progress-close" title="닫기"><i class="ri-close-line"></i></button>
            </div>
            <div class="progress-bar-container">
                <div class="progress-bar-track">
                    <div class="progress-bar-fill" id="progressFill"></div>
                </div>
            </div>
            <div class="progress-details">
                <div class="progress-file-name">MillerColumnViewModel.cs</div>
                <div class="progress-stats">
                    <span>67% · 24/36 파일</span>
                    <span>12.4 MB/s</span>
                    <span>남은 시간: ~8초</span>
                </div>
            </div>
            <div class="progress-controls">
                <button class="progress-ctrl-btn"><i class="ri-pause-line"></i>일시정지</button>
                <button class="progress-ctrl-btn cancel"><i class="ri-close-circle-line"></i>취소</button>
            </div>
        `;
        document.body.appendChild(flyout);

        flyout.querySelector('.progress-close').addEventListener('click', () => flyout.remove());
        flyout.querySelector('.cancel').addEventListener('click', () => flyout.remove());

        // Simulate progress
        let progress = 67;
        const fill = flyout.querySelector('#progressFill');
        const interval = setInterval(() => {
            progress += Math.random() * 5;
            if (progress >= 100) {
                progress = 100;
                clearInterval(interval);
                setTimeout(() => {
                    if (flyout.parentElement) flyout.remove();
                    showUndoToast('36개 파일 복사 완료');
                }, 600);
            }
            fill.style.width = progress + '%';
        }, 400);
    }

    // ===================== KEYBOARD SHORTCUTS =====================
    document.addEventListener('keydown', (e) => {
        // Ctrl+T: New tab
        if (e.ctrlKey && e.key === 't') {
            e.preventDefault();
            document.querySelector('.tab-new').click();
        }
        // Ctrl+W: Close tab
        if (e.ctrlKey && e.key === 'w') {
            e.preventDefault();
            const active = tabBar.querySelector('.tab.active .tab-close');
            if (active) active.click();
        }
        // Ctrl+F: Focus search
        if (e.ctrlKey && e.key === 'f') {
            e.preventDefault();
            document.querySelector('.search-box input').focus();
        }
        // Ctrl+L: Address bar input mode
        if (e.ctrlKey && e.key === 'l') {
            e.preventDefault();
            enterAddressInputMode();
        }
        // F2: Inline rename
        if (e.key === 'F2') {
            e.preventDefault();
            startInlineRename();
        }
        // Space: Quick Look
        if (e.key === ' ' && !e.ctrlKey && !e.altKey) {
            // Only if not in an input field
            if (document.activeElement.tagName === 'INPUT' || document.activeElement.tagName === 'TEXTAREA') return;
            e.preventDefault();
            const ql = document.getElementById('quicklookOverlay');
            if (ql) {
                closeQuickLook();
            } else {
                showQuickLook();
            }
        }
        // Escape: Close Quick Look
        if (e.key === 'Escape') {
            closeQuickLook();
            exitAddressInputMode();
        }
        // Delete: Show undo toast (demo)
        if (e.key === 'Delete') {
            const selected = document.querySelector('.miller-item.selected, .miller-item.multi-selected');
            if (selected) {
                e.preventDefault();
                const multiCount = document.querySelectorAll('.miller-item.multi-selected').length;
                const name = selected.querySelector('span')?.textContent || '항목';
                if (multiCount > 1) {
                    showUndoToast(`${multiCount}개 항목을 휴지통으로 이동했습니다`);
                } else {
                    showUndoToast(`"${name}"을(를) 휴지통으로 이동했습니다`);
                }
            }
        }
        // Ctrl+Shift+V: Show conflict dialog (demo trigger)
        if (e.ctrlKey && e.shiftKey && e.key === 'V') {
            e.preventDefault();
            showConflictDialog();
        }
        // Ctrl+Shift+C: Show progress flyout (demo trigger)
        if (e.ctrlKey && e.shiftKey && e.key === 'C') {
            e.preventDefault();
            showProgressFlyout();
        }
    });

    // ===================== INSPECT PANE TOGGLE =====================
    const inspectPane = document.getElementById('inspectPane');
    const inspectToggle = document.getElementById('inspectToggle');
    const inspectClose = document.getElementById('inspectClose');

    function toggleInspectPane() {
        if (!inspectPane) return;
        const isHidden = inspectPane.classList.toggle('hidden');
        if (inspectToggle) inspectToggle.classList.toggle('active', !isHidden);
    }

    if (inspectToggle) inspectToggle.addEventListener('click', toggleInspectPane);
    if (inspectClose) inspectClose.addEventListener('click', () => {
        inspectPane.classList.add('hidden');
        if (inspectToggle) inspectToggle.classList.remove('active');
    });

    // Ctrl+Shift+P: toggle inspect pane
    document.addEventListener('keydown', (e) => {
        if (e.ctrlKey && e.shiftKey && e.key === 'P') {
            e.preventDefault();
            toggleInspectPane();
        }
    });

});
