@echo off
echo === Span MSIX Store Upload Build ===
"C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe" "D:\11.AI\Span\src\Span\Span\Span.csproj" /restore /p:Configuration=Release /p:Platform=x64 /p:GenerateAppxPackageOnBuild=true /p:AppxBundle=Never /p:PublishTrimmed=false /p:UapAppxPackageBuildMode=StoreUpload /p:AppxPackageDir="D:\11.AI\Span\AppPackages\\"
if %ERRORLEVEL% EQU 0 (
    echo.
    echo === BUILD SUCCESS ===
    echo Output: D:\11.AI\Span\AppPackages\
    dir /s /b "D:\11.AI\Span\AppPackages\*.msixupload" 2>nul
) else (
    echo.
    echo === BUILD FAILED ===
)
pause
