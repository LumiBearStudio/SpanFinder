namespace Span.Tests.Models;

[TestClass]
public class DriveItemFromConnectionTests
{
    [TestInitialize]
    public void Setup()
    {
        // IconService.Current is null → FromConnection will use fallback glyphs
        Span.Services.IconService.Current = null;
    }

    // ── SMB protocol ────────────────────────────────────

    [TestMethod]
    public void FromConnection_Smb_UsesUncPathAsPath()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            DisplayName = "NAS Share",
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = @"\\nas\media"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual(@"\\nas\media", drive.Path);
    }

    [TestMethod]
    public void FromConnection_Smb_SetsDisplayName()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            DisplayName = "Office NAS",
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = @"\\server\docs"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual("Office NAS", drive.Name);
    }

    [TestMethod]
    public void FromConnection_Smb_NetworkGlyphFallback()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = @"\\srv\share"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        // IconService.Current is null → fallback \uEDD4
        Assert.AreEqual("\uEDD4", drive.IconGlyph);
    }

    [TestMethod]
    public void FromConnection_Smb_NullUncPath_EmptyPath()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = null
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual(string.Empty, drive.Path);
    }

    // ── Non-SMB protocols ───────────────────────────────

    [TestMethod]
    public void FromConnection_Sftp_UsesUriAsPath()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            DisplayName = "Dev Server",
            Protocol = Span.Models.RemoteProtocol.SFTP,
            Username = "deploy",
            Host = "dev.example.com",
            Port = 22,
            RemotePath = "/var/www"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual("sftp://deploy@dev.example.com:22/var/www", drive.Path);
    }

    [TestMethod]
    public void FromConnection_Ftp_ServerGlyphFallback()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.FTP,
            Host = "ftp.example.com"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        // IconService.Current is null → fallback \uEE71
        Assert.AreEqual("\uEE71", drive.IconGlyph);
    }

    [TestMethod]
    public void FromConnection_Ftps_ServerGlyphFallback()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.FTPS,
            Host = "secure.example.com"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual("\uEE71", drive.IconGlyph);
    }

    // ── Common fields ───────────────────────────────────

    [TestMethod]
    public void FromConnection_AlwaysSetsIsRemoteConnection()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.IsTrue(drive.IsRemoteConnection);
    }

    [TestMethod]
    public void FromConnection_SetsConnectionId()
    {
        var conn = new Span.Models.ConnectionInfo();
        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual(conn.Id, drive.ConnectionId);
    }

    [TestMethod]
    public void FromConnection_IsNetworkDrive_TrueWhenRemote()
    {
        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.IsTrue(drive.IsNetworkDrive);
    }

    // ── IconService.Current set ─────────────────────────

    [TestMethod]
    public void FromConnection_Smb_WithIconService_UsesNetworkGlyph()
    {
        Span.Services.IconService.Current = new Span.Services.IconService();

        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SMB,
            UncPath = @"\\srv\data"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual("\uEDD4", drive.IconGlyph); // stub NetworkGlyph
    }

    [TestMethod]
    public void FromConnection_NonSmb_WithIconService_UsesServerGlyph()
    {
        Span.Services.IconService.Current = new Span.Services.IconService();

        var conn = new Span.Models.ConnectionInfo
        {
            Protocol = Span.Models.RemoteProtocol.SFTP,
            Host = "host"
        };

        var drive = Span.Models.DriveItem.FromConnection(conn);

        Assert.AreEqual("\uEE71", drive.IconGlyph); // stub ServerGlyph
    }

    [TestCleanup]
    public void Cleanup()
    {
        Span.Services.IconService.Current = null;
    }
}
