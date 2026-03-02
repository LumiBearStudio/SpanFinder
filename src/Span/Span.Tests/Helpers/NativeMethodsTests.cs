using Span.Helpers;

namespace Span.Tests.Helpers;

[TestClass]
public class NativeMethodsTests
{
    // ── SafeDwmSetWindowAttribute ──────────────────────────

    [TestMethod]
    public void SafeDwmSetWindowAttribute_IntPtrZero_ReturnsFalse()
    {
        int value = 1;
        var result = NativeMethods.SafeDwmSetWindowAttribute(IntPtr.Zero, 0, ref value, sizeof(int));
        Assert.IsFalse(result, "Should return false for IntPtr.Zero hwnd");
    }

    // ── SafeSetWindowPos ───────────────────────────────────

    [TestMethod]
    public void SafeSetWindowPos_IntPtrZero_ReturnsFalse()
    {
        var result = NativeMethods.SafeSetWindowPos(IntPtr.Zero, IntPtr.Zero, 0, 0, 100, 100, 0);
        Assert.IsFalse(result, "Should return false for IntPtr.Zero hwnd");
    }

    // ── SafeSetForegroundWindow ────────────────────────────

    [TestMethod]
    public void SafeSetForegroundWindow_IntPtrZero_ReturnsFalse()
    {
        var result = NativeMethods.SafeSetForegroundWindow(IntPtr.Zero);
        Assert.IsFalse(result, "Should return false for IntPtr.Zero hwnd");
    }
}
