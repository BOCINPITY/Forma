# 构建与发布 / Builds and releases

## 中文

GitHub Actions 的 **Build** 工作流在 `main` 推送、面向 `main` 的拉取请求或手动运行时，使用 Windows 2025 runner 和 .NET SDK 10.0.402，执行锁定依赖还原、Release 构建及现有四组 smoke 检查，再生成 Windows x64 自包含便携 ZIP 和 SHA-256 校验文件。SDK 与本地 `bootstrap.ps1` 及包内 .NET 10.0.13 许可声明保持一致。构建附件保留 14 天。

发布步骤：

1. 更新 `Directory.Build.props` 中的版本、依赖锁文件及 `CHANGELOG.md`；需要时同步文档、运行时和第三方许可。提交到 `main`，确认 Build 通过。
2. 创建并推送与版本完全一致的标签，例如版本 `0.1.0-preview.9` 对应 `v0.1.0-preview.9`。
3. **Release** 工作流从该标签构建、检查并打包，然后创建 GitHub Release、上传 ZIP 和 `.zip.sha256`。含 `-` 的版本标记为预发布。工作流不会创建标签。

也可在 Actions 中手动运行 Release，并输入已有版本标签；输入必须与标签内的 `Directory.Build.props` 一致。相同标签的发布会串行执行；重跑会更新说明并替换同名附件。

只有发布附件的 job 具有 `contents: write`；构建 job 仅有读取权限。上传范围仅为生成的 ZIP 与校验文件，不上传 `.local`、测试快捷方式、桌面扫描、日志或备份。便携包包含 `Forma.exe`、运行时、离线图文文档 `docs`、两种语言的使用入口、MIT `LICENSE`、第三方声明、`licenses` 和构建信息。

下载后完整解压 ZIP；不要只复制 EXE。可在 PowerShell 中用 `Get-FileHash -LiteralPath '.\Forma-0.1.0-preview.9-win-x64.zip' -Algorithm SHA256` 与附件中的值比较。SHA-256 可检查文件一致性；程序当前没有代码签名、安装程序或自动更新。自动检查不替代真实桌面、多 DPI 和权限场景的人工验收。

## English

The **Build** GitHub Actions workflow runs on pushes to `main`, pull requests targeting `main`, and manual dispatch. It uses a Windows 2025 runner and .NET SDK 10.0.402 to restore locked dependencies, build Release, run the existing four smoke suites, and package a self-contained Windows x64 ZIP with its SHA-256 checksum. The SDK matches local `bootstrap.ps1` and the bundled .NET 10.0.13 license notices. Build artifacts expire after 14 days.

To release:

1. Update the version in `Directory.Build.props`, dependency lock files, and `CHANGELOG.md`. Update documentation, runtime and third-party notices as needed. Commit to `main` and confirm Build succeeds.
2. Create and push a tag matching that version exactly: for example, `v0.1.0-preview.9` for version `0.1.0-preview.9`.
3. **Release** checks out that tag, builds, checks, packages, creates a GitHub Release, and uploads the ZIP and `.zip.sha256`. Versions containing `-` become prereleases. The workflow does not create tags.

You can also manually dispatch Release with an existing version tag; it must match `Directory.Build.props` at that tag. Releases for the same tag run sequentially. Rerunning updates the release notes and replaces assets with the same names.

Only the publication job has `contents: write`; the build job has read access. Uploaded artifacts contain only the generated ZIP and checksum, excluding `.local`, test shortcuts, desktop scans, logs and backups. The portable package includes `Forma.exe`, its runtime, offline illustrated documentation in `docs`, Chinese and English guide entry points, the MIT `LICENSE`, third-party notices, `licenses`, and build information.

Fully extract the ZIP before running; keep the entire folder. Use PowerShell `Get-FileHash -LiteralPath '.\Forma-0.1.0-preview.9-win-x64.zip' -Algorithm SHA256` to compare against the attached checksum. SHA-256 checks file integrity. There is currently no code signing, installer, or automatic updater. Automated checks do not replace manual verification of actual desktop behavior, multiple DPI settings, and permissions.
