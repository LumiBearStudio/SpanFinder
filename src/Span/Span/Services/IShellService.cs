using System.Threading.Tasks;

namespace Span.Services
{
    /// <summary>
    /// Windows Shell 통합 서비스 인터페이스. 파일 실행, 연결 프로그램,
    /// 속성 표시, 터미널 열기, 드라이브 꺼내기 등 OS 수준 기능을 담당한다.
    /// </summary>
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
