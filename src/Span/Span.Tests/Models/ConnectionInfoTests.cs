namespace Span.Tests.Models;

[TestClass]
public class ConnectionInfoTests
{
    // ── GetDefaultPort ──────────────────────────────────

    [TestMethod]
    [DataRow(Span.Models.RemoteProtocol.SFTP, 22)]
    [DataRow(Span.Models.RemoteProtocol.FTP, 21)]
    [DataRow(Span.Models.RemoteProtocol.FTPS, 990)]
    [DataRow(Span.Models.RemoteProtocol.SMB, 445)]
    public void GetDefaultPort_ReturnsCorrectPort(Span.Models.RemoteProtocol protocol, int expected)
    {
        Assert.AreEqual(expected, Span.Models.ConnectionInfo.GetDefaultPort(protocol));
    }

    [TestMethod]
    public void GetDefaultPort_UnknownProtocol_Returns22()
    {
        // Cast an out-of-range value to trigger the default arm
        var unknown = (Span.Models.RemoteProtocol)999;
        Assert.AreEqual(22, Span.Models.ConnectionInfo.GetDefaultPort(unknown));
    }

    // ── Default property values ─────────────────────────

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        var conn = new Span.Models.ConnectionInfo();

        Assert.IsFalse(string.IsNullOrEmpty(conn.Id), "Id should be auto-generated");
        Assert.AreEqual(32, conn.Id.Length, "Id should be 32-char hex (Guid N format)");
        Assert.AreEqual(string.Empty, conn.DisplayName);
        Assert.AreEqual(Span.Models.RemoteProtocol.SFTP, conn.Protocol);
        Assert.AreEqual(string.Empty, conn.Host);
        Assert.AreEqual(22, conn.Port);
        Assert.AreEqual(string.Empty, conn.Username);
        Assert.AreEqual(Span.Models.AuthMethod.Password, conn.AuthMethod);
        Assert.IsNull(conn.SshKeyPath);
        Assert.AreEqual("/", conn.RemotePath);
        Assert.AreEqual(default(DateTime), conn.LastConnected);
        Assert.IsNull(conn.UncPath);
        Assert.IsNull(conn.TrustedCertThumbprint);
        Assert.IsNull(conn.TrustedHostKeyFingerprint);
    }

    [TestMethod]
    public void Id_IsUniquePerInstance()
    {
        var a = new Span.Models.ConnectionInfo();
        var b = new Span.Models.ConnectionInfo();
        Assert.AreNotEqual(a.Id, b.Id);
    }

    // ── ToUri ───────────────────────────────────────────

    [TestMethod]
    public void ToUri_Smb_ReturnsUncPath()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = @"\\server\share"
        };

        Assert.AreEqual(@"\\server\share", conn.ToUri());
    }

    [TestMethod]
    public void ToUri_Smb_NullUncPath_ReturnsEmpty()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = null
        };

        Assert.AreEqual(string.Empty, conn.ToUri());
    }

    [TestMethod]
    public void ToUri_Sftp_BuildsCorrectUri()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP,
            Username = "admin",
            Host = "example.com",
            Port = 22,
            RemotePath = "/home/admin"
        };

        Assert.AreEqual("sftp://admin@example.com:22/home/admin", conn.ToUri());
    }

    [TestMethod]
    public void ToUri_Ftp_BuildsCorrectUri()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.FTP,
            Username = "user",
            Host = "ftp.example.com",
            Port = 21,
            RemotePath = "/pub"
        };

        Assert.AreEqual("ftp://user@ftp.example.com:21/pub", conn.ToUri());
    }

    [TestMethod]
    public void ToUri_Ftps_BuildsCorrectUri()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.FTPS,
            Username = "secure",
            Host = "ftps.example.com",
            Port = 990,
            RemotePath = "/"
        };

        Assert.AreEqual("ftps://secure@ftps.example.com:990/", conn.ToUri());
    }

    [TestMethod]
    public void ToUri_EmptyFields_ProducesUriWithEmptySegments()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP
            // all string fields default to empty
        };

        // sftp://@:22/
        var uri = conn.ToUri();
        Assert.IsTrue(uri.StartsWith("sftp://"));
        Assert.IsTrue(uri.Contains(":22"));
    }

    [TestMethod]
    public void ToUri_SpecialCharactersInUsername()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP,
            Username = "user@domain",
            Host = "host.com",
            Port = 2222,
            RemotePath = "/path with spaces"
        };

        var uri = conn.ToUri();
        Assert.AreEqual("sftp://user@domain@host.com:2222/path with spaces", uri);
    }

    // ── Enum values ─────────────────────────────────────

    [TestMethod]
    public void RemoteProtocol_HasExpectedValues()
    {
        var values = Enum.GetValues<Span.Models.RemoteProtocol>();
        Assert.AreEqual(4, values.Length);
        CollectionAssert.Contains(values, Span.Models.RemoteProtocol.SFTP);
        CollectionAssert.Contains(values, Span.Models.RemoteProtocol.FTP);
        CollectionAssert.Contains(values, Span.Models.RemoteProtocol.FTPS);
        CollectionAssert.Contains(values, Span.Models.RemoteProtocol.SMB);
    }

    [TestMethod]
    public void AuthMethod_HasExpectedValues()
    {
        var values = Enum.GetValues<Span.Models.AuthMethod>();
        Assert.AreEqual(2, values.Length);
        CollectionAssert.Contains(values, Span.Models.AuthMethod.Password);
        CollectionAssert.Contains(values, Span.Models.AuthMethod.SshKey);
    }
}
