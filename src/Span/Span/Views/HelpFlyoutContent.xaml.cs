using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Extensions.DependencyInjection;
using Span.Services;
using System.Collections.Generic;

namespace Span.Views
{
    /// <summary>
    /// 키보드 단축키 도움말 Flyout UserControl.
    /// 탐색, 편집, 선택, 뷰, 윈도우/탭 카테고리별 단축키 목록을 표시한다.
    /// 다국어 UI를 지원하며, KeyBindingService에서 현재 바인딩을 읽어 키 텍스트를 동적 갱신한다.
    /// </summary>
    public sealed partial class HelpFlyoutContent : UserControl
    {
        private readonly LocalizationService? _loc;

        public HelpFlyoutContent()
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            this.InitializeComponent();
            LocalizeUI();
            UpdateKeyTexts();

            this.Loaded += (s, e) =>
            {
                if (_loc != null) _loc.LanguageChanged += LocalizeUI;
            };
            this.Unloaded += (s, e) =>
            {
                if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            };
        }

        private void LocalizeUI()
        {
            if (_loc == null) return;
            HelpTitleText.Text = _loc.Get("Help_Title");

            // Category headers
            NavHeader.Text = _loc.Get("Help_Navigation");
            EditHeader.Text = _loc.Get("Help_Edit");
            SelectionHeader.Text = _loc.Get("Help_Selection");
            ViewHeader.Text = _loc.Get("Help_View");
            WindowTabHeader.Text = _loc.Get("Help_WindowTab");

            // Navigation
            DescColumnNav.Text = _loc.Get("Help_ColumnNav");
            DescOpenFolder.Text = _loc.Get("Help_OpenFolder");
            DescParentFolder.Text = _loc.Get("Help_ParentFolder");
            DescHomeEnd.Text = _loc.Get("Help_HomeEnd");
            DescBackForward.Text = _loc.Get("Help_BackForward");
            DescAddressBar.Text = _loc.Get("Help_AddressBar");
            DescSearch.Text = _loc.Get("Help_Search");
            DescFilter.Text = _loc.Get("Help_Filter");
            DescQuickLook.Text = _loc.Get("Help_QuickLook");

            // Edit
            DescCopy.Text = _loc.Get("Help_Copy");
            DescCut.Text = _loc.Get("Help_Cut");
            DescPaste.Text = _loc.Get("Help_Paste");
            DescPasteShortcut.Text = _loc.Get("Help_PasteShortcut");
            DescDuplicate.Text = _loc.Get("Help_Duplicate");
            DescRename.Text = _loc.Get("Help_Rename");
            DescDelete.Text = _loc.Get("Help_DeleteTrash");
            DescPermDelete.Text = _loc.Get("Help_PermanentDelete");
            DescNewFolder.Text = _loc.Get("Help_NewFolder");
            DescUndoRedo.Text = _loc.Get("Help_UndoRedo");

            // Selection
            DescSelectAll.Text = _loc.Get("Help_SelectAll");
            DescDeselectAll.Text = _loc.Get("Help_DeselectAll");
            DescInvertSel.Text = _loc.Get("Help_InvertSelection");

            // View
            DescMillerCol.Text = _loc.Get("Help_MillerColumns");
            DescDetailList.Text = _loc.Get("Help_DetailList");
            DescListView.Text = _loc.Get("Help_ListView");
            DescIcons.Text = _loc.Get("Help_Icons");
            DescSplitView.Text = _loc.Get("Help_SplitView");
            DescPreviewPanel.Text = _loc.Get("Help_PreviewPanel");
            DescSwitchPanel.Text = _loc.Get("Help_SwitchPanel");
            DescEqColumns.Text = _loc.Get("Help_EqualizeColumns");
            DescAutoFit.Text = _loc.Get("Help_AutoFitColumns");
            DescRefresh.Text = _loc.Get("Help_Refresh");
            DescToggleHidden.Text = _loc.Get("Help_ToggleHidden");
            DescFullscreen.Text = _loc.Get("Help_Fullscreen");

            // Window / Tab
            DescNewTab.Text = _loc.Get("Help_NewTab");
            DescCloseTab.Text = _loc.Get("Help_CloseTab");
            DescOpenInNewTab.Text = _loc.Get("Help_OpenInNewTab");
            DescNewWindow.Text = _loc.Get("Help_NewWindow");
            DescOpenTerminal.Text = _loc.Get("Help_OpenTerminal");
            DescSettings.Text = _loc.Get("Help_Settings");
            DescProperties.Text = _loc.Get("Help_Properties");
            DescHelp.Text = _loc.Get("Help_Help");

            // Footer
            FooterHint.Text = _loc.Get("Help_CloseHint");
        }

        /// <summary>
        /// KeyBindingService에서 현재 바인딩을 읽어 키 텍스트를 동적 갱신.
        /// </summary>
        public void UpdateKeyTexts()
        {
            var service = App.Current.Services.GetService<KeyBindingService>();
            if (service == null) return;
            var bindings = service.CloneCurrentBindings();

            // 탐색
            SetKeyText(Key_AddressBar, bindings, "span.navigate.addressBar");
            SetKeyText(Key_Search, bindings, "span.navigate.search");
            SetKeyText(Key_Filter, bindings, "span.navigate.filterBar");
            SetKeyText(Key_QuickLook, bindings, "span.quickLook.toggle");

            // 편집
            SetKeyText(Key_Copy, bindings, "span.edit.copy");
            SetKeyText(Key_Cut, bindings, "span.edit.cut");
            SetKeyText(Key_Paste, bindings, "span.edit.paste");
            SetKeyText(Key_PasteShortcut, bindings, "span.edit.pasteAsShortcut");
            SetKeyText(Key_Duplicate, bindings, "span.edit.duplicate");
            SetKeyText(Key_Rename, bindings, "span.edit.rename");
            SetKeyText(Key_Delete, bindings, "span.edit.delete");
            SetKeyText(Key_PermDelete, bindings, "span.edit.permanentDelete");
            SetKeyText(Key_NewFolder, bindings, "span.edit.newFolder");

            // 보기
            SetKeyText(Key_Miller, bindings, "span.view.miller");
            SetKeyText(Key_Details, bindings, "span.view.details");
            SetKeyText(Key_List, bindings, "span.view.list");
            SetKeyText(Key_Icons, bindings, "span.view.icon");
            SetKeyText(Key_SplitView, bindings, "span.view.splitView");
            SetKeyText(Key_Preview, bindings, "span.view.preview");
            SetKeyText(Key_EqColumns, bindings, "span.view.equalizeColumns");
            SetKeyText(Key_AutoFit, bindings, "span.view.autoFitColumns");
            SetKeyText(Key_Refresh, bindings, "span.view.refresh");
            SetKeyText(Key_ToggleHidden, bindings, "span.view.toggleHidden");
            SetKeyText(Key_Fullscreen, bindings, "span.view.fullscreen");

            // 창 / 탭
            SetKeyText(Key_NewTab, bindings, "span.tab.new");
            SetKeyText(Key_CloseTab, bindings, "span.tab.close");
            SetKeyText(Key_OpenInNewTab, bindings, "span.tab.openSelectedInNew");
            SetKeyText(Key_NewWindow, bindings, "span.window.new");
            SetKeyText(Key_Terminal, bindings, "span.window.terminal");
            SetKeyText(Key_Settings, bindings, "span.window.settings");
            SetKeyText(Key_Properties, bindings, "span.window.properties");
            SetKeyText(Key_Help, bindings, "span.window.help");

            // 합쳐진 매핑: Back + Forward
            SetJoinedKeyText(Key_BackForward, bindings, "span.navigate.back", "span.navigate.forward");
            // 합쳐진 매핑: Undo + Redo
            SetJoinedKeyText(Key_UndoRedo, bindings, "span.edit.undo", "span.edit.redo");
        }

        private static void SetKeyText(TextBlock? tb, Dictionary<string, List<string>> bindings, string commandId)
        {
            if (tb == null) return;
            if (bindings.TryGetValue(commandId, out var keys) && keys.Count > 0)
                tb.Text = string.Join(", ", keys);
            else
                tb.Text = "\u2014"; // em dash
        }

        private static void SetJoinedKeyText(TextBlock? tb, Dictionary<string, List<string>> bindings,
            string commandId1, string commandId2)
        {
            if (tb == null) return;
            var parts = new List<string>();
            if (bindings.TryGetValue(commandId1, out var keys1) && keys1.Count > 0)
                parts.AddRange(keys1);
            if (bindings.TryGetValue(commandId2, out var keys2) && keys2.Count > 0)
                parts.AddRange(keys2);
            tb.Text = parts.Count > 0 ? string.Join(" / ", parts) : "\u2014";
        }
    }
}
