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

Output: `artifacts\Forma-<version>-win-x64\`, a ZIP and `.sha256`. Packages include the runtime, illustrated Chinese and English user guides, MIT and dependency notices. Keep the whole folder; do not distribute the EXE alone. Check a download using `Get-FileHash -LiteralPath '.\download.zip' -Algorithm SHA256`.

## GitHub Actions

- **Build** runs on `main` pushes, pull requests and manual dispatch. It validates and packages the app; artifacts expire after 14 days.
- **Release** runs for a pushed version tag or an existing tag selected manually. It builds and uploads ZIP / SHA-256 assets to GitHub Releases. Versions with suffixes such as `-preview` are prereleases.
- Windows runners use the same SDK. Test display changes affect only the disposable runner. Only the publication job has repository write access. Local test data and backups are never uploaded.

Update `Directory.Build.props`, dependency lock files and the short changelog. Commit to `main`, wait for Build, then push a matching tag:

```powershell
git tag -a v0.1.0-preview.9 -m 'Forma 0.1.0-preview.9'
git push origin v0.1.0-preview.9
```

The tag must be `v` plus the project version. Rerunning Release updates notes and replaces matching assets. Update `licenses` and third-party notices when changing runtimes or dependencies. There is no signing, installer or updater yet; stable releases still need manual desktop, DPI and permission checks.
