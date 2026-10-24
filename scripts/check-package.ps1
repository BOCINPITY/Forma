param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'package-tools.ps1')
$tools = Resolve-FormaPackageTools -ProjectRoot $projectRoot
$version = ([xml][IO.File]::ReadAllText((Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
$packageName = "Forma-$version-win-x64"
$scratch = Join-Path $projectRoot ('.local\package-smoke\' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null
$zip = Join-Path $projectRoot "artifacts\$packageName.zip"
$sfx = Join-Path $projectRoot "artifacts\$packageName.exe"

foreach ($package in @($zip, $sfx)) {
    $expected = (Get-Content -LiteralPath ($package + '.sha256') -Raw).Trim()
    $hash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($expected -cne "$hash  $([IO.Path]::GetFileName($package))") { throw 'Package checksum mismatch.' }
    & $tools.Zip t $package -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'Package integrity check failed.' }
}
& $tools.Zip x $zip "-o$scratch" -y -bso0 -bsp0
if ($LASTEXITCODE -ne 0) { throw 'Package extraction failed.' }
$unpacked = Join-Path $scratch $packageName
if (Get-ChildItem -LiteralPath $unpacked -Recurse -File -Filter '*.pdb') { throw 'Debug symbols must not be shipped.' }
foreach ($required in @('Forma.exe', 'coreclr.dll', 'PresentationFramework.dll', 'libSkiaSharp.dll', 'LICENSE',
    'docs\user-guide.md', 'docs\en\user-guide.md', 'licenses\LZMA-SDK-LICENSE.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $unpacked $required))) { throw "Missing package file: $required" }
}
$appVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $unpacked 'Forma.exe'))
$launcherVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($sfx)
if ($appVersion.ProductVersion -cne $launcherVersion.ProductVersion -or $launcherVersion.ProductName -cne 'Forma') {
    throw 'The compressed launcher must carry the app brand and version.'
}

# These shortcuts and app data live only under .local. Neither launch scans the real desktop.
$scanFolder = Join-Path $scratch 'scan'
New-Item -ItemType Directory -Path $scanFolder -Force | Out-Null
$linkPath = Join-Path $scanFolder 'Forma-fixture.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($linkPath)
    $shortcut.TargetPath = Join-Path $unpacked 'Forma.exe'
    $shortcut.WorkingDirectory = $unpacked
    $shortcut.Arguments = '--synthetic-fixture-not-executed'
    $shortcut.Save()
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut)
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
$originalHash = (Get-FileHash -LiteralPath $linkPath -Algorithm SHA256).Hash

foreach ($kind in @('zip', 'compressed')) {
    $executable = if ($kind -eq 'zip') { Join-Path $unpacked 'Forma.exe' } else { $sfx }
    $capture = Join-Path $scratch "$kind.png"
    $dataRoot = Join-Path $scratch "$kind-data"
    $start = New-Object Diagnostics.ProcessStartInfo
    $start.FileName = $executable
    $start.WorkingDirectory = $scratch
    $start.UseShellExecute = $false
    $start.Arguments = '--scan-folder "' + $scanFolder + '" --data-root "' + $dataRoot + '" --smoke-ui "' + $capture + '"'
    # An installed SDK/runtime must not mask a missing portable runtime.
    $start.EnvironmentVariables['DOTNET_ROOT'] = Join-Path $scratch 'no-installed-runtime'
    $start.EnvironmentVariables['DOTNET_ROOT_X64'] = Join-Path $scratch 'no-installed-runtime'
    $start.EnvironmentVariables['DOTNET_MULTILEVEL_LOOKUP'] = '0'
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (-not $process.WaitForExit(45000)) { $process.Kill(); throw "Packaged $kind app timed out." }
        if ($process.ExitCode -ne 0) { throw "Packaged $kind app exited with $($process.ExitCode)." }
    } finally { $process.Dispose() }
    if (Test-Path -LiteralPath ($capture + '.error.txt')) { throw (Get-Content -LiteralPath ($capture + '.error.txt') -Raw) }
    if (-not (Test-Path -LiteralPath $capture) -or -not (Test-Path -LiteralPath ($capture + '.json'))) {
        throw "Packaged $kind app did not produce its UI/render result."
    }
    $report = Get-Content -LiteralPath ($capture + '.json') -Raw | ConvertFrom-Json
    if ($report.Version -cne $version -or $report.Scanned -ne 1 -or $report.Ready -ne 1 -or -not $report.BundledRuntime) {
        throw "Packaged $kind app failed bundled-runtime, shortcut extraction or icon rendering checks."
    }
    if ((Get-FileHash -LiteralPath $linkPath -Algorithm SHA256).Hash -cne $originalHash) { throw 'Preview changed the fixture shortcut.' }
    Write-Output "PASS: $kind package, bundled runtime, WPF startup, native icon extraction, Skia render and read-only preview."
}
