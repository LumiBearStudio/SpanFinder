using Span.Models;
using System.Collections.Generic;

namespace Span.Services
{
    public interface IActionLogService
    {
        void LogOperation(ActionLogEntry entry);
        List<ActionLogEntry> GetEntries(int count = 50);
        void Clear();
    }
}
