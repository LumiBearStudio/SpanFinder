using System.Threading.Tasks;

namespace Span.Services
{
    public interface IShellService
    {
        void OpenFile(string filePath);
        Task OpenWithAsync(string filePath);
        void ShowProperties(string path);
        void OpenInExplorer(string path);
        void CopyPathToClipboard(string path);
        void OpenTerminal(string directoryPath, string terminalType = "wt");
        void EjectDrive(string drivePath);
        bool DisconnectNetworkDrive(string drivePath);
    }
}
