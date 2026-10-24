# IconAdapt

面向 Windows 的桌面图标适配工具，让不同应用的图标在视觉大小、底板和留白上保持协调。

## 当前状态

当前版本：`0.1.0-dev`，处于项目初始化阶段。

已完成项目结构、WPF 窗口骨架、技术方案和开发约定。尚未实现桌面扫描、图标渲染、图标替换或恢复；当前窗口仅用于后续开发。代码尚未通过编译或界面运行验证，原因是初始化环境未检测到 .NET SDK。

## 第一版范围

- 识别当前用户桌面及公共桌面中的普通 `.lnk` 快捷方式。
- 提取图标，调整可见内容大小、留白及背景，生成多尺寸 `.ico`。
- 应用前预览，对选中的快捷方式批量应用。
- 保存原始快捷方式备份及图标引用，支持恢复和失败后的处理。

任务栏、开始菜单、系统特殊图标、商店应用专用入口、文件关联和文件夹图标不属于第一版范围。Mac 风格仅作为视觉参考，不分发 Apple 图标或其他未经授权的素材。

## 技术栈

- C# / .NET 10：应用逻辑。
- WPF / XAML：桌面界面。
- SkiaSharp：计划用于图像分析与合成，尚未引入依赖。
- Windows Shell API / COM：计划用于图标提取、快捷方式读写和更新通知。
- JSON 与本地图标目录：计划用于主题参数、批次记录和缓存。

## 开发启动

需要 Windows、Git 和 .NET 10 SDK。

```powershell
Set-Location 'E:\local\IconAdapt'
dotnet --info
dotnet restore .\IconAdapt.slnx
dotnet build .\IconAdapt.slnx --configuration Debug
dotnet run --project .\src\IconAdapt.App\IconAdapt.App.csproj
```

这里的启动命令需在安装 SDK 后验证。当前工程不包含第三方运行时依赖。

## 目录

```text
IconAdapt/
  src/IconAdapt.App/      WPF 应用骨架
  docs/                  方案、开发约定、路线图和验证记录
  scripts/               PowerShell 检查脚本
  IconAdapt.slnx          解决方案
  CHANGELOG.md           版本变更
```

## 文档入口

- [产品范围](docs/product.md)
- [技术架构](docs/architecture.md)
- [技术选型记录](docs/adr/0001-windows-native-stack.md)
- [开发与版本管理](docs/development.md)
- [开发路线图](docs/roadmap.md)
- [验证记录](docs/verification.md)
- [变更记录](CHANGELOG.md)

本仓库目前只在本地管理，尚未配置远程仓库或发布安装包。开源许可证尚未确定。
