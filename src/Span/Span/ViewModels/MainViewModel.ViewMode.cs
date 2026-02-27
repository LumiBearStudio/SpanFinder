using Microsoft.Extensions.DependencyInjection;
using Span.Models;
using Span.Services;
using System;

namespace Span.ViewModels
{
    /// <summary>
    /// MainViewModel partial — 뷰 모드 전환 및 영속화.
    /// Miller Columns/Details/Icon/Home/Settings 모드 스위칭, 듀얼 패인 별 ViewMode 관리,
    /// 미리보기 패널 토글, Split View 상태 저장/복원.
    /// </summary>
    public partial class MainViewModel
    {
        #region View Mode Switching

        /// <summary>
        /// 뷰 모드 전환 — 활성 패널에 적용
        /// </summary>
        public void SwitchViewMode(ViewMode mode)
        {
            // Settings mode: 별도 탭으로 열기
            if (mode == ViewMode.Settings)
            {
                OpenOrSwitchToSettingsTab();
                return;
            }

            // Home mode always targets the left pane (HomeView only exists in left pane)
            if (mode == ViewMode.Home)
            {
                if (CurrentViewMode == ViewMode.Home) return;
                ActivePane = ActivePane.Left;
                CurrentViewMode = ViewMode.Home;
                LeftViewMode = ViewMode.Home;
                SaveViewModePreference();
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: Home (always left pane)");
                UpdateStatusBar();
                return;
            }

            // Determine which pane's view mode to update
            if (IsSplitViewEnabled && ActivePane == ActivePane.Right)
            {
                if (RightViewMode == mode) return;

                if (Helpers.ViewModeExtensions.IsIconMode(mode))
                {
                    CurrentIconSize = mode;
                    RightViewMode = mode;
                }
                else
                {
                    RightViewMode = mode;
                }

                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(mode);
                Helpers.DebugLogger.Log($"[MainViewModel] Right pane AutoNav: {RightExplorer.EnableAutoNavigation} (mode: {mode})");
            }
            else
            {
                if (CurrentViewMode == mode) return;

                if (Helpers.ViewModeExtensions.IsIconMode(mode))
                {
                    CurrentIconSize = mode;
                    CurrentViewMode = mode;
                    LeftViewMode = mode;
                }
                else
                {
                    CurrentViewMode = mode;
                    LeftViewMode = mode;
                }

                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(mode);
                Helpers.DebugLogger.Log($"[MainViewModel] Left pane AutoNav: {LeftExplorer.EnableAutoNavigation} (mode: {mode})");
            }

            SaveViewModePreference();
            UpdateActiveTabHeader();
            // 활성 탭의 ViewMode도 즉시 동기화
            if (ActiveTab != null)
            {
                ActiveTab.ViewMode = CurrentViewMode;
                ActiveTab.IconSize = CurrentIconSize;
            }
            Helpers.DebugLogger.Log($"[MainViewModel] ViewMode changed: {Helpers.ViewModeExtensions.GetDisplayName(mode)}");
            UpdateStatusBar();
        }

        /// <summary>
        /// Determines if auto-navigation should be enabled based on view mode and MillerClickBehavior setting.
        /// </summary>
        private bool ShouldAutoNavigate(ViewMode mode)
        {
            if (mode != ViewMode.MillerColumns) return false;
            try
            {
                var settings = App.Current.Services.GetRequiredService<Services.SettingsService>();
                return settings.MillerClickBehavior != "double";
            }
            catch { return true; }
        }

        #endregion

        #region View Mode Persistence

        /// <summary>
        /// ViewMode 설정 저장 (LocalSettings)
        /// </summary>
        private void SaveViewModePreference()
        {
            try
            {
                // Don't persist Home or Settings as startup mode
                if (CurrentViewMode == ViewMode.Home || CurrentViewMode == ViewMode.Settings) return;

                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["ViewMode"] = (int)CurrentViewMode;
                settings.Values["IconSize"] = (int)CurrentIconSize;
                settings.Values["LeftViewMode"] = (int)LeftViewMode;
                settings.Values["RightViewMode"] = (int)RightViewMode;
                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode saved: L={LeftViewMode}, R={RightViewMode}, IconSize={CurrentIconSize}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SaveViewModePreference error: {ex.Message}");
            }
        }

        /// <summary>
        /// ViewMode 설정 로드 (앱 시작 시)
        /// </summary>
        public void LoadViewModePreference()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;

                if (settings.Values.TryGetValue("ViewMode", out var mode) && mode is int modeInt
                    && System.Enum.IsDefined(typeof(ViewMode), modeInt))
                {
                    CurrentViewMode = (ViewMode)modeInt;
                    LeftViewMode = CurrentViewMode;
                }

                if (settings.Values.TryGetValue("IconSize", out var size) && size is int sizeInt
                    && System.Enum.IsDefined(typeof(ViewMode), sizeInt))
                {
                    CurrentIconSize = (ViewMode)sizeInt;
                }

                if (settings.Values.TryGetValue("LeftViewMode", out var leftMode) && leftMode is int leftInt
                    && System.Enum.IsDefined(typeof(ViewMode), leftInt))
                {
                    LeftViewMode = (ViewMode)leftInt;
                    CurrentViewMode = LeftViewMode;
                }

                if (settings.Values.TryGetValue("RightViewMode", out var rightMode) && rightMode is int rightInt
                    && System.Enum.IsDefined(typeof(ViewMode), rightInt))
                {
                    RightViewMode = (ViewMode)rightInt;
                }

                // Load split view state
                if (settings.Values.TryGetValue("IsSplitViewEnabled", out var splitEnabled))
                {
                    IsSplitViewEnabled = (bool)splitEnabled;
                }

                // Load preview state
                if (settings.Values.TryGetValue("IsLeftPreviewEnabled", out var leftPrev))
                    IsLeftPreviewEnabled = (bool)leftPrev;
                if (settings.Values.TryGetValue("IsRightPreviewEnabled", out var rightPrev))
                    IsRightPreviewEnabled = (bool)rightPrev;

                // Set auto-navigation based on loaded view mode
                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(LeftViewMode);
                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(RightViewMode);
                Helpers.DebugLogger.Log($"[MainViewModel] AutoNav: L={LeftExplorer.EnableAutoNavigation}, R={RightExplorer.EnableAutoNavigation}");

                Helpers.DebugLogger.Log($"[MainViewModel] ViewMode loaded: L={Helpers.ViewModeExtensions.GetDisplayName(LeftViewMode)}, R={Helpers.ViewModeExtensions.GetDisplayName(RightViewMode)}, Split={IsSplitViewEnabled}");
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LoadViewModePreference error: {ex.Message}");
                CurrentViewMode = ViewMode.MillerColumns;
                LeftViewMode = ViewMode.MillerColumns;
                RightViewMode = ViewMode.MillerColumns;
                LeftExplorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.MillerColumns);
                RightExplorer.EnableAutoNavigation = ShouldAutoNavigate(ViewMode.MillerColumns);
            }
        }

        #endregion

        #region Preview / Split View State

        /// <summary>
        /// Toggle preview panel for the active pane.
        /// </summary>
        public void TogglePreview()
        {
            if (ActivePane == ActivePane.Left)
                IsLeftPreviewEnabled = !IsLeftPreviewEnabled;
            else
                IsRightPreviewEnabled = !IsRightPreviewEnabled;

            SavePreviewState();
        }

        /// <summary>
        /// Save preview panel state to LocalSettings.
        /// </summary>
        public void SavePreviewState()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["IsLeftPreviewEnabled"] = IsLeftPreviewEnabled;
                settings.Values["IsRightPreviewEnabled"] = IsRightPreviewEnabled;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving preview state: {ex.Message}");
            }
        }

        /// <summary>
        /// Save preview panel widths (called from MainWindow on close).
        /// </summary>
        public void SavePreviewWidths(double leftWidth, double rightWidth)
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["LeftPreviewWidth"] = leftWidth;
                settings.Values["RightPreviewWidth"] = rightWidth;
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving preview widths: {ex.Message}");
            }
        }

        /// <summary>
        /// Save split view state to LocalSettings
        /// </summary>
        private void SaveSplitViewState()
        {
            try
            {
                var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
                settings.Values["IsSplitViewEnabled"] = IsSplitViewEnabled;

                // Save right pane path for restore on next launch
                if (!string.IsNullOrEmpty(RightExplorer?.CurrentPath) && RightExplorer.CurrentPath != "PC")
                {
                    settings.Values["RightPanePath"] = RightExplorer.CurrentPath;
                }

                Helpers.DebugLogger.Log($"[MainViewModel] Split state saved: {IsSplitViewEnabled}");
            }
            catch (Exception ex)
            {
                Helpers.DebugLogger.Log($"[MainViewModel] Error saving split state: {ex.Message}");
            }
        }

        #endregion
    }
}
