@echo off
setlocal
chcp 65001 >nul

set "ROOT=%~dp0.."
set "OUT=%~dp0bin"
set "CSC64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "CSC32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if exist "%CSC64%" (
    set "CSC=%CSC64%"
) else (
    set "CSC=%CSC32%"
)

if not exist "%CSC%" (
    echo [ERROR] csc.exe not found.
    exit /b 1
)

if not exist "%OUT%" mkdir "%OUT%"

echo [INFO] Building DebugTool C# lite...
echo [INFO] Compiler: "%CSC%"

"%CSC%" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /win32icon:"%ROOT%\logo.ico" ^
    /out:"%OUT%\DebugTool.exe" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Data.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Net.Http.dll ^
    /reference:System.Windows.Forms.dll ^
    "%~dp0Program.cs"

if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

echo [OK] Build complete: "%OUT%\DebugTool.exe"
