using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Span.Services;
using Span.ViewModels;

namespace Span.Views.Controls;

/// <summary>
/// 파일 작업 진행률 표시 UserControl.
/// FileOperationManager의 활성 작업 목록을 실시간으로 표시하며,
/// 개별 작업의 일시중지/재개 및 취소 기능을 제공한다.
/// </summary>
public sealed partial class FileOperationProgressControl : UserControl
{
    public FileOperationProgressViewModel ViewModel { get; set; }

    private LocalizationService? _loc;
    private FileOperationManager? _boundManager;

    public FileOperationProgressControl()
    {
        ViewModel = new FileOperationProgressViewModel();
        this.InitializeComponent();
        this.Loaded += (s, e) =>
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            LocalizeUI();
            if (_loc != null) _loc.LanguageChanged += LocalizeUI;
        };
        this.Unloaded += (s, e) =>
        {
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            if (_boundManager != null)
                _boundManager.ActiveOperations.CollectionChanged -= OnActiveOperationsChanged;
        };
    }

    public FileOperationProgressControl(FileOperationProgressViewModel viewModel)
    {
        ViewModel = viewModel;
        this.InitializeComponent();
        this.Loaded += (s, e) =>
        {
            _loc = App.Current.Services.GetService(typeof(LocalizationService)) as LocalizationService;
            LocalizeUI();
            if (_loc != null) _loc.LanguageChanged += LocalizeUI;
        };
        this.Unloaded += (s, e) =>
        {
            if (_loc != null) _loc.LanguageChanged -= LocalizeUI;
            if (_boundManager != null)
                _boundManager.ActiveOperations.CollectionChanged -= OnActiveOperationsChanged;
        };
    }

    /// <summary>
    /// Sets the FileOperationManager and binds the operations list.
    /// Call this after the control is created and the manager is available.
    /// </summary>
    public void SetOperationManager(FileOperationManager manager)
    {
        // Unsubscribe from previous manager if any
        if (_boundManager != null)
            _boundManager.ActiveOperations.CollectionChanged -= OnActiveOperationsChanged;

        _boundManager = manager;
        ViewModel.OperationManager = manager;
        OperationsList.ItemsSource = manager.ActiveOperations;

        // Show/hide based on whether there are active operations
        manager.ActiveOperations.CollectionChanged += OnActiveOperationsChanged;

        // Initially hidden
        MultiOperationPanel.Visibility = Visibility.Collapsed;
    }

    private void OnActiveOperationsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            MultiOperationPanel.Visibility = _boundManager?.ActiveOperations.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            // 새로 추가된 항목에 로컬라이즈된 텍스트 적용
            if (e.NewItems != null && _loc != null)
            {
                foreach (var item in e.NewItems)
                {
                    if (item is FileOperationEntry entry)
                    {
                        entry._cancellingText = _loc.Get("Cancelling");
                    }
                }
            }

            UpdateCancelAllButtonState();
        });
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
            // 취소 후 CancelAll 버튼 상태 업데이트
            UpdateCancelAllButtonState();
        }
    }

    /// <summary>
    /// 모든 작업이 Cancelling 상태이면 CancelAll 버튼 비활성화
    /// </summary>
    private void UpdateCancelAllButtonState()
    {
        if (_boundManager == null) return;
        bool hasCancel = false;
        foreach (var op in _boundManager.ActiveOperations)
        {
            if (op.CanCancel)
            {
                hasCancel = true;
                break;
            }
        }
        CancelAllButton.IsEnabled = hasCancel;
    }

    private void LocalizeUI()
    {
        if (_loc == null) return;
        HeaderText.Text = _loc.Get("FileOperations");
        CancelAllButton.Content = _loc.Get("CancelAll");
    }
}
