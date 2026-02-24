using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// Cloud Files API 기반 클라우드 동기화 상태를 감지하는 서비스.
    /// Win32 파일 속성(FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS, PINNED, UNPINNED)으로 판별.
    /// OneDrive, iCloud, Dropbox 등 cfapi 프로바이더에 동작.
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

        /// <summary>
        /// 프로바이더 무관하게, 파일이 클라우드 전용(로컬에 데이터 없음)인지 확인.
        /// 데이터 읽기 시 다운로드가 트리거되는 파일을 식별.
        /// OneDrive, iCloud, Dropbox 등 모든 Cloud Files API 프로바이더에 동작.
        /// </summary>
        public static bool IsCloudOnlyFile(string path)
        {
            try
            {
                uint attrs = GetFileAttributesW(path);
                if (attrs == 0xFFFFFFFF) return false; // INVALID_FILE_ATTRIBUTES

                return (attrs & (FILE_ATTRIBUTE_RECALL_ON_DATA_ACCESS
                               | FILE_ATTRIBUTE_RECALL_ON_OPEN
                               | FILE_ATTRIBUTE_OFFLINE)) != 0;
            }
            catch
            {
                return false;
            }
        }

        private string? _oneDrivePath;
        private bool _oneDriveChecked;

        /// <summary>
        /// 등록된 모든 클라우드 루트 경로 (OneDrive, iCloud, Dropbox 등).
        /// 끝에 Path.DirectorySeparatorChar가 붙어있어 StartsWith 비교가 정확함.
        /// </summary>
        private readonly List<string> _cloudRoots = new();

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
        /// 클라우드 루트 경로를 등록. CloudStorageProviderService에서 감지한 경로를 전달.
        /// </summary>
        public void RegisterCloudRoot(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath)) return;

            // StartsWith 비교를 위해 끝에 구분자 보장
            var normalized = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                           + Path.DirectorySeparatorChar;

            lock (_cloudRoots)
            {
                foreach (var existing in _cloudRoots)
                {
                    if (existing.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                        return; // 중복 방지
                }
                _cloudRoots.Add(normalized);
            }

            Helpers.DebugLogger.Log($"[CloudSync] Registered cloud root: {rootPath}");
        }

        /// <summary>
        /// 지정된 파일/폴더가 클라우드 폴더 내에 있는지 확인.
        /// 등록된 모든 클라우드 루트와 OneDrive 경로를 검사.
        /// </summary>
        public bool IsCloudPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // 등록된 클라우드 루트 검사
            lock (_cloudRoots)
            {
                foreach (var root in _cloudRoots)
                {
                    if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            // 폴백: OneDrive 환경변수 경로
            if (!string.IsNullOrEmpty(OneDrivePath)
                && path.StartsWith(OneDrivePath, StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
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
