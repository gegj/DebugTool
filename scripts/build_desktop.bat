@echo off
setlocal
chcp 65001 >nul

color 0B
set "ROOT=%~dp0.."
set "OUT=%USERPROFILE%\Desktop"
set "EXE=%OUT%\DebugTool.exe"
set "CSC64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "CSC32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if exist "%CSC64%" (
    set "CSC=%CSC64%"
) else (
    set "CSC=%CSC32%"
)

if not exist "%CSC%" (
    color 0C
    echo [错误] 未找到 csc.exe，请确认已安装 .NET Framework 4.x。
    color 07
    pause
    exit /b 1
)

if not exist "%ROOT%\logo.ico" (
    color 0C
    echo [错误] 未找到 logo.ico。
    color 07
    pause
    exit /b 1
)

echo [信息] 正在生成桌面调试版 DebugTool.exe...
echo [信息] 输出路径: "%EXE%"

"%CSC%" ^
    /nologo ^
    /target:winexe ^
    /platform:anycpu ^
    /optimize+ ^
    /win32icon:"%ROOT%\logo.ico" ^
    /out:"%EXE%" ^
    /reference:System.dll ^
    /reference:System.Core.dll ^
    /reference:System.Data.dll ^
    /reference:System.Drawing.dll ^
    /reference:System.Net.Http.dll ^
    /reference:System.Windows.Forms.dll ^
    "%ROOT%\csharp\Program.cs"

if errorlevel 1 (
    color 0C
    echo [错误] 生成失败。
    color 07
    pause
    exit /b 1
)

color 0A
echo [完成] 已生成: "%EXE%"
color 07
pause
