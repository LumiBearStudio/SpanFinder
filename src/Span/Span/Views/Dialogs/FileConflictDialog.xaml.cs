using Microsoft.UI.Xaml.Controls;
using Span.Services;
using Span.Services.FileOperations;
using Span.ViewModels;
using System;

namespace Span.Views.Dialogs;

public sealed partial class FileConflictDialog : ContentDialog
{
    public FileConflictDialogViewModel ViewModel { get; }

    private readonly LocalizationService _loc;

    private bool _isReplace;
    private bool _isKeepBoth = true;
    private bool _isSkip;

    public bool IsReplace
    {
        get => _isReplace;
        set
        {
            if (_isReplace != value)
            {
                _isReplace = value;
                if (value) ViewModel.SelectedResolution = ConflictResolution.Replace;
            }
        }
    }

    public bool IsKeepBoth
    {
        get => _isKeepBoth;
        set
        {
            if (_isKeepBoth != value)
            {
                _isKeepBoth = value;
                if (value) ViewModel.SelectedResolution = ConflictResolution.KeepBoth;
            }
        }
    }

    public bool IsSkip
    {
        get => _isSkip;
        set
        {
            if (_isSkip != value)
            {
                _isSkip = value;
                if (value) ViewModel.SelectedResolution = ConflictResolution.Skip;
            }
        }
    }

    public FileConflictDialog()
    {
        _loc = (App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService)!;
        ViewModel = new FileConflictDialogViewModel();
        this.InitializeComponent();
        this.Title = _loc.Get("FileAlreadyExists");
        this.PrimaryButtonText = _loc.Get("OK");
        this.CloseButtonText = _loc.Get("Cancel");
        LocalizeUI();
    }

    public FileConflictDialog(FileConflictDialogViewModel viewModel)
    {
        _loc = (App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService)!;
        ViewModel = viewModel;
        SyncRadioButtonsFromViewModel();
        this.InitializeComponent();
        this.Title = _loc.Get("FileAlreadyExists");
        this.PrimaryButtonText = _loc.Get("OK");
        this.CloseButtonText = _loc.Get("Cancel");
        LocalizeUI();
    }

    private void SyncRadioButtonsFromViewModel()
    {
        _isReplace = ViewModel.SelectedResolution == ConflictResolution.Replace;
        _isKeepBoth = ViewModel.SelectedResolution == ConflictResolution.KeepBoth;
        _isSkip = ViewModel.SelectedResolution == ConflictResolution.Skip;
    }

    private void LocalizeUI()
    {
        ConflictMessageRun.Text = _loc.Get("FileConflictMessage");
        SourceFileHeader.Text = _loc.Get("SourceFile");
        ExistingFileHeader.Text = _loc.Get("ExistingFile");
        SourceSizeLabel.Text = _loc.Get("FileSize");
        SourceModifiedLabel.Text = _loc.Get("FileModified");
        DestSizeLabel.Text = _loc.Get("FileSize");
        DestModifiedLabel.Text = _loc.Get("FileModified");
        ChooseActionText.Text = _loc.Get("ChooseAction");
        ReplaceText.Text = _loc.Get("ReplaceFile");
        ReplaceDesc.Text = _loc.Get("ReplaceFileDesc");
        KeepBothText.Text = _loc.Get("KeepBothFiles");
        KeepBothDesc.Text = _loc.Get("KeepBothFilesDesc");
        SkipText.Text = _loc.Get("SkipFile");
        SkipDesc.Text = _loc.Get("SkipFileDesc");
        ApplyToAllCheckbox.Content = _loc.Get("ApplyToAllConflicts");
    }

    public string FormatFileSize(long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";
        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024):F1} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
    }

    public string FormatDateTime(DateTime dateTime)
    {
        return dateTime.ToString("yyyy-MM-dd HH:mm:ss");
    }
}
