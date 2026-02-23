using System.Threading.Tasks;

namespace Span.Services
{
    public interface IShellService
    {
        Task OpenWithAsync(string filePath);
        void ShowProperties(string path);
        void OpenInExplorer(string path);
        void CopyPathToClipboard(string path);
        void OpenTerminal(string directoryPath, string terminalType = "wt");
    }
}
