param([switch] $Zip)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnetExecutable = Resolve-IconAdaptDotnet -ProjectRoot $projectRoot
$projectPath = Join-Path $projectRoot 'src\IconAdapt.App\IconAdapt.App.csproj'
$version = ([xml][System.IO.File]::ReadAllText((Join-Path $projectRoot 'Directory.Build.props'))).Project.PropertyGroup.Version
if ($version -notmatch '^[0-9][0-9A-Za-z.\-]*$') { throw '无效的构建版本号。' }
$packageName = "Forma-$version-win-x64"
$outputPath = Join-Path $projectRoot (Join-Path 'artifacts' $packageName)

Push-Location $projectRoot
try {
    & $dotnetExecutable publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $outputPath -p:PublishSingleFile=false -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw '便携版发布失败。' }
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
        $archivePath = Join-Path $projectRoot (Join-Path 'artifacts' ($packageName + '.zip'))
        Compress-Archive -Path $outputPath -DestinationPath $archivePath -Force
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$archiveHash  $packageName.zip" | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ASCII
        Write-Output "ZIP：$archivePath"
    }
    Write-Output "启动文件：$(Join-Path $outputPath 'Forma.exe')"
}
finally { Pop-Location }
