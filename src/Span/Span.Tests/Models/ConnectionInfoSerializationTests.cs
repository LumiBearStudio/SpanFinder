using System.Text.Json;
using Span.Models;

namespace Span.Tests.Models;

[TestClass]
public class ConnectionInfoSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ── SFTP round-trip ───────────────────────────────────

    [TestMethod]
    public void RoundTrip_Sftp_AllPropertiesPreserved()
    {
        var original = new ConnectionInfo
        {
            Id = "abc123",
            DisplayName = "My SFTP Server",
            Protocol = RemoteProtocol.SFTP,
            Host = "sftp.example.com",
            Port = 22,
            Username = "admin",
            AuthMethod = AuthMethod.SshKey,
            SshKeyPath = @"C:\Users\me\.ssh\id_rsa",
            RemotePath = "/home/admin",
            LastConnected = new DateTime(2025, 6, 15, 10, 30, 0, DateTimeKind.Utc),
            TrustedHostKeyFingerprint = "SHA256:abcdef1234567890"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.AreEqual(original.Id, deserialized.Id);
        Assert.AreEqual(original.DisplayName, deserialized.DisplayName);
        Assert.AreEqual(original.Protocol, deserialized.Protocol);
        Assert.AreEqual(original.Host, deserialized.Host);
        Assert.AreEqual(original.Port, deserialized.Port);
        Assert.AreEqual(original.Username, deserialized.Username);
        Assert.AreEqual(original.AuthMethod, deserialized.AuthMethod);
        Assert.AreEqual(original.SshKeyPath, deserialized.SshKeyPath);
        Assert.AreEqual(original.RemotePath, deserialized.RemotePath);
        Assert.AreEqual(original.LastConnected, deserialized.LastConnected);
        Assert.AreEqual(original.TrustedHostKeyFingerprint, deserialized.TrustedHostKeyFingerprint);
    }

    // ── FTP round-trip ────────────────────────────────────

    [TestMethod]
    public void RoundTrip_Ftp_AllPropertiesPreserved()
    {
        var original = new ConnectionInfo
        {
            Id = "ftp001",
            DisplayName = "FTP Mirror",
            Protocol = RemoteProtocol.FTP,
            Host = "ftp.mirror.org",
            Port = 21,
            Username = "anonymous",
            AuthMethod = AuthMethod.Password,
            RemotePath = "/pub/releases"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.AreEqual(original.Protocol, deserialized.Protocol);
        Assert.AreEqual(original.Host, deserialized.Host);
        Assert.AreEqual(original.Port, deserialized.Port);
        Assert.AreEqual(original.Username, deserialized.Username);
        Assert.AreEqual(original.RemotePath, deserialized.RemotePath);
    }

    // ── FTPS with TrustedCertThumbprint ───────────────────

    [TestMethod]
    public void RoundTrip_Ftps_WithTrustedCertThumbprint()
    {
        var original = new ConnectionInfo
        {
            Id = "ftps01",
            DisplayName = "Secure FTP",
            Protocol = RemoteProtocol.FTPS,
            Host = "ftps.secure.com",
            Port = 990,
            Username = "secure_user",
            AuthMethod = AuthMethod.Password,
            RemotePath = "/",
            TrustedCertThumbprint = "A1B2C3D4E5F6789012345678ABCDEF"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.AreEqual(RemoteProtocol.FTPS, deserialized.Protocol);
        Assert.AreEqual(original.TrustedCertThumbprint, deserialized.TrustedCertThumbprint);
    }

    // ── SMB with UncPath ──────────────────────────────────

    [TestMethod]
    public void RoundTrip_Smb_WithUncPath()
    {
        var original = new ConnectionInfo
        {
            Id = "smb01",
            DisplayName = "Office Share",
            Protocol = RemoteProtocol.SMB,
            Host = "fileserver",
            Port = 445,
            Username = "domain\\user",
            AuthMethod = AuthMethod.Password,
            UncPath = @"\\fileserver\shared"
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.AreEqual(RemoteProtocol.SMB, deserialized.Protocol);
        Assert.AreEqual(original.UncPath, deserialized.UncPath);
        Assert.AreEqual(original.Username, deserialized.Username);
    }

    // ── Default values survive round-trip ─────────────────

    [TestMethod]
    public void RoundTrip_DefaultValues_Preserved()
    {
        var original = new ConnectionInfo();
        var originalId = original.Id; // capture before serialize

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.AreEqual(originalId, deserialized.Id);
        Assert.AreEqual(string.Empty, deserialized.DisplayName);
        Assert.AreEqual(RemoteProtocol.SFTP, deserialized.Protocol);
        Assert.AreEqual(string.Empty, deserialized.Host);
        Assert.AreEqual(22, deserialized.Port);
        Assert.AreEqual(string.Empty, deserialized.Username);
        Assert.AreEqual(AuthMethod.Password, deserialized.AuthMethod);
        Assert.AreEqual("/", deserialized.RemotePath);
    }

    // ── Null optional properties round-trip ────────────────

    [TestMethod]
    public void RoundTrip_NullOptionalProperties_RemainNull()
    {
        var original = new ConnectionInfo
        {
            Id = "nulltest",
            SshKeyPath = null,
            UncPath = null,
            TrustedCertThumbprint = null,
            TrustedHostKeyFingerprint = null
        };

        var json = JsonSerializer.Serialize(original, Options);
        var deserialized = JsonSerializer.Deserialize<ConnectionInfo>(json, Options)!;

        Assert.IsNull(deserialized.SshKeyPath);
        Assert.IsNull(deserialized.UncPath);
        Assert.IsNull(deserialized.TrustedCertThumbprint);
        Assert.IsNull(deserialized.TrustedHostKeyFingerprint);
    }

    // ── List<ConnectionInfo> round-trip ────────────────────

    [TestMethod]
    public void RoundTrip_List_PreservesAllItems()
    {
        var list = new List<ConnectionInfo>
        {
            new() { Id = "item1", DisplayName = "Server A", Protocol = RemoteProtocol.SFTP, Host = "a.com" },
            new() { Id = "item2", DisplayName = "Server B", Protocol = RemoteProtocol.FTP, Host = "b.com" },
            new() { Id = "item3", DisplayName = "Server C", Protocol = RemoteProtocol.SMB, UncPath = @"\\c\share" }
        };

        var json = JsonSerializer.Serialize(list, Options);
        var deserialized = JsonSerializer.Deserialize<List<ConnectionInfo>>(json, Options)!;

        Assert.AreEqual(3, deserialized.Count);
        Assert.AreEqual("item1", deserialized[0].Id);
        Assert.AreEqual("Server B", deserialized[1].DisplayName);
        Assert.AreEqual(RemoteProtocol.SMB, deserialized[2].Protocol);
        Assert.AreEqual(@"\\c\share", deserialized[2].UncPath);
    }

    // ── Enum serialization ────────────────────────────────

    [TestMethod]
    public void EnumValues_SerializeAsNumbers()
    {
        var conn = new ConnectionInfo
        {
            Protocol = RemoteProtocol.FTPS,  // enum value 2
            AuthMethod = AuthMethod.SshKey    // enum value 1
        };

        var json = JsonSerializer.Serialize(conn, Options);

        // Default System.Text.Json serializes enums as integer values
        Assert.IsTrue(json.Contains("\"protocol\": 2"), $"Protocol FTPS should serialize as 2. JSON: {json}");
        Assert.IsTrue(json.Contains("\"authMethod\": 1"), $"AuthMethod SshKey should serialize as 1. JSON: {json}");
    }
}
