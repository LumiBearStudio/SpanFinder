using System.IO.Compression;

namespace Span.Services.FileOperations;

/// <summary>
/// ZIP 압축 작업. 파일 또는 폴더를 ZIP으로 압축.
/// </summary>
public class CompressOperation : IFileOperation
{
    private readonly string[] _sourcePaths;
    private readonly string _zipPath;

    public CompressOperation(string[] sourcePaths, string zipPath)
    {
        _sourcePaths = sourcePaths;
        _zipPath = zipPath;
    }

    public string Description => $"Compress to '{Path.GetFileName(_zipPath)}'";
    public bool CanUndo => true;

    public Task<OperationResult> ExecuteAsync(
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = ZipFile.Open(_zipPath, ZipArchiveMode.Create);

            int total = _sourcePaths.Length;
            int current = 0;

            foreach (var sourcePath in _sourcePaths)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (File.Exists(sourcePath))
                {
                    archive.CreateEntryFromFile(sourcePath, Path.GetFileName(sourcePath), CompressionLevel.Optimal);
                }
                else if (Directory.Exists(sourcePath))
                {
                    AddDirectoryToZip(archive, sourcePath, Path.GetFileName(sourcePath), cancellationToken);
                }

                current++;
                progress?.Report(new FileOperationProgress
                {
                    CurrentFile = Path.GetFileName(sourcePath),
                    CurrentFileIndex = current,
                    TotalFileCount = total
                });
            }

            return Task.FromResult(OperationResult.CreateSuccess(_zipPath));
        }
        catch (OperationCanceledException)
        {
            // Cleanup partial zip
            try { if (File.Exists(_zipPath)) File.Delete(_zipPath); } catch { }
            return Task.FromResult(OperationResult.CreateFailure("Operation cancelled"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }

    public Task<OperationResult> UndoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (File.Exists(_zipPath))
            {
                File.Delete(_zipPath);
                return Task.FromResult(OperationResult.CreateSuccess(_zipPath));
            }
            return Task.FromResult(OperationResult.CreateFailure("ZIP file does not exist"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(OperationResult.CreateFailure(ex.Message));
        }
    }

    private static void AddDirectoryToZip(ZipArchive archive, string dirPath, string entryBase, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            string relativePath = Path.GetRelativePath(Path.GetDirectoryName(dirPath)!, file);
            archive.CreateEntryFromFile(file, relativePath, CompressionLevel.Optimal);
        }
    }
}
