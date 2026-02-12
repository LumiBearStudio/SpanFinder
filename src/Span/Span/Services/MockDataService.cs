using Span.Models;
using System;
using System.Collections.Generic;

namespace Span.Services
{
    public class MockDataService
    {
        private FolderItem _root;

        public MockDataService()
        {
            InitializeData();
        }

        public FolderItem GetRoot()
        {
            return _root;
        }

        private void InitializeData()
        {
            // Root (Virtual)
            _root = new FolderItem { Name = "Root", Path = "Root" };

            // Drives
            var cDrive = new FolderItem { Name = "C:", Path = @"C:\", DateModified = DateTime.Now };
            var dDrive = new FolderItem { Name = "D:", Path = @"D:\", DateModified = DateTime.Now };

            _root.SubFolders.Add(cDrive);
            _root.SubFolders.Add(dDrive);
            _root.Children.Add(cDrive);
            _root.Children.Add(dDrive);

            // Populate C: with mock data structure from app.js
            // 'default' (representing a project root like 'Span')
            var spanProject = new FolderItem { Name = "Span", Path = @"C:\Users\Dev\Span", DateModified = DateTime.Now };
            cDrive.SubFolders.Add(spanProject);
            cDrive.Children.Add(spanProject);

            AddMockContent(spanProject);
        }

        private void AddMockContent(FolderItem root)
        {
            // Based on app.js 'mockFolders'

            // 1. Folders
            var bin = CreateFolder("bin", root);
            var obj = CreateFolder("obj", root);
            var properties = CreateFolder("Properties", root);
            var controllers = CreateFolder("Controllers", root);
            var assets = CreateFolder("Assets", root);
            var models = CreateFolder("Models", root);
            var services = CreateFolder("Services", root);
            var viewmodels = CreateFolder("ViewModels", root);
            var views = CreateFolder("Views", root);

            // 2. Files in Root
            CreateFile("Program.cs", "cs", root);
            CreateFile("Startup.cs", "cs", root);
            CreateFile("appsettings.json", "json", root);

            // Sub-content (Sample for dept)

            // Properties
            CreateFile("launchSettings.json", "json", properties);
            CreateFile("AssemblyInfo.cs", "cs", properties);

            // Controllers
            CreateFile("HomeController.cs", "cs", controllers);
            CreateFile("ApiController.cs", "cs", controllers);

            // Assets
            CreateFolder("Icons", assets);
            CreateFolder("Fonts", assets);
            CreateFile("app-icon.png", "image", assets);
            CreateFile("splash.png", "image", assets);

            // Models
            CreateFile("DriveItem.cs", "cs", models);
            CreateFile("FileItem.cs", "cs", models);
            CreateFile("FolderItem.cs", "cs", models);

            // Services
            CreateFile("FileService.cs", "cs", services);
            CreateFile("MockDataService.cs", "cs", services);

            // ViewModels
            CreateFile("MainViewModel.cs", "cs", viewmodels);
            CreateFile("FolderViewModel.cs", "cs", viewmodels);

            // Views
            CreateFile("MainWindow.xaml", "xaml", views);
            CreateFile("MainWindow.xaml.cs", "cs", views);

            // Deep branching for testing
            var debug = CreateFolder("Debug", bin);
            CreateFolder("net6.0", debug);
        }

        private FolderItem CreateFolder(string name, FolderItem parent)
        {
            var folder = new FolderItem
            {
                Name = name,
                Path = System.IO.Path.Combine(parent.Path, name),
                DateModified = DateTime.Now
            };
            parent.SubFolders.Add(folder);
            parent.Children.Add(folder);
            return folder;
        }

        private void CreateFile(string name, string type, FolderItem parent)
        {
            var file = new FileItem
            {
                Name = name,
                Path = System.IO.Path.Combine(parent.Path, name),
                FileType = type,
                DateModified = DateTime.Now,
                Size = 1024 // Dummy
            };
            parent.Files.Add(file);
            parent.Children.Add(file);
        }
    }
}
