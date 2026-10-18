using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// 사이드바 즐겨찾기 항목. FavoritesService가 JSON으로 영속화하며,
    /// 드래그 드롭 또는 컨텍스트 메뉴로 추가/제거/순서 변경할 수 있다.
    /// desktop.ini 기반 커스텀 아이콘 lazy 로드를 지원 (Issue #39 a).
    /// </summary>
    public partial class FavoriteItem : ObservableObject
    {
        /// <summary>표시 이름.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>폴더 전체 경로.</summary>
        public string Path { get; set; } = string.Empty;

        /// <summary>아이콘 글리프 문자 (현재 아이콘 팩 기준).</summary>
        public string IconGlyph { get; set; } = string.Empty;

        /// <summary>아이콘 색상 (HEX, 예: "#FFC857").</summary>
        public string IconColor { get; set; } = "#FFFFFF";

        /// <summary>정렬 순서 (드래그로 변경 가능).</summary>
        public int Order { get; set; }

        /// <summary>True when the favorite points to a file (click opens it with the default app).</summary>
        public bool IsFile { get; set; }

        /// <summary>User-defined display name (null = name derived from the path).</summary>
        public string? CustomName { get; set; }

        /// <summary>User-defined icon color (HEX, null = default color for the path).</summary>
        public string? CustomColor { get; set; }

        /// <summary>
        /// desktop.ini 기반 커스텀 아이콘 (lazy 로드). null이면 기본 글리프 표시.
        /// FolderViewModel.CustomIcon과 동일한 패턴.
        /// </summary>
        [ObservableProperty]
        private ImageSource? _customIcon;

        public bool HasCustomIcon => CustomIcon != null;

        public Visibility CustomIconVisibility =>
            CustomIcon != null ? Visibility.Visible : Visibility.Collapsed;

        public Visibility GlyphVisibility =>
            CustomIcon != null ? Visibility.Collapsed : Visibility.Visible;

        partial void OnCustomIconChanged(ImageSource? value)
        {
            OnPropertyChanged(nameof(HasCustomIcon));
            OnPropertyChanged(nameof(CustomIconVisibility));
            OnPropertyChanged(nameof(GlyphVisibility));
        }

        private bool _customIconRequested;

        /// <summary>
        /// 설정 ON일 때 desktop.ini 기반 커스텀 아이콘을 비동기 로드.
        /// 중복 호출 방지 (_customIconRequested 플래그).
        /// </summary>
        public void RequestCustomIconLoad()
        {
            if (_customIconRequested) return;
            if (string.IsNullOrEmpty(Path) || IsFile) return;

            try
            {
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings == null || !settings.FolderCustomIconsEnabled) return;

                var iconSvc = App.Current.Services.GetService(typeof(Services.FolderIconService)) as Services.FolderIconService;
                if (iconSvc == null) return;

                _customIconRequested = true;
                _ = LoadCustomIconAsync(iconSvc);
            }
            catch (System.Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoriteItem.CustomIcon] RequestCustomIconLoad failed for {Path}: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadCustomIconAsync(Services.FolderIconService iconSvc)
        {
            try
            {
                var icon = await iconSvc.GetCustomIconAsync(Path).ConfigureAwait(false);
                if (icon == null) return;

                // 레이스 방지: 로드 중 설정이 OFF로 바뀌었거나 Clear 호출된 경우 무시
                var settings = App.Current.Services.GetService(typeof(Services.SettingsService)) as Services.SettingsService;
                if (settings == null || !settings.FolderCustomIconsEnabled) return;
                if (!_customIconRequested) return;

                var dispatcher = iconSvc.GetUiDispatcher();
                if (dispatcher == null) return;

                var iconToSet = icon;
                dispatcher.TryEnqueue(() =>
                {
                    if (!_customIconRequested) return;
                    CustomIcon = iconToSet;
                });
            }
            catch (System.Exception ex)
            {
                Helpers.DebugLogger.Log($"[FavoriteItem.CustomIcon] LoadCustomIconAsync failed for {Path}: {ex.Message}");
            }
        }

        /// <summary>
        /// 설정 OFF 시 호출되어 캐시된 커스텀 아이콘을 초기화.
        /// </summary>
        public void ClearCustomIcon()
        {
            _customIconRequested = false;
            CustomIcon = null;
        }

        /// <summary>
        /// IconColor를 파싱하여 SolidColorBrush를 반환. XAML 바인딩용.
        /// 파싱 실패 시 흰색 브러시를 반환한다.
        /// </summary>
        public SolidColorBrush IconBrush
        {
            get
            {
                try
                {
                    var hex = IconColor.TrimStart('#');
                    byte r = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
                    byte g = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
                    byte b = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
                    return new SolidColorBrush(ColorHelper.FromArgb(255, r, g, b));
                }
                catch
                {
                    return new SolidColorBrush(Colors.White);
                }
            }
        }
    }

    /// <summary>Persisted favorite (Span-owned list, independent of Windows Quick Access).</summary>
    public class FavoriteEntry
    {
        public string Path { get; set; } = string.Empty;
        public string? Name { get; set; }
        public string? Color { get; set; }
        public bool IsFile { get; set; }
    }

    /// <summary>User-defined sidebar favorites group.</summary>
    public class FavoriteGroup
    {
        public string Id { get; set; } = System.Guid.NewGuid().ToString("N");
        public string Name { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsExpanded { get; set; } = true;
        public List<string> Paths { get; set; } = new();
    }

    /// <summary>Persisted sidebar favorites layout.</summary>
    public class FavoritesLayout
    {
        public List<FavoriteGroup> Groups { get; set; } = new();
        public List<string> UngroupedPaths { get; set; } = new();
    }

    /// <summary>Unified favorites sidebar row (section header or pinned folder).</summary>
    public abstract class FavoriteSidebarRow : ObservableObject
    {
        public abstract bool IsHeader { get; }
    }

    /// <summary>Collapsible group header in the favorites sidebar.</summary>
    public partial class FavoriteGroupHeaderRow : FavoriteSidebarRow
    {
        public FavoriteGroupHeaderRow(string groupId, string name, bool isExpanded)
        {
            GroupId = groupId;
            _name = name;
            _isExpanded = isExpanded;
        }

        public override bool IsHeader => true;
        public string GroupId { get; }

        [ObservableProperty]
        private string _name;

        [ObservableProperty]
        private bool _isExpanded;
    }

    /// <summary>Pinned folder row in the favorites sidebar.</summary>
    public partial class FavoriteItemRow : FavoriteSidebarRow
    {
        public FavoriteItemRow(FavoriteItem item, string? groupId)
        {
            Item = item;
            GroupId = groupId;
        }

        public override bool IsHeader => false;

        /// <summary>Null = ungrouped (above the first section header).</summary>
        public string? GroupId { get; }

        public FavoriteItem Item { get; }
    }
}
