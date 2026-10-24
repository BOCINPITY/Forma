# IconAdapt

面向 Windows 的桌面图标适配 App，让不同应用的图标在视觉大小、底板和留白上保持协调。

## 当前状态

当前版本：`0.1.0-preview.2`，可操作的开发预览版。修复跨盘快捷方式的相对路径标志误报，并改善权限失败提示。

已实现桌面 / 文件夹扫描、图标网格选择、实时前后对比、渐变 / 纯色 / 透明底板、大小与圆角调整、多尺寸 ICO、批量应用、持久备份、历史记录和按批次恢复。

Release 构建及图像、Windows COM、跨模块集成验证已通过；WPF 界面已启动并渲染检查。自动写入验证均使用自建快捷方式，没有修改真实桌面。详见 [验证记录](docs/verification.md)。

## 直接使用

构建好的便携版位于 `artifacts\IconAdapt-0.1.0-preview.2-win-x64\`，完整保留该文件夹，双击 `IconAdapt.exe` 启动。自包含 Windows x64 包无需另装 .NET SDK。按版本分别输出，避免覆盖仍在运行的旧程序。

先预览，再应用；恢复入口在左侧“恢复记录”。**应用后请保留 `%LOCALAPPDATA%\IconAdapt\` 数据目录**，快捷方式会持续引用这里生成的图标。详见 [使用说明](docs/user-guide.md)。

当前没有安装程序、自动更新和代码签名，不是正式发布版。

![使用自建快捷方式的实际界面](docs/images/workspace.png)

## 支持范围

- 用户桌面和公共桌面的普通 `.lnk`，或用户指定文件夹中的 `.lnk`；不递归扫描。
- 当前仅适配目标是可访问 `.exe` 的普通应用快捷方式。
- 预览只读；应用前保存原始快捷方式副本和操作记录。
- 冲突时保留文件并说明原因；多批次适配应由新到旧恢复。

任务栏、开始菜单、系统特殊图标、商店应用专用入口、文件关联和文件夹图标不在当前范围内。拒绝符号链接和目录联接；公共桌面写入权限不足时逐项报错。底板识别和自动取色尚未实现，已有底板可通过关闭裁切及调整比例手动适配。

## 技术栈

C# / .NET 10、WPF / XAML、SkiaSharp 4.153.1、Windows Shell COM / Win32，以及本地 JSON 操作记录。项目固定 NuGet 版本并提交依赖锁文件，第三方许可见 [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md)。

## 开发启动

需要 Windows x64 和 Git。已有 .NET 10 SDK 可直接使用；也可以下载经过 SHA-512 校验的项目本地 SDK，不修改系统 PATH：

```powershell
Set-Location 'E:\local\IconAdapt'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

生成自包含便携包和 ZIP：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

SDK、测试文件和构建产物保存在忽略提交的 `.local` / `artifacts` 下。脚本优先使用项目本地 SDK，其次查找 PATH 中的 `dotnet`。

## 目录与文档

```text
src/IconAdapt.App/            WPF 界面与 MVVM
src/IconAdapt.Core/           共享模型和接口
src/IconAdapt.Imaging/        SkiaSharp 图标渲染及 ICO 编码
src/IconAdapt.Windows/        STA Shell 操作、备份与恢复
tests/                       图像、Windows 和端到端验证程序
scripts/                     SDK / 构建 / 启动 / 打包
docs/                        产品、架构、开发和使用文档
licenses/                    上游许可原文
```

- [使用说明](docs/user-guide.md)
- [应用失败排查与解决方案](docs/troubleshooting-apply.md)
- [产品范围](docs/product.md)
- [技术架构](docs/architecture.md)
- [技术选型记录](docs/adr/0001-windows-native-stack.md)
- [开发与版本管理](docs/development.md)
- [路线图](docs/roadmap.md)
- [验证记录](docs/verification.md)
- [变更记录](CHANGELOG.md)

Git 在本地管理，未配置远程或推送；项目自身的开源许可证尚未决定。工具不分发 Apple 图标或未经授权的应用图标素材。
