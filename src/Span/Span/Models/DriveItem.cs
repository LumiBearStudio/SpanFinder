using System;

namespace Span.Models
{
    /// <summary>
    /// 사이드바에 표시되는 드라이브 항목 (로컬 HDD/SSD, USB, 네트워크, 클라우드, 원격 연결).
    /// <see cref="Services.FileSystemService"/>가 시스템 드라이브를 열거하고,
    /// <see cref="ConnectionInfo.FromConnection"/>으로 원격 연결도 DriveItem으로 변환된다.
    /// </summary>
    public class DriveItem
    {
        private string _name = string.Empty;
        public string Name
        {
            get => _name;
            set => _name = FileItem.NfcNormalize(value);
        }
        public string Path { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public long TotalSize { get; set; }
        public long AvailableFreeSpace { get; set; }
        public string DriveFormat { get; set; } = string.Empty;
        public string DriveType { get; set; } = string.Empty;
        public string IconGlyph { get; set; } = "\uEDFA"; // ri-hard-drive-fill, always overridden by FileSystemService/IconService

        /// <summary>
        /// SFTP/FTP 원격 연결 여부
        /// </summary>
        public bool IsRemoteConnection { get; set; }

        /// <summary>
        /// 클라우드 스토리지 프로바이더 여부 (iCloud, OneDrive, Dropbox 등)
        /// </summary>
        public bool IsCloudStorage { get; set; }

        /// <summary>
        /// ConnectionInfo.Id (원격 연결 식별)
        /// </summary>
        public string? ConnectionId { get; set; }

        /// <summary>
        /// 네트워크 위치(Network Shortcuts) 바로가기 폴더 경로.
        /// 연결 해제 시 이 폴더를 삭제한다.
        /// </summary>
        public string? NetworkShortcutPath { get; set; }

        /// <summary>
        /// 네트워크 위치(바로가기) 여부 — 드라이브 매핑이 아닌 Network Shortcuts 폴더 항목
        /// </summary>
        public bool IsNetworkShortcut => !string.IsNullOrEmpty(NetworkShortcutPath);

        /// <summary>
        /// 인증 필요 여부 (FTP 네트워크 바로가기 중 저장된 연결이 없는 경우).
        /// 사이드바에서 잠금 아이콘 오버레이 표시용.
        /// </summary>
        public bool NeedsAuth { get; set; }

        /// <summary>잠금 뱃지 Visibility (x:Bind용)</summary>
        public Microsoft.UI.Xaml.Visibility AuthBadgeVisibility =>
            NeedsAuth ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;

        public bool IsNetworkDrive => IsRemoteConnection || DriveType == "Network";

        /// <summary>
        /// 사이드바 ToolTip용: 이름 + 용량 정보 (용량이 없으면 이름만)
        /// </summary>
        public string TooltipText => string.IsNullOrEmpty(SizeDescription)
            ? Name : $"{Name}\n{SizeDescription}";

        /// <summary>
        /// Usage percentage (0-100). Returns 0 if TotalSize is 0.
        /// </summary>
        public double UsagePercent =>
            TotalSize > 0 ? Math.Round((double)(TotalSize - AvailableFreeSpace) / TotalSize * 100, 1) : 0;

        /// <summary>
        /// Human-readable size description: "X GB free of Y GB"
        /// </summary>
        public string SizeDescription
        {
            get
            {
                if (TotalSize <= 0) return string.Empty;
                return $"{FormatSize(AvailableFreeSpace)} free of {FormatSize(TotalSize)}";
            }
        }

        /// <summary>
        /// ConnectionInfo에서 DriveItem 생성 (사이드바 통합 표시용)
        /// </summary>
        public static DriveItem FromConnection(ConnectionInfo conn) => new()
        {
            Name = conn.DisplayName,
            Path = conn.Protocol == RemoteProtocol.SMB
                ? conn.UncPath ?? string.Empty
                : conn.ToUri(),
            IconGlyph = conn.Protocol == RemoteProtocol.SMB
                ? Span.Services.IconService.Current?.NetworkGlyph ?? "\uEDCF"
                : Span.Services.IconService.Current?.ServerGlyph ?? "\uF0DF",
            IsRemoteConnection = true,
            ConnectionId = conn.Id
        };

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1L << 40) return $"{bytes / (double)(1L << 40):F1} TB";
            if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):F1} GB";
            if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):F1} MB";
            return $"{bytes / (double)(1L << 10):F1} KB";
        }
    }
}
