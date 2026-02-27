using Span.Models;
using System.Collections.Generic;

namespace Span.Services
{
    /// <summary>
    /// 파일 조작 이력 관리 서비스 인터페이스.
    /// Copy/Move/Delete/Rename 등의 작업을 기록하고 조회할 수 있다.
    /// </summary>
    public interface IActionLogService
    {
        /// <summary>작업 로그 항목을 기록한다.</summary>
        void LogOperation(ActionLogEntry entry);

        /// <summary>최근 작업 로그를 조회한다 (기본 50개).</summary>
        List<ActionLogEntry> GetEntries(int count = 50);

        /// <summary>모든 로그를 삭제한다.</summary>
        void Clear();
    }
}
