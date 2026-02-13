using System;
using CommunityToolkit.Mvvm.ComponentModel;
using Span.Services.FileOperations;

namespace Span.ViewModels
{
    /// <summary>
    /// ViewModel for the file conflict resolution dialog.
    /// Presents source/destination file information and allows the user
    /// to choose a resolution strategy (Replace, Skip, KeepBoth).
    /// </summary>
    public partial class FileConflictDialogViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _sourcePath = string.Empty;

        [ObservableProperty]
        private string _destinationPath = string.Empty;

        [ObservableProperty]
        private ConflictResolution _selectedResolution = ConflictResolution.KeepBoth;

        [ObservableProperty]
        private bool _applyToAll = false;

        public long SourceSize { get; set; }
        public DateTime SourceModified { get; set; }
        public long DestinationSize { get; set; }
        public DateTime DestinationModified { get; set; }
    }
}
