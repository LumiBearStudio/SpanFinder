using System.Collections.Generic;
using System.ComponentModel;
using System.Threading.Tasks;
using Span.Helpers;

namespace Span.Tests.Helpers;

/// <summary>
/// Design §5.1 T-1 ~ T-15.
/// FontScaleService is a singleton — each test resets Level to 0 in TestInitialize
/// and unsubscribes PropertyChanged handlers in TestCleanup to prevent leakage.
/// </summary>
[TestClass]
public class FontScaleServiceTests
{
    private PropertyChangedEventHandler? _subscribedHandler;

    [TestInitialize]
    public void Reset()
    {
        // 싱글톤 상태를 매 테스트 시작 시 0 으로 초기화.
        // 직접 setter 호출 — 앞 테스트에서 Level != 0 이면 PropertyChanged 가 발생하지만
        // 이 시점에는 아직 핸들러가 구독되지 않은 상태이므로 안전.
        FontScaleService.Instance.Level = 0;
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (_subscribedHandler != null)
        {
            FontScaleService.Instance.PropertyChanged -= _subscribedHandler;
            _subscribedHandler = null;
        }
        // 다음 테스트를 위해 다시 0 복원
        FontScaleService.Instance.Level = 0;
    }

    // ---------------------------------------------------------------------
    // T-1: 싱글톤 참조 동일성
    // ---------------------------------------------------------------------
    [TestMethod]
    public void Instance_IsSingleton()
    {
        var a = FontScaleService.Instance;
        var b = FontScaleService.Instance;
        Assert.AreSame(a, b, "FontScaleService.Instance must return the same object every time.");
    }

    // ---------------------------------------------------------------------
    // T-2: 기본값 0
    // ---------------------------------------------------------------------
    [TestMethod]
    public void DefaultLevel_IsZero()
    {
        // Reset() 에서 0 으로 맞춘 상태
        Assert.AreEqual(0, FontScaleService.Instance.Level);
    }

    // ---------------------------------------------------------------------
    // T-3: 하한 클램프
    // ---------------------------------------------------------------------
    [TestMethod]
    public void SetLevel_ClampsBelow0()
    {
        FontScaleService.Instance.Level = -3;
        Assert.AreEqual(0, FontScaleService.Instance.Level);

        FontScaleService.Instance.Level = int.MinValue;
        Assert.AreEqual(0, FontScaleService.Instance.Level);
    }

    // ---------------------------------------------------------------------
    // T-4: 상한 클램프
    // ---------------------------------------------------------------------
    [TestMethod]
    public void SetLevel_ClampsAbove5()
    {
        FontScaleService.Instance.Level = 99;
        Assert.AreEqual(5, FontScaleService.Instance.Level);

        FontScaleService.Instance.Level = int.MaxValue;
        Assert.AreEqual(5, FontScaleService.Instance.Level);
    }

    // ---------------------------------------------------------------------
    // T-5: 유효 범위 저장
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(3)]
    [DataRow(4)]
    [DataRow(5)]
    public void SetLevel_ValidRange(int level)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(level, FontScaleService.Instance.Level);
    }

    // ---------------------------------------------------------------------
    // T-6: Level 변경 시 8개 PropertyChanged 이벤트 발생
    //     (Level + ItemFontSize + ItemFontSizeXL + IconFontSize +
    //      SecondaryFontSize + AddressBarFontSize + AddressBarIconFontSize +
    //      MillerColumnWidth)
    // ---------------------------------------------------------------------
    [TestMethod]
    public void SetLevel_RaisesPropertyChanged()
    {
        var raised = new List<string>();
        _subscribedHandler = (s, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };
        FontScaleService.Instance.PropertyChanged += _subscribedHandler;

        FontScaleService.Instance.Level = 3;

        CollectionAssert.AreEquivalent(
            new[]
            {
                nameof(FontScaleService.Level),
                nameof(FontScaleService.ItemFontSize),
                nameof(FontScaleService.ItemFontSizeXL),
                nameof(FontScaleService.IconFontSize),
                nameof(FontScaleService.SecondaryFontSize),
                nameof(FontScaleService.AddressBarFontSize),
                nameof(FontScaleService.AddressBarIconFontSize),
                nameof(FontScaleService.MillerColumnWidth),
            },
            raised,
            $"Expected exactly 8 PropertyChanged events, got {raised.Count}: [{string.Join(", ", raised)}]");
    }

    // ---------------------------------------------------------------------
    // T-7: 동일 값 setter 호출 시 PropertyChanged 미발생 (중복 무시)
    // ---------------------------------------------------------------------
    [TestMethod]
    public void SetLevel_NoChange_NoEvent()
    {
        FontScaleService.Instance.Level = 3; // 사전 세팅

        var raised = new List<string>();
        _subscribedHandler = (s, e) =>
        {
            if (e.PropertyName != null) raised.Add(e.PropertyName);
        };
        FontScaleService.Instance.PropertyChanged += _subscribedHandler;

        FontScaleService.Instance.Level = 3; // 동일 값 재할당

        Assert.AreEqual(0, raised.Count,
            "Setting Level to the same value must not raise PropertyChanged.");
    }

    // ---------------------------------------------------------------------
    // T-8: ItemFontSize 매핑
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 13.0)]
    [DataRow(1, 14.0)]
    [DataRow(2, 15.0)]
    [DataRow(3, 16.0)]
    [DataRow(4, 17.0)]
    [DataRow(5, 18.0)]
    public void ItemFontSize_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.ItemFontSize, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-9: IconFontSize 매핑
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 16.0)]
    [DataRow(3, 19.0)]
    [DataRow(5, 21.0)]
    public void IconFontSize_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.IconFontSize, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-10: SecondaryFontSize 매핑
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 12.0)]
    [DataRow(3, 15.0)]
    [DataRow(5, 17.0)]
    public void SecondaryFontSize_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.SecondaryFontSize, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-11: MillerColumnWidth 매핑 (220 + level * 6)
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 220.0)]
    [DataRow(1, 226.0)]
    [DataRow(3, 238.0)]
    [DataRow(5, 250.0)]
    public void MillerColumnWidth_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.MillerColumnWidth, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-12: 동시 쓰기 — 최종값이 유효한 0..5 범위 (방어적 테스트)
    // ---------------------------------------------------------------------
    [TestMethod]
    public void ConcurrentSet_LastWins()
    {
        // 두 스레드에서 무작위하게 값을 써도 최종값은 유효 범위에 있어야 함.
        // 실제 운영에서는 UI 스레드 single-writer 규약이지만,
        // int 쓰기의 atomicity 및 클램프 로직의 최종 일관성을 검증.
        Parallel.For(0, 1000, i =>
        {
            FontScaleService.Instance.Level = i % 12 - 3; // -3 ~ 8
        });

        int final = FontScaleService.Instance.Level;
        Assert.IsTrue(final >= 0 && final <= 5,
            $"Final Level must be in [0..5], got {final}.");
    }

    // ---------------------------------------------------------------------
    // T-13: ItemFontSizeXL 매핑 (W-1, IconView XL 전용 baseline 14)
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 14.0)]
    [DataRow(1, 15.0)]
    [DataRow(3, 17.0)]
    [DataRow(5, 19.0)]
    public void ItemFontSizeXL_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.ItemFontSizeXL, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-14: AddressBarFontSize 매핑 (MF-3, AddressBar 텍스트 baseline 11)
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 11.0)]
    [DataRow(1, 12.0)]
    [DataRow(3, 14.0)]
    [DataRow(5, 16.0)]
    public void AddressBarFontSize_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.AddressBarFontSize, 0.0001);
    }

    // ---------------------------------------------------------------------
    // T-15: AddressBarIconFontSize 매핑 (MF-3, AddressBar 아이콘 baseline 13)
    // ---------------------------------------------------------------------
    [TestMethod]
    [DataRow(0, 13.0)]
    [DataRow(1, 14.0)]
    [DataRow(3, 16.0)]
    [DataRow(5, 18.0)]
    public void AddressBarIconFontSize_Mapping(int level, double expected)
    {
        FontScaleService.Instance.Level = level;
        Assert.AreEqual(expected, FontScaleService.Instance.AddressBarIconFontSize, 0.0001);
    }
}
