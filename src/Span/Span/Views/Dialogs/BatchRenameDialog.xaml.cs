using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;
using System.Collections.Generic;

namespace Span.Views.Dialogs
{
    public sealed partial class BatchRenameDialog : ContentDialog
    {
        private readonly BatchRenameViewModel _viewModel;

        public BatchRenameDialog(List<FileSystemViewModel> items)
        {
            this.InitializeComponent();
            _viewModel = new BatchRenameViewModel(items);
            PreviewListView.ItemsSource = _viewModel.PreviewItems;
            // XAML에서 {name} 패턴이 바인딩으로 해석되므로 코드비하인드에서 설정
            PatternTextBox.PlaceholderText = "{name}_{n}";
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

        private void UpdatePreviewAndValidate()
        {
            if (_viewModel == null) return;
            _viewModel.UpdatePreview();

            ConflictInfoBar.IsOpen = _viewModel.HasConflicts;
            IsPrimaryButtonEnabled = _viewModel.HasChanges && !_viewModel.HasConflicts;
        }
    }
}
