using Span.Helpers;

namespace Span.Tests.Unit;

/// <summary>
/// Issue #67 — classifying UNC paths so a server root can be browsed without capturing
/// paths that already work today.
/// </summary>
[TestClass]
public class UncPathHelperTests
{
    [TestMethod]
    public void IsServerRoot_AcceptsBareServers()
    {
        Assert.IsTrue(UncPathHelper.IsServerRoot(@"\\dave-mba"));
        Assert.IsTrue(UncPathHelper.IsServerRoot(@"\\192.168.1.98"));
        Assert.IsTrue(UncPathHelper.IsServerRoot(@"\\DAVE-MBA"));
        Assert.IsTrue(UncPathHelper.IsServerRoot(@"\\dave-mba\"), "trailing separator is still a root");
        Assert.IsTrue(UncPathHelper.IsServerRoot(@"\\dave-mba/"));
    }

    [TestMethod]
    public void IsServerRoot_RejectsPathsThatAlreadyWork()
    {
        // These reach Directory.Exists successfully today. The new branch must not capture
        // them, or a working path would start going through share enumeration instead.
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\dave-mba\lanshared"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\dave-mba\lanshared\sub"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\wsl.localhost\Ubuntu"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\dave-mba\lanshared\"));
    }

    [TestMethod]
    public void IsServerRoot_RejectsShellNamespaceRoots()
    {
        // Verified: Directory.Exists(@"\\wsl.localhost") is false but it is not a file
        // server — enumerating shares on it fails, so routing it to the server-root path
        // would replace today's (working) Explorer hand-off with an empty folder.
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\wsl.localhost"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\wsl$"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\WSL.LOCALHOST"));
    }

    [TestMethod]
    public void IsServerRoot_RejectsDevicePaths()
    {
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\?\C:\x"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\?\UNC\srv\share"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\.\pipe\x"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\?\"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\.\"));
    }

    [TestMethod]
    public void IsServerRoot_RejectsNonUncAndDegenerateInput()
    {
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"C:\Users"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\single"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(@"\\\"));
        Assert.IsFalse(UncPathHelper.IsServerRoot(""));
        Assert.IsFalse(UncPathHelper.IsServerRoot(null));
    }

    [TestMethod]
    public void IsShellNamespaceRoot_MatchesOnlyTheRootItself()
    {
        Assert.IsTrue(UncPathHelper.IsShellNamespaceRoot(@"\\wsl.localhost"));
        Assert.IsTrue(UncPathHelper.IsShellNamespaceRoot(@"\\wsl.localhost\"));
        Assert.IsTrue(UncPathHelper.IsShellNamespaceRoot(@"\\wsl$"));

        // The level below is a real path served by the P9 redirector and must be left alone.
        Assert.IsFalse(UncPathHelper.IsShellNamespaceRoot(@"\\wsl.localhost\Ubuntu"));
        Assert.IsFalse(UncPathHelper.IsShellNamespaceRoot(@"\\dave-mba"));
        Assert.IsFalse(UncPathHelper.IsShellNamespaceRoot(null));
    }

    [TestMethod]
    public void IsUnc_IdentifiesUncForm()
    {
        Assert.IsTrue(UncPathHelper.IsUnc(@"\\dave-mba"));
        Assert.IsTrue(UncPathHelper.IsUnc(@"\\?\C:\x"));
        Assert.IsFalse(UncPathHelper.IsUnc(@"C:\x"));
        Assert.IsFalse(UncPathHelper.IsUnc(@"\x"));
        Assert.IsFalse(UncPathHelper.IsUnc(""));
        Assert.IsFalse(UncPathHelper.IsUnc(null));
    }

    [TestMethod]
    public void ServerRootAndShellNamespace_AreMutuallyExclusive()
    {
        // Both classifiers feed different branches; an overlap would make behaviour depend
        // on evaluation order.
        foreach (var p in new[] { @"\\dave-mba", @"\\wsl.localhost", @"\\wsl$",
                                  @"\\dave-mba\share", @"\\?\C:\x", @"C:\x" })
        {
            Assert.IsFalse(UncPathHelper.IsServerRoot(p) && UncPathHelper.IsShellNamespaceRoot(p),
                $"'{p}' matched both classifiers");
        }
    }
}
