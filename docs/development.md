# 构建与发布

[首页](../README.md) · [English](en/development.md)

## 本地开发

使用 Windows x64、Git 和 PowerShell。SDK 固定为 10.0.402，可用项目脚本下载并校验，不修改系统 PATH。

```powershell
git clone https://github.com/BOCINPITY/Forma.git
Set-Location .\Forma
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

`check.ps1` 锁定还原、Release 构建并运行图像、Shell COM、集成和 WPF 检查；`-SkipSmoke` 只构建。测试写入自建快捷方式，集成检查还会只读扫描桌面。测试文件和 SDK 位于忽略提交的 `.local`。

隔离启动可使用 `.\scripts\run.ps1 -ScanFolder '.\.local\demo' -DataRoot '.\.local\demo-data'`，先在扫描目录准备测试快捷方式。

## 打包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

输出 `artifacts\Forma-<版本>-win-x64\`、ZIP 和 `.sha256`。包内包含运行时、中英文图文使用说明、MIT 和第三方许可。完整保留文件夹，不单独分发 EXE。检查下载文件可使用 `Get-FileHash -LiteralPath '.\下载的文件.zip' -Algorithm SHA256`。

## GitHub Actions

- **Build**：`main` 推送、PR 和手动运行，检查并生成便携包；Actions 附件保留 14 天。
- **Release**：推送版本标签，或手动指定已有标签，构建并上传 GitHub Release 的 ZIP / SHA-256。带 `-preview` 等后缀的版本标记为预发布。
- Windows runner 使用同一 SDK；测试设置仅影响临时 runner。只有发布附件的 job 有仓库写权限，不上传 `.local`、测试记录或备份。

发布时更新 `Directory.Build.props` 的版本、依赖锁和简短变更记录。提交到 `main`，Build 通过后创建与版本一致的标签：

```powershell
git tag -a v0.1.0-preview.9 -m 'Forma 0.1.0-preview.9'
git push origin v0.1.0-preview.9
```

标签必须等于 `v` 加项目版本。重跑 Release 会更新说明和同名附件。依赖或运行时升级时同步 `licenses` 和第三方声明。当前无签名、安装器或自动更新；稳定发布前仍需实际桌面、多 DPI 和权限场景验收。
