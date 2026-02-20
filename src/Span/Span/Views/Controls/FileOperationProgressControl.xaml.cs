using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
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

    /// <summary>
    /// Sets the FileOperationManager and binds the operations list.
    /// Call this after the control is created and the manager is available.
    /// </summary>
    public void SetOperationManager(FileOperationManager manager)
    {
        ViewModel.OperationManager = manager;
        OperationsList.ItemsSource = manager.ActiveOperations;

        // Show/hide based on whether there are active operations
        manager.ActiveOperations.CollectionChanged += (s, e) =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                MultiOperationPanel.Visibility = manager.ActiveOperations.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            });
        };

        // Initially hidden
        MultiOperationPanel.Visibility = Visibility.Collapsed;
    }

    private void OnPauseResumeClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int operationId)
        {
            ViewModel.OperationManager?.TogglePause(operationId);
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is int operationId)
        {
            ViewModel.OperationManager?.CancelOperation(operationId);
        }
    }
}
