using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using Span.Models;
using Span.Services;
using System.Linq;
using System.Threading.Tasks;

namespace Span.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _appTitle = "Span";

        public ObservableCollection<TabItem> Tabs { get; } = new();
        public ObservableCollection<DriveItem> Drives { get; } = new();

        // Engine
        private ExplorerViewModel _explorer;
        public ExplorerViewModel Explorer
        {
            get => _explorer;
            set => SetProperty(ref _explorer, value);
        }

        private readonly FileSystemService _fileService;

        public MainViewModel(FileSystemService fileService)
        {
            _fileService = fileService;
            Initialize();
        }

        private void Initialize()
        {
            // Dummy tabs
            Tabs.Add(new TabItem { Header = "Project Span", Icon = "\uEA34" }); // ri-apps-2-fill

            // Initialize Engine with a conceptual Root or just empty
            // To make sure UI binds correctly, we start with a dummy or a specific path if possible.
            // Let's start with "My Computer" concept or just C:\
            var root = new FolderItem { Name = "PC", Path = "PC" }; /* Virtual Root */
            Explorer = new ExplorerViewModel(root, _fileService);

            // Populate Sidebar
            LoadDrives();
        }

        private async void LoadDrives()
        {
            Drives.Clear();
            var drives = await _fileService.GetDrivesAsync();
            foreach (var drive in drives)
            {
                Drives.Add(drive);
            }
        }

        [RelayCommand]
        public void OpenDrive(DriveItem drive)
        {
            // When a drive is clicked, navigate Explorer to it.
            var driveRoot = new FolderItem
            {
                Name = drive.Name,
                Path = drive.Path
            };

            // Re-initialize Explorer or Navigate?
            // Since we want to clear previous columns and start fresh from this drive:
            Explorer.NavigateTo(driveRoot);
        }
    }
}
