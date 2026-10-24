# 开发与版本管理

## 环境

- Windows；首轮兼容性目标为 Windows 11，尚未完成实机验证。
- .NET 10 SDK（仅安装 Desktop Runtime 不足以编译）。
- Git。
- 可选：支持 .NET 10 的 Visual Studio 或其他 C# 编辑器。

终端默认 PowerShell。运行 `Get-Command dotnet` 和 `dotnet --list-sdks` 确认 SDK 可用。若命令不存在，安装 SDK 并重新打开终端；不把“未执行”记作构建通过。

## 本地命令

```powershell
Set-Location 'E:\local\IconAdapt'
.\scripts\check.ps1
dotnet run --project .\src\IconAdapt.App\IconAdapt.App.csproj
```

如果本机执行策略阻止脚本，可以先阅读脚本，再在本次进程中执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
```

检查脚本执行 restore 和 Release 构建。当前没有测试工程，构建成功不代表桌面修改能力已验证。后续对渲染边界、恢复状态机等关键逻辑添加有意义的自动化测试。

## Git 流程

- `main`：保存已审查的项目基线，提交必须标明验证状态。
- 功能开发使用 `feat/<topic>`，修复使用 `fix/<topic>`，文档使用 `docs/<topic>`。
- 提交采用 `feat:`、`fix:`、`docs:`、`chore:` 等前缀，范围尽量单一。
- 提交前检查 `git diff --check`、`git diff` 和 `git status --short`，确认不含用户数据、凭据和生成文件。
- Git 负责本地项目历史；远程备份须在配置远程仓库并推送后才成立。目前未配置远程。

```powershell
git switch -c feat/desktop-scan
git status --short
git diff --check
git add src docs CHANGELOG.md
git commit -m 'feat: add desktop shortcut discovery'
```

示例提交只能在对应功能实际完成并检查后执行。

## 版本与发布

- 当前为 `0.1.0-dev`，不是已发布版本。
- 版本号来源是 `Directory.Build.props`；显示版本变更时同步窗口中的版本说明。
- 功能和行为变化同步维护 CHANGELOG.md。
- 发布前完成构建、关键流程测试、恢复验证和打包验证，再创建 `vX.Y.Z` 标签。
- 发布方式、签名、安装包、更新机制和许可证仍待确定，当前未生成发布包。
- 不自动推送、不自动创建远程仓库。

## 验证原则

桌面操作先使用临时测试目录和自建快捷方式。确认备份、原属性保留、部分失败和恢复行为后，再验证真实桌面。每次验证记录环境、操作、结果及限制，不把静态检查当作运行测试。
