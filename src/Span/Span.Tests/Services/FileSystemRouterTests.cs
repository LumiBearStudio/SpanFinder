using Moq;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FileSystemRouterTests
{
    private FileSystemRouter _router = null!;

    [TestInitialize]
    public void Setup()
    {
        _router = new FileSystemRouter();
    }

    private static Mock<IFileSystemProvider> CreateProvider(string scheme, string displayName = "Test")
    {
        var mock = new Mock<IFileSystemProvider>();
        mock.Setup(p => p.Scheme).Returns(scheme);
        mock.Setup(p => p.DisplayName).Returns(displayName);
        return mock;
    }

    // ── RegisterProvider ──

    [TestMethod]
    public void RegisterProvider_FirstProvider_BecomesDefault()
    {
        var fileProvider = CreateProvider("file", "Local");
        _router.RegisterProvider(fileProvider.Object);

        // 로컬 경로를 전달하면 default provider가 반환되어야 함
        var result = _router.GetProvider(@"C:\Users\test");
        Assert.AreSame(fileProvider.Object, result);
    }

    [TestMethod]
    public void RegisterProvider_MultipleProviders_FirstRemainsDefault()
    {
        var fileProvider = CreateProvider("file");
        var sftpProvider = CreateProvider("sftp");

        _router.RegisterProvider(fileProvider.Object);
        _router.RegisterProvider(sftpProvider.Object);

        var result = _router.GetProvider(@"D:\data");
        Assert.AreSame(fileProvider.Object, result, "첫 번째 등록된 provider가 default여야 함");
    }

    // ── GetProvider ──

    [TestMethod]
    public void GetProvider_LocalPath_ReturnsDefaultProvider()
    {
        var fileProvider = CreateProvider("file");
        _router.RegisterProvider(fileProvider.Object);

        var result = _router.GetProvider(@"C:\Windows\System32");
        Assert.AreSame(fileProvider.Object, result);
    }

    [TestMethod]
    public void GetProvider_RemoteUri_ReturnsCorrectProvider()
    {
        var fileProvider = CreateProvider("file");
        var sftpProvider = CreateProvider("sftp");
        _router.RegisterProvider(fileProvider.Object);
        _router.RegisterProvider(sftpProvider.Object);

        var result = _router.GetProvider("sftp://myhost.com/home/user");
        Assert.AreSame(sftpProvider.Object, result);
    }

    [TestMethod]
    public void GetProvider_FtpUri_ReturnsCorrectProvider()
    {
        var fileProvider = CreateProvider("file");
        var ftpProvider = CreateProvider("ftp");
        _router.RegisterProvider(fileProvider.Object);
        _router.RegisterProvider(ftpProvider.Object);

        var result = _router.GetProvider("ftp://server:21/data");
        Assert.AreSame(ftpProvider.Object, result);
    }

    [TestMethod]
    public void GetProvider_NoProviderRegistered_ThrowsInvalidOperationException()
    {
        Assert.ThrowsException<InvalidOperationException>(
            () => _router.GetProvider(@"C:\test"));
    }

    [TestMethod]
    public void GetProvider_FileSchemeUri_ReturnsDefaultProvider()
    {
        var fileProvider = CreateProvider("file");
        _router.RegisterProvider(fileProvider.Object);

        // file:// URI는 로컬 경로처럼 default provider를 반환해야 함
        var result = _router.GetProvider("file:///C:/Users/test");
        Assert.AreSame(fileProvider.Object, result);
    }

    // ── IsRemotePath (정적 메서드) ──

    [TestMethod]
    [DataRow("sftp://host/path", true, DisplayName = "sftp URI -> true")]
    [DataRow("ftp://server:21/data", true, DisplayName = "ftp URI -> true")]
    [DataRow("https://example.com", true, DisplayName = "https URI -> true")]
    [DataRow(@"C:\Users\test", false, DisplayName = "로컬 경로 -> false")]
    [DataRow(@"D:\data\file.txt", false, DisplayName = "로컬 파일 경로 -> false")]
    [DataRow("file://localhost/share", false, DisplayName = "file:// URI -> false")]
    [DataRow("", false, DisplayName = "빈 문자열 -> false")]
    public void IsRemotePath_ReturnsExpected(string path, bool expected)
    {
        Assert.AreEqual(expected, FileSystemRouter.IsRemotePath(path));
    }

    // ── ExtractRemotePath (정적 메서드) ──

    [TestMethod]
    public void ExtractRemotePath_ExtractsPathFromUri()
    {
        var result = FileSystemRouter.ExtractRemotePath("sftp://myhost.com/home/user/docs");
        Assert.AreEqual("/home/user/docs", result);
    }

    [TestMethod]
    public void ExtractRemotePath_UriWithPort_ExtractsPath()
    {
        var result = FileSystemRouter.ExtractRemotePath("ftp://server:2222/data/files");
        Assert.AreEqual("/data/files", result);
    }

    [TestMethod]
    public void ExtractRemotePath_InvalidUri_ReturnsSlash()
    {
        var result = FileSystemRouter.ExtractRemotePath("not a valid uri");
        Assert.AreEqual("/", result);
    }

    // ── GetUriPrefix (정적 메서드) ──

    [TestMethod]
    public void GetUriPrefix_ExtractsSchemeHostPort()
    {
        var result = FileSystemRouter.GetUriPrefix("sftp://myhost.com:22/home/user/docs");
        Assert.AreEqual("sftp://myhost.com:22", result);
    }

    [TestMethod]
    public void GetUriPrefix_WithUserInfo_IncludesUserInfo()
    {
        var result = FileSystemRouter.GetUriPrefix("sftp://admin@myhost.com:22/path");
        Assert.AreEqual("sftp://admin@myhost.com:22", result);
    }

    [TestMethod]
    public void GetUriPrefix_DefaultPort_IncludesDefaultPort()
    {
        // ftp 기본 포트 21
        var result = FileSystemRouter.GetUriPrefix("ftp://server/data");
        Assert.AreEqual("ftp://server:21", result);
    }

    // ── RegisterConnection / UnregisterConnection ──

    [TestMethod]
    public void RegisterConnection_AddsActiveConnection()
    {
        var provider = CreateProvider("sftp");
        _router.RegisterConnection("sftp://host:22", provider.Object);

        var result = _router.GetConnectionForPath("sftp://host:22/home/user");
        Assert.AreSame(provider.Object, result);
    }

    [TestMethod]
    public void UnregisterConnection_RemovesActiveConnection()
    {
        var provider = CreateProvider("sftp");
        provider.As<IDisposable>();
        _router.RegisterConnection("sftp://host:22", provider.Object);

        _router.UnregisterConnection("sftp://host:22");

        var result = _router.GetConnectionForPath("sftp://host:22/home/user");
        Assert.IsNull(result, "연결 해제 후 null을 반환해야 함");
    }

    [TestMethod]
    public void UnregisterConnection_TrailingSlashHandled()
    {
        var provider = CreateProvider("sftp");
        provider.As<IDisposable>();
        // 등록 시 trailing slash 있음
        _router.RegisterConnection("sftp://host:22/", provider.Object);

        // 해제 시 trailing slash 없음 — 동일하게 처리되어야 함
        _router.UnregisterConnection("sftp://host:22");

        var result = _router.GetConnectionForPath("sftp://host:22/home");
        Assert.IsNull(result);
    }

    // ── GetConnectionForPath: longest prefix match ──

    [TestMethod]
    public void GetConnectionForPath_LongestPrefixMatch()
    {
        var provider1 = CreateProvider("sftp", "Host1");
        var provider2 = CreateProvider("sftp", "Host1-Specific");

        _router.RegisterConnection("sftp://host:22", provider1.Object);
        _router.RegisterConnection("sftp://host:22/home/admin", provider2.Object);

        // 더 긴 prefix가 매칭되어야 함
        var result = _router.GetConnectionForPath("sftp://host:22/home/admin/docs");
        Assert.AreSame(provider2.Object, result, "가장 긴 prefix가 매칭되어야 함");
    }

    [TestMethod]
    public void GetConnectionForPath_NoMatch_ReturnsNull()
    {
        var result = _router.GetConnectionForPath("sftp://unknown:22/path");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetConnectionForPath_ShorterPrefixMatch()
    {
        var provider1 = CreateProvider("sftp", "Host1");
        var provider2 = CreateProvider("sftp", "Host1-Specific");

        _router.RegisterConnection("sftp://host:22", provider1.Object);
        _router.RegisterConnection("sftp://host:22/home/admin", provider2.Object);

        // 짧은 prefix만 매칭되는 경로
        var result = _router.GetConnectionForPath("sftp://host:22/var/log");
        Assert.AreSame(provider1.Object, result, "짧은 prefix가 매칭되어야 함");
    }

    // ── GetAllProviders ──

    [TestMethod]
    public void GetAllProviders_ReturnsAllRegistered()
    {
        var file = CreateProvider("file");
        var sftp = CreateProvider("sftp");
        _router.RegisterProvider(file.Object);
        _router.RegisterProvider(sftp.Object);

        var all = _router.GetAllProviders();
        Assert.AreEqual(2, all.Count);
    }
}
