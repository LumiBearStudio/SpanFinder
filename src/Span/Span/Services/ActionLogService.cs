using Span.Models;
using System.Text.Json;

namespace Span.Services;

public class ActionLogService : IActionLogService
{
    private const int MaxEntries = 1000;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private List<ActionLogEntry> _entries = new();
    private bool _loaded = false;

    public ActionLogService()
    {
        var appDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SPAN Finder");
        Directory.CreateDirectory(appDataDir);
        _logFilePath = Path.Combine(appDataDir, "action_log.json");
    }

    public void LogOperation(ActionLogEntry entry)
    {
        entry.Timestamp = DateTime.Now;

        lock (_lock)
        {
            EnsureLoaded();
            _entries.Add(entry);

            // FIFO: trim to max entries
            if (_entries.Count > MaxEntries)
                _entries = _entries.Skip(_entries.Count - MaxEntries).ToList();
        }

        // Write async on thread pool
        Task.Run(() => SaveToFile());
    }

    public List<ActionLogEntry> GetEntries(int count = 50)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _entries.TakeLast(count).Reverse().ToList();
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
        }
        Task.Run(() => SaveToFile());
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;

        try
        {
            if (File.Exists(_logFilePath))
            {
                var json = File.ReadAllText(_logFilePath);
                _entries = JsonSerializer.Deserialize<List<ActionLogEntry>>(json) ?? new();
            }
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ActionLogService] Load error: {ex.Message}");
            _entries = new();
        }
    }

    private void SaveToFile()
    {
        try
        {
            List<ActionLogEntry> snapshot;
            lock (_lock)
            {
                snapshot = new List<ActionLogEntry>(_entries);
            }

            var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_logFilePath, json);
        }
        catch (Exception ex)
        {
            Helpers.DebugLogger.Log($"[ActionLogService] Save error: {ex.Message}");
        }
    }
}
