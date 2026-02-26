using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Span.Services;
using Span.ViewModels;
using System.Collections.Generic;

namespace Span.Views.Dialogs
{
    public sealed partial class BatchRenameDialog : ContentDialog
    {
        private readonly BatchRenameViewModel _viewModel;
        private readonly LocalizationService? _loc;

        public BatchRenameDialog(List<FileSystemViewModel> items)
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            this.InitializeComponent();
            _viewModel = new BatchRenameViewModel(items);
            PreviewListView.ItemsSource = _viewModel.PreviewItems;
            // XAML에서 {name} 패턴이 바인딩으로 해석되므로 코드비하인드에서 설정
            PatternTextBox.PlaceholderText = "{name}_{n}";
            LocalizeUI();
            UpdatePreviewAndValidate();
        }

        public List<(string OldPath, string NewName)> GetRenameList() => _viewModel.GetRenameList();

        private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _viewModel.SelectedMode = (BatchRenameViewModel.RenameMode)ModePivot.SelectedIndex;
            SyncUIToViewModel();
            UpdatePreviewAndValidate();
        }

        private void OnInputChanged(object sender, TextChangedEventArgs e)
        {
            SyncUIToViewModel();
            UpdatePreviewAndValidate();
        }

        private void OnOptionChanged(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
        {
            SyncUIToViewModel();
            UpdatePreviewAndValidate();
        }

        private void OnNumberChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
        {
            SyncUIToViewModel();
            UpdatePreviewAndValidate();
        }

        private void SyncUIToViewModel()
        {
            if (_viewModel == null) return;
            _viewModel.FindText = FindTextBox?.Text ?? "";
            _viewModel.ReplaceText = ReplaceTextBox?.Text ?? "";
            _viewModel.CaseSensitive = CaseSensitiveCheck?.IsChecked == true;
            _viewModel.UseRegex = UseRegexCheck?.IsChecked == true;

            _viewModel.Prefix = PrefixTextBox?.Text ?? "";
            _viewModel.Suffix = SuffixTextBox?.Text ?? "";

            _viewModel.NumberingPattern = PatternTextBox?.Text ?? "{name}_{n}";
            _viewModel.StartNumber = (int)(StartNumberBox?.Value ?? 1);
            _viewModel.Increment = (int)(IncrementBox?.Value ?? 1);
            _viewModel.Digits = (int)(DigitsBox?.Value ?? 2);
        }

        private void LocalizeUI()
        {
            if (_loc == null) return;

            // Dialog title & buttons
            this.Title = _loc.Get("BatchRename_Title");
            this.PrimaryButtonText = _loc.Get("BatchRename_Rename");
            this.CloseButtonText = _loc.Get("Cancel");

            // Pivot headers
            PivotFindReplace.Header = _loc.Get("BatchRename_FindReplace");
            PivotPrefixSuffix.Header = _loc.Get("BatchRename_PrefixSuffix");
            PivotNumbering.Header = _loc.Get("BatchRename_Numbering");

            // Find/Replace tab
            FindTextBox.Header = _loc.Get("BatchRename_Find");
            FindTextBox.PlaceholderText = _loc.Get("BatchRename_FindPlaceholder");
            ReplaceTextBox.Header = _loc.Get("BatchRename_Replace");
            ReplaceTextBox.PlaceholderText = _loc.Get("BatchRename_ReplacePlaceholder");
            CaseSensitiveCheck.Content = _loc.Get("BatchRename_CaseSensitive");
            UseRegexCheck.Content = _loc.Get("BatchRename_UseRegex");

            // Prefix/Suffix tab
            PrefixTextBox.Header = _loc.Get("BatchRename_Prefix");
            PrefixTextBox.PlaceholderText = _loc.Get("BatchRename_PrefixPlaceholder");
            SuffixTextBox.Header = _loc.Get("BatchRename_Suffix");
            SuffixTextBox.PlaceholderText = _loc.Get("BatchRename_SuffixPlaceholder");

            // Numbering tab
            PatternTextBox.Header = _loc.Get("BatchRename_Pattern");
            PatternHintText.Inlines.Clear();
            PatternHintText.Inlines.Add(new Run { Text = _loc.Get("BatchRename_PatternHint") });
            StartNumberBox.Header = _loc.Get("BatchRename_StartNumber");
            IncrementBox.Header = _loc.Get("BatchRename_Increment");
            DigitsBox.Header = _loc.Get("BatchRename_Digits");

            // Preview
            PreviewHeader.Text = _loc.Get("BatchRename_Preview");

            // Conflict warning
            ConflictInfoBar.Title = _loc.Get("BatchRename_ConflictTitle");
            ConflictInfoBar.Message = _loc.Get("BatchRename_ConflictMessage");
        }

        private void UpdatePreviewAndValidate()
        {
            if (_viewModel == null) return;
            _viewModel.UpdatePreview();

            ConflictInfoBar.IsOpen = _viewModel.HasConflicts;
            IsPrimaryButtonEnabled = _viewModel.HasChanges && !_viewModel.HasConflicts;
        }
    }
}
