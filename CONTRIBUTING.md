# Contributing to SPAN Finder

Thank you for your interest in contributing to SPAN Finder! This guide will help you get started.

## Getting Started

### Prerequisites

- **Windows 10 (1903+)** or **Windows 11**
- **Visual Studio 2022** (17.8+) with the following workloads:
  - .NET Desktop Development
  - Windows App SDK (WinUI 3) C# Templates
- **.NET 8 SDK**
- **Windows App SDK 1.8**

### Build

```bash
# Clone the repository
git clone https://github.com/LumiBearStudio/SpanFinder.git
cd SpanFinder

# Build (x64)
dotnet build src/Span/Span/Span.csproj -p:Platform=x64

# Run unit tests
dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64
```

> **Note**: WinUI 3 apps cannot be launched via `dotnet run`. You must use **Visual Studio F5** (MSIX packaging required) to run the app.

### MSIX Certificate

The `Package.appxmanifest` contains a placeholder `Publisher` value. For local development, Visual Studio will prompt you to create a temporary certificate on first build. This is normal — each developer uses their own certificate.

## How to Contribute

### Reporting Bugs

- Use the [Bug Report](https://github.com/LumiBearStudio/SpanFinder/issues/new?template=bug_report.md) issue template
- Include your Windows version and SPAN Finder version
- Provide steps to reproduce the issue
- Attach screenshots if applicable

### Suggesting Features

- Use the [Feature Request](https://github.com/LumiBearStudio/SpanFinder/issues/new?template=feature_request.md) issue template
- Describe the use case and expected behavior
- Check existing issues to avoid duplicates

### Submitting Pull Requests

1. **Fork** the repository and create a branch from `main`
2. Follow the coding conventions below
3. Ensure `dotnet build` completes with **0 errors**
4. Run unit tests: `dotnet test src/Span/Span.Tests/Span.Tests.csproj -p:Platform=x64`
5. Submit a PR with a clear description of the changes

## Coding Conventions

- **MVVM pattern** with CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`)
- **Naming**: ViewModels → `{Name}ViewModel.cs`, Models → `{Name}Item.cs`, Converters → `{Purpose}Converter.cs`
- Use `x:Bind` (compile-time) over `Binding` in XAML
- Service methods return `Task<T>`, never `void` (except UI event handlers)
- Path comparison: always `StringComparison.OrdinalIgnoreCase`
- Event handlers: unsubscribe (`-=`) before subscribe (`+=`) to prevent accumulation

## Project Structure

```
src/Span/
├── Span/                    # Main application
│   ├── Models/              # Data models (IFileSystemItem, TabItem, etc.)
│   ├── ViewModels/          # MVVM ViewModels
│   ├── Views/               # XAML Views and Controls
│   ├── Services/            # 40+ service classes
│   ├── Helpers/             # Converters, utilities
│   └── Assets/              # Icons and images
├── Span.Tests/              # Unit tests (MSTest + Moq)
└── Span.UITests/            # UI automation tests (FlaUI)
```

## License

By contributing, you agree that your contributions will be licensed under the [GPL v3.0](LICENSE.md).

## Trademark Notice

The "SPAN Finder" name and official logo are trademarks of LumiBear Studio. If you fork this project, you **must** use a different name and replace all logo assets. See [LICENSE.md](LICENSE.md) for details.
