using Microsoft.UI.Xaml.Controls;
using Span.ViewModels;

namespace Span.Views.Controls;

public sealed partial class FileOperationProgressControl : UserControl
{
    public FileOperationProgressViewModel ViewModel { get; set; }

    public FileOperationProgressControl()
    {
        ViewModel = new FileOperationProgressViewModel();
        this.InitializeComponent();
    }

    public FileOperationProgressControl(FileOperationProgressViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
    }
}
