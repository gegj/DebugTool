@echo off
setlocal EnableExtensions
chcp 65001 >nul

set "NO_PAUSE="
if /i "%~1"=="/nopause" set "NO_PAUSE=1"

color 0B

pushd "%~dp0.." >nul 2>nul
if errorlevel 1 (
    set "ERROR_MESSAGE=无法定位项目根目录。"
    goto fail
)
set "ROOT=%CD%"
popd >nul

for /f "usebackq delims=" %%I in (`powershell -NoProfile -ExecutionPolicy Bypass -Command "[Environment]::GetFolderPath('Desktop')"`) do set "OUT=%%I"
if not defined OUT set "OUT=%USERPROFILE%\Desktop"
if not exist "%OUT%" mkdir "%OUT%" >nul 2>nul

set "EXE=%OUT%\DebugTool.exe"
set "CSC64=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "CSC32=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if exist "%CSC64%" (
    set "CSC=%CSC64%"
) else (
    set "CSC=%CSC32%"
)

if not exist "%CSC%" (
    set "ERROR_MESSAGE=未找到 csc.exe，请确认已安装 .NET Framework 4.x。"
    goto fail
)

if not exist "%ROOT%\logo.ico" (
    set "ERROR_MESSAGE=未找到 logo.ico。"
    goto fail
)

if not exist "%ROOT%\csharp\Program.cs" (
    set "ERROR_MESSAGE=未找到 csharp\Program.cs。"
    goto fail
)

echo [信息] 正在生成桌面调试版 DebugTool.exe...
echo [信息] 项目目录: "%ROOT%"
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
    set "EXIT_CODE=1"
    goto finish
)

color 0A
echo [完成] 已生成: "%EXE%"
set "EXIT_CODE=0"
goto finish

:fail
color 0C
echo [错误] %ERROR_MESSAGE%
set "EXIT_CODE=1"
goto finish

:finish
color 07
if not defined NO_PAUSE pause
exit /b %EXIT_CODE%
