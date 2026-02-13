using Microsoft.UI.Xaml.Controls;
using Span.Services.FileOperations;
using Span.ViewModels;
using System;

namespace Span.Views.Dialogs;

public sealed partial class FileConflictDialog : ContentDialog
{
    public FileConflictDialogViewModel ViewModel { get; }

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
        ViewModel = new FileConflictDialogViewModel();
        this.InitializeComponent();
    }

    public FileConflictDialog(FileConflictDialogViewModel viewModel)
    {
        ViewModel = viewModel;
        SyncRadioButtonsFromViewModel();
        this.InitializeComponent();
    }

    private void SyncRadioButtonsFromViewModel()
    {
        _isReplace = ViewModel.SelectedResolution == ConflictResolution.Replace;
        _isKeepBoth = ViewModel.SelectedResolution == ConflictResolution.KeepBoth;
        _isSkip = ViewModel.SelectedResolution == ConflictResolution.Skip;
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
