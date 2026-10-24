<p align="center">
  <img src="src/IconAdapt.App/Assets/IconAdapt.svg" width="112" alt="Forma 的代码绘制标志">
</p>

# Forma

**让 Windows 桌面图标更协调。** 原生桌面应用，统一图标的视觉大小、留白和底板，同时保留应用的识别特征。

**简体中文** · [English](README.en.md)

[![Build](https://github.com/BOCINPITY/Forma/actions/workflows/build.yml/badge.svg)](https://github.com/BOCINPITY/Forma/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![Release](https://img.shields.io/badge/version-1.0.0-green)

[下载便携版](https://github.com/BOCINPITY/Forma/releases) · [使用说明](docs/user-guide.md) · [开发文档](docs/development.md) · [English guide](docs/en/user-guide.md)

![Forma 桌面整体预览，六枚代码绘制的演示图标](docs/images/desktop-preview-light.png)

## 能做什么

- **先看整组效果**：浅 / 深背景、原始 / 适配切换、32 / 48 / 64 / 96px 实际像素预览。
- **按形态适配**：macOS 质感区分已有底板、圆形、透明标志及细线 / 复杂图案，保守调整视觉大小和安全留白；另有渐变、纯色、透明模式。
- **保留清晰细节**：读取原生尺寸图标，逐尺寸生成 ICO，用实际导出帧预览，避免重复缩放。
- **批量应用，一键还原**：写入前自动备份，保留启动目标、参数等属性；连续多次适配可以统一恢复。
- **原生、离线**：C# / .NET 10 / WPF；macOS 风格表面、短时动效，应用标志全部由代码绘制，无需模型服务。

| 图标库与实际像素对比 | 深色背景的整体预览 |
| --- | --- |
| ![图标库与检查器](docs/images/workspace.png) | ![深色背景预览](docs/images/desktop-preview-dark.png) |

图片使用自绘合成图标与演示状态。桌面预览模拟背景和排列，不读取壁纸或移动桌面图标；窗口材质由 WPF 绘制。

## 下载与使用

1. 在 [Releases](https://github.com/BOCINPITY/Forma/releases/latest) 下载 `Forma-<版本>-win-x64.exe`，双击即用，无需安装 .NET 或解压软件。也提供 ZIP，完整解压后运行其中的 `Forma.exe`。
2. 扫描桌面或选择快捷方式文件夹，勾选项目，点击 **macOS 预设** 或调整风格。
3. 先用桌面预览看整体，再用右侧 32 / 48px 对比检查细节；满意后点击 **应用选中图标**。
4. 如需还原，点击 **恢复默认图标**，自动恢复到 Forma 首次修改前的图标引用，包含原有自定义图标。

当前正式版为 **`1.0.0`**，面向 Windows x64，优先验证 Windows 11。高压缩 EXE 每次启动时自动解压到临时目录，退出后清理；ZIP 解压一次后启动更直接。两者功能一致，均自带运行时，尚未签名，无安装程序或自动更新。升级时关闭旧窗口再运行新版。

高压缩 EXE 约 **50 MiB**，兼容 ZIP 约 **68 MiB**；相比首个预发布包约 **102 MiB**，推荐下载体积约减半。

**应用后保留 `%LOCALAPPDATA%\IconAdapt\`。** 这里保存 ICO、备份和操作记录，快捷方式会持续引用生成的 ICO。卸载或清理数据前先恢复图标。Forma 原名 IconAdapt，沿用旧数据目录以保持已有恢复链可用。

支持桌面 / 指定文件夹中的普通 `.lnk`，当前仅适配可访问 `.exe` 的应用入口；扫描不递归。不覆盖任务栏、开始菜单、文件关联、系统特殊图标或商店应用专用入口。公共桌面权限不足会逐项提示，不自动提权、重启 Explorer 或清理全局缓存。低清原图无法凭空补出细节；更多限制见[使用说明](docs/user-guide.md)。

## 从源码构建

需要 Windows x64、Git 和 .NET 10 SDK。所有脚本使用 PowerShell。

```powershell
git clone https://github.com/BOCINPITY/Forma.git
Set-Location .\Forma
# 可选：下载并校验项目本地 SDK，不修改系统 PATH
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

生成包含运行时、双语使用说明、MIT 及第三方许可的便携包：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

打包需要 [7-Zip](https://www.7-zip.org/)。输出位于 `artifacts\Forma-<版本>-win-x64\`，旁边生成高压缩 EXE、ZIP 及各自的 `.sha256`。SDK、构建产物和测试数据不会提交。

## 自动构建与发布

- [Build](.github/workflows/build.yml)：`main` 提交、PR 或手动运行，构建验证并上传便携包为 Actions artifact。
- [Release](.github/workflows/release.yml)：推送 `v<版本>` 标签，或手动指定已有标签，自动打包并上传 GitHub Release。标签必须与 `Directory.Build.props` 中的版本一致。
- 正式版本发布为 **Latest**，带 `preview` 等后缀的版本标记为 **Pre-release**；提供 Windows x64 自包含 EXE / ZIP 和 SHA-256 校验文件。

具体操作见[构建与发布](docs/development.md)。

## 文档与贡献

| 中文 | English |
| --- | --- |
| [使用说明](docs/user-guide.md) | [User guide](docs/en/user-guide.md) |
| [构建与发布](docs/development.md) | [Build and release](docs/en/development.md) |

欢迎通过 [Issues](https://github.com/BOCINPITY/Forma/issues) 与 PR 反馈，提交前请阅读[贡献指南](CONTRIBUTING.md)。版本变化见 [CHANGELOG](CHANGELOG.md)。

## 许可

Forma 源码与原创资源采用 [MIT License](LICENSE)。.NET、SkiaSharp / Skia 等依赖保留各自许可与声明，见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) 和 [licenses](licenses/README.md)。工具不分发 Apple 图标或第三方应用图标包；用户应用图标的权利归其原作者或权利人所有。
