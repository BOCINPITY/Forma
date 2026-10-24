<p align="center">
  <img src="src/IconAdapt.App/Assets/IconAdapt.svg" width="112" alt="Forma's code-drawn mark">
</p>

# Forma

**A more coherent Windows desktop.** A native app that balances icon size, padding and backgrounds while keeping applications recognizable.

[简体中文](README.md) · **English**

[![Build](https://github.com/BOCINPITY/Forma/actions/workflows/build.yml/badge.svg)](https://github.com/BOCINPITY/Forma/actions/workflows/build.yml)
[![MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
![Windows x64](https://img.shields.io/badge/platform-Windows%20x64-0078D4)
![Release](https://img.shields.io/badge/version-1.0.0-green)

[Download](https://github.com/BOCINPITY/Forma/releases) · [User guide](docs/en/user-guide.md) · [Development](docs/en/development.md) · [中文使用说明](docs/user-guide.md)

![Desktop preview with six original code-drawn demo icons](docs/images/desktop-preview-light.png)

## Features

- **Compare a whole set.** Light and dark backgrounds, original/adapted views, and 32 / 48 / 64 / 96px previews at physical pixel size.
- **Adapt by shape.** The macOS material treats existing plates, circles, transparent marks and fine or complex artwork differently, with bounded optical sizing and safe margins. Gradient, solid and transparent modes are also available.
- **Keep native detail.** Read original icon frames, generate each ICO size directly, and preview actual exported frames without repeated resizing.
- **Apply in batches, restore in one step.** Back up before writing, preserve launch properties, and unwind multiple adaptation layers automatically.
- **Native and local.** C# / .NET 10 / WPF, a macOS-inspired interface and short transitions. The app mark is drawn entirely in code. No model service is required.

| Library and pixel comparison | Dark desktop preview |
| --- | --- |
| ![Library and inspector](docs/images/workspace.png) | ![Dark desktop preview](docs/images/desktop-preview-dark.png) |

Screenshots use original synthetic artwork and demo states. The interface currently uses Chinese labels. The scene simulates a background and arrangement; it does not read your wallpaper or move desktop icons. Window surfaces are drawn in WPF.

## Download and use

1. Download `Forma-<version>-win-x64.exe` from [Releases](https://github.com/BOCINPITY/Forma/releases/latest) and double-click to run. No .NET or archive utility installation is needed. Alternatively, fully extract the ZIP and run its `Forma.exe`.
2. Scan the desktop or choose a shortcut folder. Check the icons you want to change, then select **macOS 预设** (macOS preset) or adjust the style.
3. Review the set in **桌面预览** (desktop preview), inspect individual icons at 32 / 48px, then select **应用选中图标** (apply selected icons).
4. Select **恢复默认图标** (restore default icons) to return to the references saved before Forma's first change, including any custom icons you already had.

Current stable version: **`1.0.0`**, Windows x64, primarily tested on Windows 11. The compressed EXE extracts to a temporary directory on each launch and cleans up on exit. The ZIP needs extraction only once. Both include the runtime and offer the same features. Packages are unsigned, without an installer or automatic updater. Close the old version before starting a newer one.

The compressed EXE is approximately **50 MiB** and the compatible ZIP **68 MiB**, compared with **102 MiB** for the first public preview. The recommended download is roughly half the size.

**Keep `%LOCALAPPDATA%\IconAdapt\` after applying icons.** It stores persistent ICO files, backups and operation records. Shortcuts continue to reference those ICO files. Restore icons before deleting app data. Forma was previously named IconAdapt and retains its data directory for compatibility.

Supported inputs are ordinary `.lnk` application shortcuts on the desktop or in a selected folder, currently pointing to accessible `.exe` files. Scans are not recursive. Taskbar icons, Start menu entries, file associations, special Shell icons and dedicated Store app entries are outside the current scope. Permission failures are reported per item; Forma does not elevate itself, restart Explorer or clear the global icon cache. Low-resolution sources cannot regain missing detail. See the [user guide](docs/en/user-guide.md).

## Build from source

Requires Windows x64, Git and .NET 10 SDK. All scripts use PowerShell.

```powershell
git clone https://github.com/BOCINPITY/Forma.git
Set-Location .\Forma
# Optional: download and verify a project-local SDK without changing PATH
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

Create a self-contained portable archive with bilingual user guides and license notices:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

Packaging requires [7-Zip](https://www.7-zip.org/). Output: `artifacts\Forma-<version>-win-x64\`, plus a compressed EXE, ZIP and SHA-256 checksums. SDK files, build output and local test data are excluded from Git.

## Automated builds and releases

- [Build](.github/workflows/build.yml) runs on `main` pushes, pull requests and manual dispatch, validates the project and uploads the portable package as an Actions artifact.
- [Release](.github/workflows/release.yml) builds a pushed `v<version>` tag, or an existing tag selected manually, and uploads a GitHub Release. The tag must match `Directory.Build.props`.
- Stable versions are marked **Latest**; versions with a prerelease suffix, including `preview`, are marked **Pre-release**. Self-contained EXE / ZIP packages target Windows x64 and include SHA-256 checksums.

See [Build and release](docs/en/development.md) for instructions.

## Documentation and contributions

| English | 中文 |
| --- | --- |
| [User guide](docs/en/user-guide.md) | [使用说明](docs/user-guide.md) |
| [Build and release](docs/en/development.md) | [构建与发布](docs/development.md) |

Reports and pull requests are welcome; read [Contributing](CONTRIBUTING.md#english) before submitting changes. Version changes are summarized in the [changelog](CHANGELOG.md).

## License

Forma's source and original assets are licensed under the [MIT License](LICENSE). Dependencies retain their own licenses and notices; see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md) and [licenses](licenses/README.md). Forma does not distribute Apple icons or third-party application icon packs. Icons extracted from a user's installed apps remain the property of their respective rights holders.
