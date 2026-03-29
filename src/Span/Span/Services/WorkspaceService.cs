using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Span.Helpers;
using Span.Models;

namespace Span.Services
{
    /// <summary>
    /// 워크스페이스(탭 세트) 저장/복원 서비스.
    /// JSON 파일로 영구 저장하며, SemaphoreSlim + 원자적 쓰기(tmp -> rename)로 데이터 안전성 보장.
    /// </summary>
    public class WorkspaceService
    {
        private const string WorkspacesFileName = "workspaces.json";
        private const string WorkspacesTempFileName = "workspaces.json.tmp";
        private const string AutoSaveId = "_autosave";

        private readonly string _filePath;
        private readonly string _tempPath;
        private readonly SemaphoreSlim _lock = new(1, 1);
        private List<WorkspaceDto> _workspaces = new();
        private bool _loaded;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public WorkspaceService()
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SPAN Finder");
            Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, WorkspacesFileName);
            _tempPath = Path.Combine(folder, WorkspacesTempFileName);
        }

        /// <summary>
        /// 디스크에서 워크스페이스 목록 로드 (lazy, 최초 1회).
        /// </summary>
        public async Task<List<WorkspaceDto>> GetWorkspacesAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    await LoadFromDiskAsync();
                    _loaded = true;
                }
                // AutoSave 제외한 목록 반환
                return _workspaces.Where(w => w.Id != AutoSaveId).ToList();
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 워크스페이스 저장 (신규) 또는 갱신 (Id 기준).
        /// </summary>
        public async Task SaveWorkspaceAsync(WorkspaceDto workspace)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    await LoadFromDiskAsync();
                    _loaded = true;
                }

                var index = _workspaces.FindIndex(w => w.Id == workspace.Id);
                if (index >= 0)
                    _workspaces[index] = workspace;
                else
                    _workspaces.Add(workspace);

                await WriteToDiskAsync();
                DebugLogger.Log($"[WorkspaceService] Saved workspace: {workspace.Name} ({workspace.Tabs.Count} tabs)");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorkspaceService] ERROR saving workspace: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Id로 워크스페이스 삭제.
        /// </summary>
        public async Task DeleteWorkspaceAsync(string id)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    await LoadFromDiskAsync();
                    _loaded = true;
                }

                var removed = _workspaces.RemoveAll(w => w.Id == id);
                if (removed > 0)
                {
                    await WriteToDiskAsync();
                    DebugLogger.Log($"[WorkspaceService] Deleted workspace: {id}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorkspaceService] ERROR deleting workspace: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Id로 워크스페이스 이름 변경.
        /// </summary>
        public async Task RenameWorkspaceAsync(string id, string newName)
        {
            await _lock.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    await LoadFromDiskAsync();
                    _loaded = true;
                }

                var index = _workspaces.FindIndex(w => w.Id == id);
                if (index >= 0)
                {
                    var old = _workspaces[index];
                    _workspaces[index] = old with { Name = newName };
                    await WriteToDiskAsync();
                    DebugLogger.Log($"[WorkspaceService] Renamed workspace {id}: {old.Name} -> {newName}");
                }
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorkspaceService] ERROR renaming workspace: {ex.Message}");
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 자동 저장: 워크스페이스 전환 전 현재 세션을 "_autosave"로 백업.
        /// </summary>
        public async Task AutoSaveAsync(List<TabStateDto> tabs, int activeIndex)
        {
            var workspace = new WorkspaceDto(
                AutoSaveId,
                "Auto Save",
                tabs,
                activeIndex,
                DateTime.UtcNow,
                DateTime.UtcNow
            );
            await SaveWorkspaceAsync(workspace);
        }

        /// <summary>
        /// 자동 저장된 워크스페이스 반환. 없으면 null.
        /// </summary>
        public async Task<WorkspaceDto?> GetAutoSaveAsync()
        {
            await _lock.WaitAsync();
            try
            {
                if (!_loaded)
                {
                    await LoadFromDiskAsync();
                    _loaded = true;
                }
                return _workspaces.FirstOrDefault(w => w.Id == AutoSaveId);
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// 디스크에서 JSON 파일 로드. 파일 없으면 빈 목록.
        /// </summary>
        private async Task LoadFromDiskAsync()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    _workspaces = new List<WorkspaceDto>();
                    return;
                }

                var json = await Task.Run(() => File.ReadAllText(_filePath, Encoding.UTF8));
                if (string.IsNullOrWhiteSpace(json))
                {
                    _workspaces = new List<WorkspaceDto>();
                    return;
                }

                _workspaces = JsonSerializer.Deserialize<List<WorkspaceDto>>(json, _jsonOptions)
                              ?? new List<WorkspaceDto>();
                DebugLogger.Log($"[WorkspaceService] Loaded {_workspaces.Count} workspaces from disk");
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorkspaceService] ERROR loading workspaces: {ex.Message}");
                _workspaces = new List<WorkspaceDto>();
            }
        }

        /// <summary>
        /// 원자적 쓰기: tmp 파일에 쓴 후 rename.
        /// </summary>
        private async Task WriteToDiskAsync()
        {
            try
            {
                var snapshot = _workspaces.ToList();
                var json = JsonSerializer.Serialize(snapshot, _jsonOptions);

                await Task.Run(() =>
                {
                    // Step 1: tmp 파일에 쓰기
                    File.WriteAllText(_tempPath, json, Encoding.UTF8);

                    // Step 2: tmp -> 메인 파일로 원자적 이동
                    File.Move(_tempPath, _filePath, overwrite: true);
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Log($"[WorkspaceService] ERROR writing to disk: {ex.Message}");
                // tmp 파일 정리
                try { if (File.Exists(_tempPath)) File.Delete(_tempPath); } catch { }
            }
        }
    }
}
