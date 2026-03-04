---
description: Initialize the Span project - restore packages and build
---

# Init Workflow

## Steps

1. Restore NuGet packages:
```powershell
dotnet restore src/Span/Span/Span.csproj
```

// turbo
2. Build the project (x64 Debug):
```powershell
dotnet build src/Span/Span/Span.csproj -p:Platform=x64
```

3. Review the build output for any errors or warnings.
