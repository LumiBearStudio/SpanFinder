using System;
using System.IO;
using System.Runtime.InteropServices;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// OneDrive 등 클라우드 동기화 상태를 감지하는 서비스.
    /// Win32 파일 속성(FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS, PINNED, UNPINNED)으로 판별.
    /// </summary>
    public class CloudSyncService
    {
        // Win32 파일 속성 상수
        private const uint FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS = 0x00400000;
        private const uint FILE_ATTRIBUTE_RECALL_ON_OPEN = 0x00040000;
        private const uint FILE_ATTRIBUTE_PINNED = 0x00080000;
        private const uint FILE_ATTRIBUTE_UNPINNED = 0x00100000;
        private const uint FILE_ATTRIBUTE_OFFLINE = 0x00001000;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern uint GetFileAttributesW(string lpFileName);

        private string? _oneDrivePath;
        private bool _oneDriveChecked;

        /// <summary>
        /// OneDrive 경로를 감지하여 캐시.
        /// </summary>
        public string? OneDrivePath
        {
            get
            {
                if (!_oneDriveChecked)
                {
                    _oneDriveChecked = true;
                    _oneDrivePath = DetectOneDrivePath();
                }
                return _oneDrivePath;
            }
        }

        /// <summary>
        /// 지정된 파일/폴더가 클라우드 폴더 내에 있는지 확인.
        /// </summary>
        public bool IsCloudPath(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(OneDrivePath))
                return false;

            return path.StartsWith(OneDrivePath, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 파일/폴더의 클라우드 동기화 상태를 반환.
        /// </summary>
        public CloudState GetCloudState(string path)
        {
            if (!IsCloudPath(path))
                return CloudState.None;

            try
            {
                uint attrs = GetFileAttributesW(path);
                if (attrs == 0xFFFFFFFF) // INVALID_FILE_ATTRIBUTES
                    return CloudState.None;

                // Cloud-only: 파일이 로컬에 없고 클라우드에만 존재
                if ((attrs & FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS) != 0 ||
                    (attrs & FILE_ATTRIBUTE_RECALL_ON_OPEN) != 0 ||
                    (attrs & FILE_ATTRIBUTE_OFFLINE) != 0)
                {
                    return CloudState.CloudOnly;
                }

                // Unpinned: 온디맨드 (사용 후 로컬 삭제 가능)
                if ((attrs & FILE_ATTRIBUTE_UNPINNED) != 0)
                {
                    return CloudState.CloudOnly;
                }

                // Pinned: 항상 로컬 유지
                if ((attrs & FILE_ATTRIBUTE_PINNED) != 0)
                {
                    return CloudState.Synced;
                }

                // 클라우드 경로이지만 특별한 속성 없음 → 동기화됨
                return CloudState.Synced;
            }
            catch
            {
                return CloudState.None;
            }
        }

        /// <summary>
        /// OneDrive 아이콘 글리프 반환.
        /// </summary>
        public static string GetCloudStateGlyph(CloudState state)
        {
            return state switch
            {
                CloudState.Synced => "\uE73E",       // Checkmark
                CloudState.CloudOnly => "\uE753",     // Cloud
                CloudState.PendingUpload => "\uE898",  // Upload
                CloudState.Syncing => "\uE895",        // Sync
                _ => string.Empty
            };
        }

        private static string? DetectOneDrivePath()
        {
            // 1. 환경변수 확인
            var oneDrive = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(oneDrive) && Directory.Exists(oneDrive))
                return oneDrive;

            var oneDriveConsumer = Environment.GetEnvironmentVariable("OneDriveConsumer");
            if (!string.IsNullOrEmpty(oneDriveConsumer) && Directory.Exists(oneDriveConsumer))
                return oneDriveConsumer;

            var oneDriveCommercial = Environment.GetEnvironmentVariable("OneDriveCommercial");
            if (!string.IsNullOrEmpty(oneDriveCommercial) && Directory.Exists(oneDriveCommercial))
                return oneDriveCommercial;

            // 2. 기본 경로 확인
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var defaultPath = Path.Combine(userProfile, "OneDrive");
            if (Directory.Exists(defaultPath))
                return defaultPath;

            return null;
        }
    }
}
