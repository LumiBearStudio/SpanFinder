using Span.Models;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// 파일 시스템 기본 서비스 인터페이스.
    /// 드라이브 목록 조회 및 경로 내 파일/폴더 항목을 비동기로 로드한다.
    /// </summary>
    public interface IFileSystemService
    {
        /// <summary>시스템 드라이브 목록을 비동기로 반환한다.</summary>
        Task<List<DriveItem>> GetDrivesAsync();

        /// <summary>지정 경로의 파일/폴더 목록을 비동기로 반환한다.</summary>
        Task<List<IFileSystemItem>> GetItemsAsync(string path);
    }
}
