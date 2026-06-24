using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace Span.Models
{
    /// <summary>
    /// Represents a subfolder node in the sidebar favorites tree.
    /// Used as TreeViewNode.Content for lazily-loaded child folders.
    /// desktop.ini 기반 커스텀 아이콘 lazy 로드 지원 (Issue #39 a).
    /// </summary>
    public partial class SidebarFolderNode : ObservableObject
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = ""; // overridden by IconService at construction
        public string IconColor { get; set; } = "#FFC857"; // Folder yellow

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

        public void RequestCustomIconLoad()
        {
            if (_customIconRequested) return;
            if (string.IsNullOrEmpty(Path)) return;

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
                Helpers.DebugLogger.Log($"[SidebarFolderNode.CustomIcon] RequestCustomIconLoad failed for {Path}: {ex.Message}");
            }
        }

        private async System.Threading.Tasks.Task LoadCustomIconAsync(Services.FolderIconService iconSvc)
        {
            try
            {
                var icon = await iconSvc.GetCustomIconAsync(Path).ConfigureAwait(false);
                if (icon == null) return;

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
                Helpers.DebugLogger.Log($"[SidebarFolderNode.CustomIcon] LoadCustomIconAsync failed for {Path}: {ex.Message}");
            }
        }

        public void ClearCustomIcon()
        {
            _customIconRequested = false;
            CustomIcon = null;
        }

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
}
