# Build and release

[Home](../../README.en.md) · [简体中文](../development.md)

## Local development

Use Windows x64, Git and PowerShell. SDK 10.0.402 matches CI. Bootstrap downloads and verifies a project-local SDK without changing PATH.

```powershell
git clone https://github.com/BOCINPITY/Forma.git
Set-Location .\Forma
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

`check.ps1` restores locked dependencies, builds Release and runs image, Shell COM, integration and WPF checks. Use `-SkipSmoke` to build only. Tests write to synthetic shortcuts; integration also scans the desktop read-only. SDK and test data stay in ignored `.local`.

For isolated UI work, create test shortcuts and run `.\scripts\run.ps1 -ScanFolder '.\.local\demo' -DataRoot '.\.local\demo-data'`.

## Package

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

Packaging requires [7-Zip](https://www.7-zip.org/). The script cleans old output, excludes debug symbols, and keeps Chinese and fallback English framework resources. It retains the WPF runtime without unsupported trimming. It creates a maximum-Deflate ZIP and a solid LZMA2-compressed EXE, each with a SHA-256 file. The launcher uses SHA-256-pinned, public-domain LZMA SDK 26.01, extracts to a temporary directory and cleans up on exit. Its icon, version and asInvoker manifest resources come from Forma.

Output: `artifacts\Forma-<version>-win-x64\` and adjacent assets, including the runtime, bilingual guides and license notices. Keep the complete ZIP folder. Run `.\scripts\check-package.ps1` to verify both assets and test bundled-runtime startup, native icon extraction and rendering with isolated shortcut fixtures; it never changes the real desktop. Check a download using `Get-FileHash -LiteralPath '.\download.exe' -Algorithm SHA256`.

## GitHub Actions

- **Build** runs on `main` pushes, pull requests and manual dispatch. It validates and packages the app; artifacts expire after 14 days.
- **Release** runs for a pushed version tag or an existing tag selected manually. It verifies and uploads EXE / ZIP / SHA-256 assets. Stable versions are marked Latest; versions with suffixes such as `-preview` are prereleases.
- Windows runners use the same SDK. Test display changes affect only the disposable runner. Only the publication job has repository write access. Local test data and backups are never uploaded.

Update `Directory.Build.props`, dependency lock files and the short changelog. Commit to `main`, wait for Build, then push a matching tag:

```powershell
git tag -a v1.0.0 -m 'Forma 1.0.0'
git push origin v1.0.0
```

The tag must be `v` plus the project version. Rerunning Release updates notes and replaces matching assets. Update `licenses` and third-party notices when changing runtimes or dependencies. There is no signing, installer or updater yet; see the user guide for platform scope.
