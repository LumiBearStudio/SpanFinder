using System;
using System.ComponentModel;

namespace Span.Helpers
{
    /// <summary>
    /// 폰트 스케일의 Single Source of Truth.
    /// Settings.IconFontScale(0~5) 슬라이더가 본 서비스의 Level만 갱신하고,
    /// 모든 XAML은 {Binding X, Source={StaticResource FontScale}} 로 구독.
    ///
    /// 기존 fan-out dispatch (ApplyIconFontScale*, _iconFontScaleLevel 필드,
    /// VisualTree 순회, ConditionalWeakTable baseline 트릭) 전체를 대체함.
    ///
    /// [Thread Safety] setter는 UI(DispatcherQueue) 스레드에서만 호출된다는
    /// single-writer 규약을 따른다. Span 전체에서 쓰기 경로는 단 2곳:
    ///   1) MainWindow.SettingsHandler.OnAppearanceSettingChanged (슬라이더 이벤트)
    ///   2) MainWindow 생성자 앱 시작 시 복원 1회
    /// 둘 다 UI 스레드. int 쓰기는 CLR에서 atomic이며,
    /// reader(Binding) 측은 PropertyChanged 이후 UI 스레드에서 안전하게 읽음.
    /// WinUI 타입(DispatcherQueue)을 직접 참조하면 Span.Tests에서 컴파일 불가하므로
    /// 런타임 어서트 대신 convention으로 보장.
    /// </summary>
    public sealed class FontScaleService : INotifyPropertyChanged
    {
        // 테스트(Span.Tests) 는 Application.Current == null 이므로 별도 싱글톤이 필요.
        // 런타임은 App.xaml 의 XAML 파서가 <helpers:FontScaleService x:Key="FontScale"/>
        // 를 활성화하면서 만든 인스턴스가 Application.Resources["FontScale"] 로 저장됨.
        // Instance 는 런타임엔 XAML 인스턴스를, 테스트엔 fallback 싱글톤을 반환.
        private static FontScaleService? _fallback;

        /// <summary>
        /// 전역 단일 인스턴스.
        /// - 런타임(Application.Current != null): App.xaml 에 선언된
        ///   <c>{StaticResource FontScale}</c> 와 동일한 XAML 파서-생성 객체.
        /// - 테스트(Application 없음): lazy-created fallback 싱글톤.
        /// 두 경우 모두 호출자 관점에서 "한 앱 수명 당 하나의 객체" 라는 불변식 유지.
        /// </summary>
        public static FontScaleService Instance
        {
            get
            {
                // 런타임: Application.Current.Resources 에서 가져옴.
                // {StaticResource FontScale} 와 정확히 같은 객체를 보장.
                try
                {
                    if (Microsoft.UI.Xaml.Application.Current is { } app)
                    {
                        if (app.Resources.TryGetValue("FontScale", out var val)
                            && val is FontScaleService svc)
                        {
                            return svc;
                        }
                    }
                }
                catch
                {
                    // Application.Current.Resources 접근 실패 시 fallback 으로 내려감
                }

                // 테스트 / 초기 부팅 fallback
                return _fallback ??= new FontScaleService();
            }
        }

        /// <summary>
        /// XAML 파서용 public ctor.
        /// App.xaml 의 <c>&lt;helpers:FontScaleService x:Key="FontScale"/&gt;</c> 활성화.
        /// 코드에서 직접 <c>new FontScaleService()</c> 호출은 테스트에서만 허용.
        /// </summary>
        public FontScaleService() { }

        private int _level = 0;

        /// <summary>
        /// 현재 폰트 스케일 레벨. 0 ~ 5 범위로 자동 클램프.
        /// 동일 값 할당 시 PropertyChanged 미발생 (중복 무시).
        /// </summary>
        public int Level
        {
            get => _level;
            set
            {
                int clamped = Math.Clamp(value, 0, 5);
                if (_level == clamped) return;
                _level = clamped;

                DebugLogger.Log(
                    $"[FontScale] view=service tab=- level={clamped} reason=setter");

                // 8개 변경 통지 — MillerColumnWidth 까지 한 set 에서 일괄 raise
                Raise(nameof(Level));
                Raise(nameof(ItemFontSize));
                Raise(nameof(ItemFontSizeXL));
                Raise(nameof(IconFontSize));
                Raise(nameof(SecondaryFontSize));
                Raise(nameof(AddressBarFontSize));
                Raise(nameof(AddressBarIconFontSize));
                Raise(nameof(MillerColumnWidth));
            }
        }

        // --- 표준 리스트 항목 (CAT-A/B/C) ---------------------------------
        // baseline 13/16/12 + level

        /// <summary>항목 이름 텍스트. baseline 13 → 13..18.</summary>
        public double ItemFontSize => 13.0 + _level;

        /// <summary>항목 아이콘(FontIcon). baseline 16 → 16..21.</summary>
        public double IconFontSize => 16.0 + _level;

        /// <summary>보조 셀 텍스트 (날짜/크기/타입). baseline 12 → 12..17.</summary>
        public double SecondaryFontSize => 12.0 + _level;

        // --- IconView XL 전용 (W-1) --------------------------------------
        // 다른 모드는 baseline 13, XL만 14. 별도 derived 로 regression 방지.

        /// <summary>IconView ExtraLarge 모드 텍스트. baseline 14 → 14..19.</summary>
        public double ItemFontSizeXL => 14.0 + _level;

        // --- AddressBar 전용 (MF-3) --------------------------------------
        // 기존 ApplyAbsoluteScaleToTree(elem, level, 8, 20) 동작과 픽셀 단위 동일.
        // AddressBar는 컴팩트 chrome 이라 일반 리스트보다 작은 baseline 이 표준.

        /// <summary>
        /// AddressBar 세그먼트 텍스트 / AutoSuggest / Overflow. baseline 11 → 11..16.
        /// level=0 에서 기존 동작과 픽셀 단위 동일.
        /// </summary>
        public double AddressBarFontSize => 11.0 + _level;

        /// <summary>
        /// AddressBar 세그먼트 FontIcon. baseline 13 → 13..18.
        /// level=0 에서 기존 동작과 픽셀 단위 동일.
        /// </summary>
        public double AddressBarIconFontSize => 13.0 + _level;

        // --- Miller 컬럼 Width (Q-1) -------------------------------------

        /// <summary>
        /// Miller 컬럼 외부 Grid Width. 220 + level*6 → 220..250.
        /// FontSize가 커질수록 컬럼도 약간 넓어져 텍스트 잘림 방지.
        /// </summary>
        public double MillerColumnWidth => 220.0 + _level * 6.0;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Raise(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
