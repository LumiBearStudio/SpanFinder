namespace Span.Models
{
    public enum RemoteProtocol
    {
        SFTP,
        FTP,
        FTPS,
        SMB
    }

    public enum AuthMethod
    {
        Password,
        SshKey
    }

    public class ConnectionInfo
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string DisplayName { get; set; } = string.Empty;
        public RemoteProtocol Protocol { get; set; } = RemoteProtocol.SFTP;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 22;
        public string Username { get; set; } = string.Empty;
        public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;
        public string? SshKeyPath { get; set; }
        public string RemotePath { get; set; } = "/";
        public DateTime LastConnected { get; set; }

        /// <summary>
        /// SMB 전용: UNC 경로 (예: \\server\share)
        /// </summary>
        public string? UncPath { get; set; }

        public static int GetDefaultPort(RemoteProtocol protocol) => protocol switch
        {
            RemoteProtocol.SFTP => 22,
            RemoteProtocol.FTP => 21,
            RemoteProtocol.FTPS => 990,
            RemoteProtocol.SMB => 445,
            _ => 22
        };

        public string ToUri() => Protocol == RemoteProtocol.SMB
            ? UncPath ?? string.Empty
            : $"{Protocol.ToString().ToLower()}://{Username}@{Host}:{Port}{RemotePath}";
    }
}
