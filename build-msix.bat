@echo off
setlocal enabledelayedexpansion

echo === Span MSIX Store Upload Build ===
echo.

:: Package.appxmanifest에서 버전 추출
for /f "tokens=2 delims==" %%a in ('findstr /i "Version=" "D:\11.AI\Span\src\Span\Span\Package.appxmanifest" ^| findstr "Identity"') do (
    set RAW=%%a
)
:: 따옴표, 공백, /> 제거
set VER=%RAW:"=%
set VER=%VER: =%
set VER=%VER:/=%
set VER=%VER:>=%

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
    echo === BUILD SUCCESS ===
    echo.
    dir /s /b "%OUTDIR%\*.msixupload" 2>nul
    echo.
    echo Output: %OUTDIR%
) else (
    echo.
    echo === BUILD FAILED ===
)
pause
