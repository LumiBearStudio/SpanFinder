using CommunityToolkit.Mvvm.ComponentModel;
using Span.ViewModels;

namespace Span.Models
{
    public partial class TabItem : ObservableObject
    {
        /// <summary>
        /// 탭 전용 ExplorerViewModel 인스턴스 — 탭 전환 시 참조 교체로 즉시 복원.
        /// XAML 바인딩 불필요, 직렬화 불가이므로 일반 프로퍼티.
        /// </summary>
        public ExplorerViewModel? Explorer { get; set; }
        [ObservableProperty]
        private string _header = "Home";

        [ObservableProperty]
        private string _icon = "\uE80F"; // Segoe Fluent Icons Home glyph

        [ObservableProperty]
        private string _path = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsHomeModeVisible))]
        [NotifyPropertyChangedFor(nameof(IsNotHomeModeVisible))]
        [NotifyPropertyChangedFor(nameof(IsSettingsModeVisible))]
        private ViewMode _viewMode = ViewMode.Home;

        [ObservableProperty]
        private ViewMode _iconSize = ViewMode.IconMedium;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsActiveVisible))]
        [NotifyPropertyChangedFor(nameof(IsInactiveVisible))]
        private bool _isActive = false;

        public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];

        // Computed visibility properties for XAML binding
        public Microsoft.UI.Xaml.Visibility IsHomeModeVisible
            => ViewMode == ViewMode.Home ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsNotHomeModeVisible
            => (ViewMode != ViewMode.Home && ViewMode != ViewMode.Settings) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsSettingsModeVisible
            => ViewMode == ViewMode.Settings ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsActiveVisible
            => IsActive ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsInactiveVisible
            => IsActive ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    }

    /// <summary>
    /// Lightweight DTO for JSON serialization of tab state.
    /// </summary>
    public record TabStateDto(string Id, string Header, string Path, int ViewMode, int IconSize);
}
