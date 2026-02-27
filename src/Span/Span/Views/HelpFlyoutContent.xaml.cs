using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;

namespace Span.Views
{
    /// <summary>
    /// 키보드 단축키 도움말 Flyout UserControl.
    /// 탐색, 편집, 선택, 뷰, 윈도우/탭 카테고리별 단축키 목록을 표시한다.
    /// 다국어 UI를 지원한다.
    /// </summary>
    public sealed partial class HelpFlyoutContent : UserControl
    {
        private readonly LocalizationService? _loc;

        public HelpFlyoutContent()
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            this.InitializeComponent();
            LocalizeUI();

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

            // Window / Tab
            DescNewTab.Text = _loc.Get("Help_NewTab");
            DescCloseTab.Text = _loc.Get("Help_CloseTab");
            DescNewWindow.Text = _loc.Get("Help_NewWindow");
            DescOpenTerminal.Text = _loc.Get("Help_OpenTerminal");
            DescSettings.Text = _loc.Get("Help_Settings");
            DescProperties.Text = _loc.Get("Help_Properties");
            DescHelp.Text = _loc.Get("Help_Help");

            // Footer
            FooterHint.Text = _loc.Get("Help_CloseHint");
        }
    }
}
