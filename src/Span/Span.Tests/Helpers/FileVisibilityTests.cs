using System.IO;
using Span.Helpers;

namespace Span.Tests.Helpers;

/// <summary>
/// Issue #68 회귀 방지. 네이버 MYBOX 드라이브 루트가 통째로 빈 폴더로 보였던 원인이
/// "System 단독 항목을 숨김으로 취급"한 것이었다. 그 규칙을 여기에 고정한다.
/// </summary>
[TestClass]
public class FileVisibilityTests
{
    // ── IsHidden: 속성만 보는 규칙 ──────────────────────────

    [TestMethod]
    public void IsHidden_Normal_False()
    {
        Assert.IsFalse(FileVisibility.IsHidden(FileAttributes.Normal));
    }

    [TestMethod]
    public void IsHidden_SystemOnly_False()
    {
        // Issue #68의 핵심. MYBOX 루트(개인/공유/즐겨찾기/휴지통), C:\ProgramData\Microsoft,
        // C:\Windows\Fonts 가 모두 이 조합이며 탐색기는 항상 표시한다.
        Assert.IsFalse(FileVisibility.IsHidden(FileAttributes.System | FileAttributes.Directory));
    }

    [TestMethod]
    public void IsHidden_HiddenOnly_True()
    {
        Assert.IsTrue(FileVisibility.IsHidden(FileAttributes.Hidden));
    }

    [TestMethod]
    public void IsHidden_HiddenAndSystem_True()
    {
        Assert.IsTrue(FileVisibility.IsHidden(FileAttributes.Hidden | FileAttributes.System));
    }

    [TestMethod]
    public void IsHidden_ReadOnlyAndSystem_False()
    {
        // C:\Windows\Fonts 조합.
        Assert.IsFalse(FileVisibility.IsHidden(
            FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Directory));
    }

    // ── ShouldHide: 설정까지 반영하는 정책 ──────────────────

    [TestMethod]
    public void ShouldHide_SystemOnly_NeverHidden()
    {
        var attrs = FileAttributes.System | FileAttributes.Directory;
        Assert.IsFalse(FileVisibility.ShouldHide(attrs, showHidden: false));
        Assert.IsFalse(FileVisibility.ShouldHide(attrs, showHidden: true));
    }

    [TestMethod]
    public void ShouldHide_Hidden_FollowsSetting()
    {
        Assert.IsTrue(FileVisibility.ShouldHide(FileAttributes.Hidden, showHidden: false));
        Assert.IsFalse(FileVisibility.ShouldHide(FileAttributes.Hidden, showHidden: true));
    }

    [TestMethod]
    public void ShouldHide_Normal_NeverHidden()
    {
        Assert.IsFalse(FileVisibility.ShouldHide(FileAttributes.Normal, showHidden: false));
        Assert.IsFalse(FileVisibility.ShouldHide(FileAttributes.Normal, showHidden: true));
    }

    // ── AttributesToSkip: 열거자 레벨 필터가 같은 결과를 내는가 ──

    [TestMethod]
    public void AttributesToSkip_MatchesShouldHide()
    {
        FileAttributes[] samples =
        {
            FileAttributes.Normal,
            FileAttributes.Hidden,
            FileAttributes.System | FileAttributes.Directory,
            FileAttributes.Hidden | FileAttributes.System,
            FileAttributes.ReadOnly | FileAttributes.System | FileAttributes.Directory,
        };

        foreach (var showHidden in new[] { false, true })
        {
            var skip = FileVisibility.AttributesToSkip(showHidden);
            foreach (var attrs in samples)
            {
                bool skippedByEnumerator = (attrs & skip) != 0;
                Assert.AreEqual(
                    FileVisibility.ShouldHide(attrs, showHidden),
                    skippedByEnumerator,
                    $"attrs={attrs}, showHidden={showHidden}");
            }
        }
    }
}
