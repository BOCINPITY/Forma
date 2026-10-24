# Development and releases

[Documentation](README.md) · [简体中文](../development.md)

## Windows setup

Use Windows x64, Git, PowerShell, and a .NET 10 SDK. WPF and Windows Shell integration require Windows. The initial desktop target is Windows 11.

```powershell
git clone https://github.com/BOCINPITY/Forma.git
Set-Location .\Forma
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\bootstrap.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\check.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\run.ps1
```

If a compatible .NET 10 SDK is already installed, bootstrap is optional. `global.json` defines SDK resolution. Bootstrap downloads SDK **10.0.402** from Microsoft's official release metadata, verifies SHA-512, and extracts it to `.local\dotnet`. It neither elevates nor changes the system PATH. The other scripts prefer this local SDK, then look for `dotnet` on PATH.

NuGet versions and `packages.lock.json` files are committed. Checks use **locked restore**; packaging sets `RestoreLockedMode=true`. Dependency changes must deliberately regenerate lock files and update third-party notices and upstream license copies as needed.

## Check and package

`scripts\check.ps1` restores the solution in locked mode, builds Release, and runs four smoke programs. `-SkipSmoke` restores and builds only.

| Program | Main coverage |
| --- | --- |
| `IconAdapt.Imaging.Smoke` | Source frames, alpha sampling, small details, rounded compositing, ICO/WIC decoding, and concurrency |
| `IconAdapt.Windows.Smoke` | Real Shell COM, preserved properties, backups, layered restoration, conflicts, and recovery |
| `IconAdapt.Integration.Smoke` | Extract → render → apply → reload records → restore; untouched items and launch properties |
| `IconAdapt.App.Smoke` | Export/preview pixel agreement, settings compatibility, compact layouts, restoration results, and synthetic screenshots |

The programs require no separate test framework and return nonzero on failure. Write tests use shortcuts created under `.local`. The integration program also scans the real desktop read-only and reports counts. Passing a build or smoke suite does not establish real desktop appearance, mixed-monitor DPI behavior, or every permissions scenario.

Create the portable package with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\publish.ps1 -Zip
```

Output is `artifacts\Forma-<version>-win-x64\`, with `Forma.exe`, the runtime, native dependencies, documentation, license notices, and build information. `-Zip` adds the ZIP archive and a SHA-256 checksum file. Distribution is self-contained and multi-file; retain the complete folder. Output is versioned to avoid overwriting another version that is running. Current packages are unsigned.

Do not commit `.local`, `artifacts`, generated icons, actual desktop shortcuts, backups, operation records, or credentials.

## Isolated UI work

```powershell
.\scripts\run.ps1 -ScanFolder '.\.local\demo' -DataRoot '.\.local\demo-data'
```

Create your own test `.lnk` files in the scan folder. A separate data root keeps test operations apart from desktop history. Direct application arguments also support `--scan-folder <absolute-path>`, `--data-root <absolute-path>`, and `--smoke-ui <output-png-path>`. Screenshot mode waits for scanning and rendering, captures the window, and exits without applying or restoring. Failure produces an `.error.txt` file.

`IconAdapt.App.Smoke --motion` and `IconAdapt.App.Smoke --desktop-preview` select specialized checks in the **test executable**; these are not Forma application arguments. UI checks load actual WPF resources through a test-only construction path that suppresses production startup services. Layout checks disable motion to inspect final geometry, while motion checks validate transitions separately.

## GitHub Actions

The Windows workflow [`build.yml`](https://github.com/BOCINPITY/Forma/blob/main/.github/workflows/build.yml) runs on pushes to `main`, pull requests targeting `main`, and manual dispatch. It performs the project checks and produces portable build artifacts. Workflow artifacts are useful for reviewing a build; published versions are available through GitHub Releases.

The [`release.yml`](https://github.com/BOCINPITY/Forma/blob/main/.github/workflows/release.yml) workflow runs for a pushed version tag or a manual dispatch with the `tag` input. The tag must be **`v` followed by the `Version` in `Directory.Build.props`**, currently `v0.1.0-preview.9`. It validates the version, checks the project, packages the Windows x64 archive and checksum, and publishes them to the corresponding GitHub Release. A version containing a prerelease suffix, including `preview`, is published as a prerelease.

For a release, update `Directory.Build.props`, the changelog, and affected documentation together; commit the intended source, then release the matching tag. A manual release must refer to the intended tag rather than an unrelated checkout. Neither a local build nor a version edit alone publishes a release.

## Repository conventions

Use `main` for the baseline, `feat/<topic>` for features, and `fix/<topic>` for fixes. Keep commits focused, with prefixes such as `feat:`, `fix:`, `docs:`, or `chore:`. Before committing, review `git diff --check`, the actual diff, and `git status --short` for unintended data.

The current version is **0.1.0-preview.9**, sourced from `Directory.Build.props`; the interface reads assembly information. The product and application assembly are Forma. Source namespaces, the solution name, and the existing local data directory retain IconAdapt for compatibility. Behavior changes belong in the [changelog](../../CHANGELOG.md); architectural changes also belong in the relevant guides.

Forma is [MIT licensed](../../LICENSE). Third-party licenses remain separate; preserve [notices](../../THIRD-PARTY-NOTICES.md) and [upstream license files](../../licenses/) in distributions. Real application icons used during local investigation are not redistributable project assets. Documentation screenshots use synthetic test artwork.
