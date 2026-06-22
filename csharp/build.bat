@echo off
setlocal
chcp 65001 >nul
color 0B
title Build DebugTool CSharp Lite

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
    color 0C
    echo [ERROR] csc.exe not found.
    color 07
    pause
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
    color 0C
    echo [ERROR] Build failed. Close DebugTool.exe if it is running, then retry.
    color 07
    pause
    exit /b 1
)

color 0A
echo [OK] Build complete: "%OUT%\DebugTool.exe"
color 07
pause
