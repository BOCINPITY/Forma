# 开发与版本管理

## 环境与命令

Windows x64、Git 和 .NET 10 SDK；首轮目标 Windows 11。终端使用 PowerShell。

```powershell
Set-Location 'E:\local\IconAdapt'
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

`bootstrap.ps1` 从微软官方元数据定位固定 SDK 10.0.402，下载 Windows x64 ZIP，验证 SHA-512 后解压到 `.local\dotnet`。不提权、不改系统 PATH。其余脚本优先用本地 SDK，找不到时再查 PATH。

`check.ps1` 执行解决方案 restore、Release build 和三个验证程序；`-SkipSmoke` 仅构建。测试仅创建 / 修改 `.local` 中的快捷方式；集成测试还会只读扫描实际桌面，控制台仅输出计数。不能把构建通过视作桌面效果已验证。

开发便携版由 `publish.ps1` 生成到 `artifacts\IconAdapt-win-x64`，包含运行时、原生库、使用说明、许可和构建信息。`-Zip` 额外生成 ZIP 与 SHA-256 文件。不要提交 SDK、输出包或用户数据。

## 隔离启动与截图

```powershell
.\scripts\run.ps1 -ScanFolder 'E:\local\IconAdapt\.local\demo' -DataRoot 'E:\local\IconAdapt\.local\demo-data'
```

直接运行应用还可传 `--scan-folder <绝对路径>`、`--data-root <绝对路径>`、`--smoke-ui <输出 PNG 路径>`。截图模式等待扫描及渲染完成后保存窗口内容并退出；失败时输出 `.error.txt`。它不执行应用或恢复。

真实桌面验证时只读扫描无需写入；应用须由用户在界面选择并确认。自动验证坚持使用隔离测试目录。

## Git 约定

- `main` 保存项目基线，功能开发使用 `feat/<topic>`，修复使用 `fix/<topic>`。
- 提交使用 `feat:`、`fix:`、`docs:`、`chore:` 等前缀，范围保持清楚。
- 提交前检查 `git diff --check`、实际 diff 和 `git status --short`，避免提交用户路径记录、快捷方式、备份或凭据。
- NuGet 依赖精确版本与锁文件一同提交，检查及打包采用 locked restore。升级依赖时显式重新生成锁文件并同步许可原文。
- 当前没有远程仓库，不自动创建远程或推送。本地 Git 历史不等于远程备份。

## 版本

当前 `0.1.0-preview.1` 是开发预览版本，版本来源为 `Directory.Build.props`，界面读取程序集信息。行为变化同步维护 CHANGELOG.md；架构变化同步 docs。

正式发布前仍需人工验证真实桌面更新、多 DPI、更多应用图标和文件权限场景，明确安装 / 卸载、代码签名、许可证和自动更新策略。没有正式发布授权时不创建版本标签。

## 验证程序

- `IconAdapt.Imaging.Smoke`：边界裁切、渲染参数、透明图、ICO 结构、WIC 解码与并发确定性。
- `IconAdapt.Windows.Smoke`：真实 COM 操作、属性保留、备份 / 恢复、多批次、文件冲突和错误恢复。
- `IconAdapt.Integration.Smoke`：提取 → 渲染 → 应用 → 重启后读取历史 → 恢复，并检查未选中项目和启动属性。

三者无需额外测试框架，失败返回非零退出码。输出图片、ICO、快捷方式和记录保留在 `.local` 便于调查，不会提交。
