param([switch] $Zip)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')
. (Join-Path $PSScriptRoot 'package-tools.ps1')
$dotnetExecutable = Resolve-IconAdaptDotnet -ProjectRoot $projectRoot
$projectPath = Join-Path $projectRoot 'src\IconAdapt.App\IconAdapt.App.csproj'
$version = ([xml][System.IO.File]::ReadAllText((Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
if ($version -notmatch '^[0-9][0-9A-Za-z.\-]*$') { throw '无效的构建版本号。' }
$packageName = "Forma-$version-win-x64"
$outputPath = Join-Path $projectRoot (Join-Path 'artifacts' $packageName)

Push-Location $projectRoot
try {
    $artifactBoundary = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')).TrimEnd('\') + '\'
    $outputPath = [IO.Path]::GetFullPath($outputPath)
    if (-not $outputPath.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package output path.' }
    if (Test-Path -LiteralPath $outputPath) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
    & $dotnetExecutable publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $outputPath -p:PublishSingleFile=false -p:DebugType=None -p:DebugSymbols=false -p:SatelliteResourceLanguages=zh-Hans -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw '便携版发布失败。' }
    Get-ChildItem -LiteralPath $outputPath -Filter '*.pdb' -Recurse -File | Remove-Item -Force
    $packageDocs = [System.IO.Path]::GetFullPath((Join-Path $outputPath 'docs'))
    $packageBoundary = [System.IO.Path]::GetFullPath($outputPath).TrimEnd('\') + '\'
    if (-not $packageDocs.StartsWith($packageBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid package documentation path.' }
    if (Test-Path -LiteralPath $packageDocs) { Remove-Item -LiteralPath $packageDocs -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $packageDocs 'en'), (Join-Path $packageDocs 'images') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\user-guide.md') -Destination $packageDocs
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\en\user-guide.md') -Destination (Join-Path $packageDocs 'en')
    Copy-Item -Path (Join-Path $projectRoot 'docs\images\*.png') -Destination (Join-Path $packageDocs 'images')
    @('# Forma 使用说明', '', '完整解压本文件夹，双击 Forma.exe 启动。无需另装 .NET。', '', '[完整图文使用说明](docs/user-guide.md) · [English guide](docs/en/user-guide.md)', '', '应用后请保留 %LOCALAPPDATA%\IconAdapt\ 数据目录；删除数据前先在程序中恢复默认图标。') | Set-Content -LiteralPath (Join-Path $outputPath '使用说明.md') -Encoding UTF8
    @('# Forma user guide', '', 'Extract this entire folder and run Forma.exe. No separate .NET installation is required.', '', '[Illustrated user guide](docs/en/user-guide.md) · [中文说明](docs/user-guide.md)', '', 'Keep %LOCALAPPDATA%\IconAdapt\ after applying icons. Restore icons before deleting app data.') | Set-Content -LiteralPath (Join-Path $outputPath 'user-guide.md') -Encoding UTF8
    Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $outputPath -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'CHANGELOG.md') -Destination $outputPath -Force
    $packageContributing = Join-Path $outputPath 'CONTRIBUTING.md'
    if (Test-Path -LiteralPath $packageContributing) { Remove-Item -LiteralPath $packageContributing -Force }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD-PARTY-NOTICES.md') -Destination $outputPath -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'licenses') -Destination $outputPath -Recurse -Force

    $buildInfo = [ordered]@{
        BuiltAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
        Commit = (& git -C $projectRoot rev-parse HEAD)
        Sdk = (& $dotnetExecutable --version)
        RuntimeIdentifier = 'win-x64'
        SelfContained = $true
        Configuration = 'Release'
    }
    $buildInfo | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'build-info.json') -Encoding UTF8

    if ($Zip) {
        $tools = Resolve-FormaPackageTools -ProjectRoot $projectRoot
        $archivePath = Join-Path $projectRoot (Join-Path 'artifacts' ($packageName + '.zip'))
        if (Test-Path -LiteralPath $archivePath) { Remove-Item -LiteralPath $archivePath -Force }
        & $tools.Zip a -tzip $archivePath $outputPath -mx=9 -mm=Deflate -mfb=258 -mpass=15 -bso0 -bsp0
        if ($LASTEXITCODE -ne 0) { throw 'ZIP compression failed.' }

        $staging = Join-Path $projectRoot (Join-Path '.local\packaging' $packageName)
        New-Item -ItemType Directory -Path $staging -Force | Out-Null
        $solidPath = Join-Path $staging 'payload.7z'
        if (Test-Path -LiteralPath $solidPath) { Remove-Item -LiteralPath $solidPath -Force }
        Push-Location $outputPath
        try {
            & $tools.Solid a -t7z $solidPath '.\*' -mx=9 -md=128m -mfb=273 -ms=on -mqs=on -mmt=2 -bso0 -bsp0
            if ($LASTEXITCODE -ne 0) { throw 'Solid compression failed.' }
        } finally { Pop-Location }

        $stubPath = Join-Path $staging 'forma.sfx'
        Copy-Item -LiteralPath $tools.Sfx -Destination $stubPath -Force
        & (Join-Path $PSScriptRoot 'copy-executable-brand.ps1') -Source (Join-Path $outputPath 'Forma.exe') -Destination $stubPath
        $launcherPath = Join-Path $projectRoot (Join-Path 'artifacts' ($packageName + '.exe'))
        $launcher = [IO.File]::Create($launcherPath)
        try {
            $stub = [IO.File]::OpenRead($stubPath)
            try { $stub.CopyTo($launcher) } finally { $stub.Dispose() }
            $config = ';!@Install@!UTF-8!' + "`n" + 'Title="Forma"' + "`n" + 'RunProgram="Forma.exe"' + "`n" + ';!@InstallEnd@!' + "`n"
            $configBytes = [Text.Encoding]::UTF8.GetBytes($config)
            $launcher.Write($configBytes, 0, $configBytes.Length)
            $payload = [IO.File]::OpenRead($solidPath)
            try { $payload.CopyTo($launcher) } finally { $payload.Dispose() }
        } finally { $launcher.Dispose() }

        foreach ($package in @($archivePath, $launcherPath)) {
            $archiveHash = (Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash.ToLowerInvariant()
            "$archiveHash  $([IO.Path]::GetFileName($package))" | Set-Content -LiteralPath ($package + '.sha256') -Encoding ASCII
            Write-Output ("Package: {0} ({1:N2} MiB)" -f $package, ((Get-Item -LiteralPath $package).Length / 1MB))
        }
    }
    Write-Output "启动文件：$(Join-Path $outputPath 'Forma.exe')"
}
finally { Pop-Location }
