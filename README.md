# DebugTool

DebugTool 是一个用于某兴随身 WiFi 调试开关的 Windows 轻量工具，提供 F32/F30PRO 和 REMO 两类设备的 IMEI 获取、Debug/Telnet 开关和重启操作。

当前主版本为 C# WinForms 轻量版，发布产物为单个 `DebugTool.exe`。

## 下载

已编译版本下载：

```text
https://github.com/gegj/DebugTool/releases/latest/download/DebugTool.exe
```

## 功能

- F32/F30PRO
  - 获取 IMEI
  - 开启 Telnet
  - 开启 Telnet + Debug
  - 关闭 Telnet
  - 重启设备
- REMO
  - 获取 IMEI
  - 开启 REMO Debug
  - 重启 REMO 设备
- 启动自动获取设备信息
- 启动更新检测
- 跟随 Windows 系统深色/浅色主题，并监听实时变化
- EXE 内置程序图标，无需额外携带 `logo.ico`

## 运行环境

- Windows 10 / Windows 11 推荐
- 需要 .NET Framework 4.x，建议 .NET Framework 4.8

Windows 10/11 通常已经具备运行环境。若无法启动，请安装 .NET Framework 4.8 Runtime。

## 默认地址

- F32/F30PRO：`192.168.0.1`
- REMO：`192.168.100.1`

可在软件界面中手动修改 IP。

## 构建

本项目不再保留本地构建产物，也不在本地手动构建 EXE。

发布新版时推送版本 tag，由 GitHub Actions 在 GitHub Windows 环境自动编译并发布 `DebugTool.exe`。

`logo.ico` 会在 GitHub 构建时编入 EXE；发布后只需要下载 `DebugTool.exe`。

## 更新检测

程序启动时会请求：

```text
https://github.com/gegj/DebugTool/releases/latest/download/latest.json
```

JSON 格式：

```json
{
  "version": "1.1.13",
  "url": "https://github.com/gegj/DebugTool/releases/latest/download/DebugTool.exe",
  "notes": "更新说明"
}
```

当远程版本大于程序内置版本时，会提示打开下载地址。

## 自动发布

仓库已配置 GitHub Actions。推送版本 tag 后会自动在 GitHub Windows 环境编译并发布 Release：

```bat
git tag v1.1.13
git push origin v1.1.13
```

发布前请先把 `csharp/Program.cs` 中的 `AppVersion` 和 Assembly 版本同步改成同一版本。

Release 会自动上传：

```text
DebugTool.exe
latest.json
```

## 项目结构

```text
.
├── ai_studio_code.py          # Python 原版
├── csharp
│   └── Program.cs             # C# WinForms 轻量版源码
├── scripts
│   └── build_desktop.bat      # 本地调试打包脚本，输出到桌面
├── docs
│   ├── 使用说明.md
│   └── 发布说明.md
├── latest.json                # 更新检测 JSON 模板
├── logo.ico                   # 构建时使用的程序图标
└── requirements.txt           # Python 原版依赖
```

## 仓库简介建议

可用于 GitHub 仓库描述：

```text
Windows 轻量版某兴随身 WiFi Debug/Telnet 开关工具，支持 F32/F30PRO 与 REMO，内置更新检测和系统主题自适应。
```
