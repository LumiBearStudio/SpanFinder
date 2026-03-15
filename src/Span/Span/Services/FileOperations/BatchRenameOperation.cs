using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using static Span.Services.LocalizationService;

namespace Span.Services.FileOperations;

/// <summary>
/// 배치 이름 변경 작업. 여러 파일/폴더의 이름을 한 번에 변경.
/// Undo 지원.
/// </summary>
public class BatchRenameOperation : IFileOperation
{
    private readonly List<(string OldPath, string NewName)> _renames;
    private readonly List<(string OldPath, string NewPath)> _executedRenames = new();

    public BatchRenameOperation(List<(string OldPath, string NewName)> renames)
    {
        _renames = renames ?? throw new ArgumentNullException(nameof(renames));
    }

    public string Description => string.Format(L("FileOp_BatchRename"), _renames.Count);
    public bool CanUndo => _executedRenames.Count > 0;

    public async Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        _executedRenames.Clear();
        int total = _renames.Count;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (oldPath, newName) = _renames[i];
            var dir = Path.GetDirectoryName(oldPath);
            if (string.IsNullOrEmpty(dir)) continue;

            var newPath = Path.Combine(dir, newName);

            progress?.Report(new FileOperationProgress
            {
                CurrentFile = Path.GetFileName(oldPath),
                CurrentFileIndex = i + 1,
                TotalFileCount = total,
                Percentage = (int)((i + 1) * 100.0 / total)
            });

            try
            {
                if (oldPath.Equals(newPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (File.Exists(oldPath))
                    File.Move(oldPath, newPath);
                else if (Directory.Exists(oldPath))
                    Directory.Move(oldPath, newPath);
                else
                    continue;

                _executedRenames.Add((oldPath, newPath));
            }
            catch (Exception ex)
            {
                // 부분 실패: 이미 변경된 것들의 Undo는 가능
                return OperationResult.CreateFailure(
                    string.Format(L("FileOp_RenameFailed"), Path.GetFileName(oldPath), ex.Message));
            }
        }

        return OperationResult.CreateSuccess(string.Format(L("FileOp_ItemsRenamed"), _executedRenames.Count));
    }

    public async Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        // 역순으로 되돌리기
        for (int i = _executedRenames.Count - 1; i >= 0; i--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (oldPath, newPath) = _executedRenames[i];

            try
            {
                if (File.Exists(newPath))
                    File.Move(newPath, oldPath);
                else if (Directory.Exists(newPath))
                    Directory.Move(newPath, oldPath);
            }
            catch (Exception ex)
            {
                return OperationResult.CreateFailure(string.Format(L("FileOp_UndoFailed"), Path.GetFileName(newPath), ex.Message));
            }
        }

        var count = _executedRenames.Count;
        _executedRenames.Clear();
        return OperationResult.CreateSuccess(string.Format(L("FileOp_ItemsReverted"), count));
    }
}
