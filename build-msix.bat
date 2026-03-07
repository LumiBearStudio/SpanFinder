@echo off
setlocal

echo === Span MSIX Store Upload Build ===
echo.

:: Package.appxmanifest에서 버전 추출 (PowerShell XML 파싱)
for /f "usebackq delims=" %%V in (`powershell -NoProfile -Command "([xml](Get-Content 'D:\11.AI\Span\src\Span\Span\Package.appxmanifest')).Package.Identity.Version"`) do set VER=%%V

if "%VER%"=="" (
    echo ERROR: Version not found in Package.appxmanifest
    pause
    exit /b 1
)

:: 출력 디렉토리: builds\v{버전}
set OUTDIR=D:\11.AI\Span\builds\v%VER%
echo Version: %VER%
echo Output:  %OUTDIR%
echo.

if not exist "%OUTDIR%" mkdir "%OUTDIR%"

"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" ^
    "D:\11.AI\Span\src\Span\Span\Span.csproj" ^
    /restore ^
    /p:Configuration=Release ^
    /p:Platform=x64 ^
    /p:GenerateAppxPackageOnBuild=true ^
    /p:AppxBundle=Never ^
    /p:PublishTrimmed=false ^
    /p:UapAppxPackageBuildMode=StoreUpload ^
    /p:AppxPackageDir="%OUTDIR%\\"

if %ERRORLEVEL% EQU 0 (
    echo.
    echo =========================================
    echo   BUILD SUCCESS - v%VER%
    echo =========================================
    echo.
    dir /s /b "%OUTDIR%\*.msixupload" 2>nul
    echo.
    echo Output: %OUTDIR%
) else (
    echo.
    echo === BUILD FAILED ===
)
pause
