# Repository Guidelines

## Project Structure & Module Organization
- `src/Span/Span/` contains the WinUI 3 app (`Span.csproj`) and all C# source.
- `src/Span/Span/ViewModels`, `Models`, `Services`, and `Helpers` follow MVVM separation. Keep UI logic in `ViewModels`, not code-behind.
- `src/Span/Span/Assets/` stores packaged images, fonts, and XAML styles.
- `docs/` holds planning, design, analysis, and mockup artifacts; treat it as product/engineering documentation.
- `design/` contains standalone HTML/CSS/JS prototypes.
- Build outputs live in `bin/` and `obj/`; logs in `Logs/`. Do not commit generated artifacts.

## Build, Test, and Development Commands
- `dotnet --version` should resolve to SDK `8.0.406` (see `global.json`).
- `dotnet restore src/Span/Span/Span.csproj` restores NuGet packages.
- `dotnet build src/Span/Span/Span.csproj -c Debug -p:Platform=x64` builds local debug binaries.
- `dotnet run --project src/Span/Span/Span.csproj -c Debug -p:Platform=x64` runs the app locally.
- `dotnet publish src/Span/Span/Span.csproj -c Release -p:Platform=x64` creates release output.

## Coding Style & Naming Conventions
- Use 4-space indentation and file-scoped, nullable-aware C# (`<Nullable>enable</Nullable>`).
- Use `PascalCase` for types/methods/properties, `camelCase` for locals/parameters, and meaningful MVVM names (e.g., `MainViewModel`).
- Keep services focused (`*Service.cs`), converters explicit (`*Converter.cs`), and commands in `RelayCommand` patterns.

## Testing Guidelines
- No dedicated test project exists yet. Add tests under `tests/Span.Tests/` using xUnit when introducing non-trivial logic.
- Name test files `*Tests.cs` and methods `MethodName_State_ExpectedResult`.
- Run tests with `dotnet test` once a test project is added.

## Commit & Pull Request Guidelines
- Git history is not available in this workspace snapshot; use Conventional Commit style (`feat:`, `fix:`, `refactor:`, `docs:`) in imperative mood.
- Keep commits scoped to one concern.
- PRs should include: summary, affected paths (for example `src/Span/Span/ViewModels/MainViewModel.cs`), test/build evidence, and UI screenshots for visual changes.
- Link related issues or design docs in `docs/` when applicable.
