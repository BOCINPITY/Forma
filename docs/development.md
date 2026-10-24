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

打包需安装 [7-Zip](https://www.7-zip.org/)。脚本清理旧输出、排除调试符号，仅保留中文及默认英文框架资源；保留 WPF 所需运行时，不使用不受支持的裁剪。生成最大 Deflate 压缩 ZIP，以及 LZMA2 固实压缩 EXE，各附 `.sha256`。EXE 使用经 SHA-256 固定校验的公共领域 LZMA SDK 26.01，自动解压到临时目录启动，退出清理；图标、版本与普通权限清单取自 Forma。

输出位于 `artifacts\Forma-<版本>-win-x64\` 及同级附件，含运行时、双语使用说明和许可。ZIP 内文件夹需完整保留。运行 `.\scripts\check-package.ps1` 检查两种附件的完整性，并使用隔离快捷方式验证自带运行时、WPF 启动、图标提取和渲染；不会修改真实桌面。下载校验可用 `Get-FileHash -LiteralPath '.\下载的文件.exe' -Algorithm SHA256`。

## GitHub Actions

- **Build**：`main` 推送、PR 和手动运行，检查并生成便携包；Actions 附件保留 14 天。
- **Release**：推送版本标签，或手动指定已有标签，验证并上传 EXE / ZIP / SHA-256。正式版本标记为 Latest，带 `-preview` 等后缀的版本标记为预发布。
- Windows runner 使用同一 SDK；测试设置仅影响临时 runner。只有发布附件的 job 有仓库写权限，不上传 `.local`、测试记录或备份。

发布时更新 `Directory.Build.props` 的版本、依赖锁和简短变更记录。提交到 `main`，Build 通过后创建与版本一致的标签：

```powershell
git tag -a v1.0.0 -m 'Forma 1.0.0'
git push origin v1.0.0
```

标签必须等于 `v` 加项目版本。重跑 Release 会更新说明和同名附件。依赖或运行时升级时同步 `licenses` 和第三方声明。当前无签名、安装器或自动更新；平台范围以使用说明为准。
