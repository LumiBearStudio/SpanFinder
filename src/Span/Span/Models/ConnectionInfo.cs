namespace Span.Models
{
    /// <summary>
    /// 원격 파일 시스템 접속 프로토콜.
    /// </summary>
    public enum RemoteProtocol
    {
        /// <summary>SSH File Transfer Protocol (포트 22).</summary>
        SFTP,
        /// <summary>File Transfer Protocol (포트 21).</summary>
        FTP,
        /// <summary>FTP over TLS/SSL (포트 990).</summary>
        FTPS,
        /// <summary>Server Message Block / Windows 파일 공유 (포트 445).</summary>
        SMB
    }

    /// <summary>
    /// 원격 연결 인증 방식.
    /// </summary>
    public enum AuthMethod
    {
        /// <summary>비밀번호 인증.</summary>
        Password,
        /// <summary>SSH 키 파일 인증.</summary>
        SshKey
    }

    /// <summary>
    /// 원격 연결(SFTP/FTP/FTPS/SMB) 설정 정보.
    /// <see cref="Services.ConnectionManagerService"/>가 JSON으로 영속화하며,
    /// 사이드바에서 DriveItem으로 변환되어 표시된다.
    /// </summary>
    public class ConnectionInfo
    {
        /// <summary>고유 식별자 (GUID 기반, 32자 hex).</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>사용자에게 표시되는 연결 이름.</summary>
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>연결 프로토콜.</summary>
        public RemoteProtocol Protocol { get; set; } = RemoteProtocol.SFTP;

        /// <summary>서버 호스트명 또는 IP.</summary>
        public string Host { get; set; } = string.Empty;

        /// <summary>포트 번호 (프로토콜별 기본값: SFTP=22, FTP=21, FTPS=990, SMB=445).</summary>
        public int Port { get; set; } = 22;

        /// <summary>접속 사용자명.</summary>
        public string Username { get; set; } = string.Empty;

        /// <summary>인증 방식 (비밀번호 또는 SSH 키).</summary>
        public AuthMethod AuthMethod { get; set; } = AuthMethod.Password;

        /// <summary>SSH 키 파일 로컬 경로 (AuthMethod.SshKey인 경우).</summary>
        public string? SshKeyPath { get; set; }

        /// <summary>원격 시작 경로 (기본: "/").</summary>
        public string RemotePath { get; set; } = "/";

        /// <summary>마지막 접속 시각.</summary>
        public DateTime LastConnected { get; set; }

        /// <summary>
        /// SMB 전용: UNC 경로 (예: \\server\share)
        /// </summary>
        public string? UncPath { get; set; }

        /// <summary>
        /// FTPS: 신뢰된 서버 인증서 SHA-256 썸프린트
        /// </summary>
        public string? TrustedCertThumbprint { get; set; }

        /// <summary>
        /// SFTP: 신뢰된 호스트키 핑거프린트 (SHA-256)
        /// </summary>
        public string? TrustedHostKeyFingerprint { get; set; }

        /// <summary>
        /// 프로토콜별 기본 포트 번호를 반환한다.
        /// </summary>
        public static int GetDefaultPort(RemoteProtocol protocol) => protocol switch
        {
            RemoteProtocol.SFTP => 22,
            RemoteProtocol.FTP => 21,
            RemoteProtocol.FTPS => 990,
            RemoteProtocol.SMB => 445,
            _ => 22
        };

        /// <summary>
        /// 연결 정보를 URI 문자열로 변환한다. SMB는 UNC 경로, 나머지는 "protocol://user@host:port/path" 형식.
        /// </summary>
        public string ToUri() => Protocol == RemoteProtocol.SMB
            ? UncPath ?? string.Empty
            : $"{Protocol.ToString().ToLower()}://{Username}@{Host}:{Port}{RemotePath}";
    }
}
