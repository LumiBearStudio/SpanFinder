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
        [NotifyPropertyChangedFor(nameof(IsActionLogModeVisible))]
        private ViewMode _viewMode = ViewMode.Home;

        [ObservableProperty]
        private ViewMode _iconSize = ViewMode.IconMedium;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsActiveVisible))]
        [NotifyPropertyChangedFor(nameof(IsInactiveVisible))]
        private bool _isActive = false;

        public string Id { get; set; } = System.Guid.NewGuid().ToString("N")[..8];

        /// <summary>
        /// Home 모드 탭이 탐색기로 전환될 때 사용할 뷰모드.
        /// 시작 설정의 "시작 뷰모드"를 저장해두고, 드라이브 클릭 시 이 값으로 전환.
        /// null이면 기본값(MillerColumns) 사용.
        /// </summary>
        public ViewMode? PreferredViewMode { get; set; }

        /// <summary>
        /// 이 탭에서 분할뷰가 활성화되어 있었는지 여부.
        /// 탭 전환 시 저장/복원되어 탭별 독립적인 분할뷰 상태를 유지.
        /// </summary>
        public bool IsSplitEnabled { get; set; }

        /// <summary>
        /// 이 탭의 분할뷰 우측 패인 뷰모드.
        /// 탭 전환 시 저장/복원.
        /// </summary>
        public ViewMode SplitRightViewMode { get; set; } = ViewMode.MillerColumns;

        // Computed visibility properties for XAML binding
        public Microsoft.UI.Xaml.Visibility IsHomeModeVisible
            => ViewMode == ViewMode.Home ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsNotHomeModeVisible
            => (ViewMode != ViewMode.Home && ViewMode != ViewMode.Settings && ViewMode != ViewMode.ActionLog) ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsSettingsModeVisible
            => ViewMode == ViewMode.Settings ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public Microsoft.UI.Xaml.Visibility IsActionLogModeVisible
            => ViewMode == ViewMode.ActionLog ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

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
