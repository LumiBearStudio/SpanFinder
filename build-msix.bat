@echo off
setlocal

echo =========================================
echo   Span MSIX Store Upload Build (x64/x86/ARM64)
echo =========================================
echo.

:: Extract version from Package.appxmanifest
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content 'D:\11.AI\Span\src\Span\Span\Package.appxmanifest')).Package.Identity.Version"`) do set VER=%%V

if "%VER%"=="" (
    echo ERROR: Version not found in Package.appxmanifest
    pause
    exit /b 1
)

set OUTDIR=D:\11.AI\Span\AppPackages\VER_%VER%
set CSPROJ=D:\11.AI\Span\src\Span\Span\Span.csproj
set MSBUILD="C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe"

echo Version: %VER%
echo Output:  %OUTDIR%
echo.

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

set FAILED=0

:: -- x64 Build --
echo [1/3] Building x64...
%MSBUILD% "%CSPROJ%" /restore /v:minimal ^
    /p:Configuration=Release /p:Platform=x64 ^
    /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never ^
    /p:PublishTrimmed=false /p:SelfContained=true ^
    /p:UapAppxPackageBuildMode=StoreUpload ^
    /p:AppxPackageDir="%OUTDIR%\\"
if %ERRORLEVEL% NEQ 0 (
    echo [FAILED] x64 build failed
    set FAILED=1
) else (
    echo [OK] x64
)
echo.

:: -- x86 Build --
echo [2/3] Building x86...
%MSBUILD% "%CSPROJ%" /restore /v:minimal ^
    /p:Configuration=Release /p:Platform=x86 ^
    /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never ^
    /p:PublishTrimmed=false /p:SelfContained=true ^
    /p:UapAppxPackageBuildMode=StoreUpload ^
    /p:AppxPackageDir="%OUTDIR%\\"
if %ERRORLEVEL% NEQ 0 (
    echo [FAILED] x86 build failed
    set FAILED=1
) else (
    echo [OK] x86
)
echo.

:: -- ARM64 Build --
echo [3/3] Building ARM64...
%MSBUILD% "%CSPROJ%" /restore /v:minimal ^
    /p:Configuration=Release /p:Platform=ARM64 ^
    /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never ^
    /p:PublishTrimmed=false /p:SelfContained=true ^
    /p:UapAppxPackageBuildMode=StoreUpload ^
    /p:AppxPackageDir="%OUTDIR%\\"
if %ERRORLEVEL% NEQ 0 (
    echo [FAILED] ARM64 build failed
    set FAILED=1
) else (
    echo [OK] ARM64
)
echo.

:: -- Create ZIP packages for GitHub Release --
echo Creating ZIP packages for GitHub Release...
for %%P in (x64 x86 ARM64) do (
    if exist "%OUTDIR%\Span_%VER%_%%P_Test" (
        powershell -NoProfile -Command "Compress-Archive -Path '%OUTDIR%\Span_%VER%_%%P_Test\*' -DestinationPath '%OUTDIR%\SpanFinder_v%VER%_%%P.zip' -Force"
        echo [OK] SpanFinder_v%VER%_%%P.zip
    ) else (
        echo [SKIP] %%P test folder not found
    )
)
echo.

:: -- Cleanup: keep only .msixupload and .zip files --
echo Cleaning up build artifacts...
powershell -NoProfile -Command ^
    "Get-ChildItem '%OUTDIR%' -Recurse -Force | Where-Object { -not $_.PSIsContainer } | Where-Object { $_.Extension -notin '.msixupload','.zip' } | Remove-Item -Force -ErrorAction SilentlyContinue; Get-ChildItem '%OUTDIR%' -Directory -Force | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue"
echo [OK] Cleanup done
echo.

:: -- Results --
if %FAILED%==0 (
    echo =========================================
    echo   ALL BUILDS SUCCESS - v%VER%
    echo =========================================
) else (
    echo =========================================
    echo   SOME BUILDS FAILED
    echo =========================================
)
echo.
echo Output: %OUTDIR%
echo.
echo --- MS Store uploads (.msixupload) ---
dir /b "%OUTDIR%\*.msixupload" 2>nul
echo.
echo --- GitHub Release (.zip) ---
dir /b "%OUTDIR%\*.zip" 2>nul
echo.
pause
