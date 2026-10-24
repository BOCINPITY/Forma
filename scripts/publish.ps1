param([switch] $Zip)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnetExecutable = Resolve-IconAdaptDotnet -ProjectRoot $projectRoot
$projectPath = Join-Path $projectRoot 'src\IconAdapt.App\IconAdapt.App.csproj'
$outputPath = Join-Path $projectRoot 'artifacts\IconAdapt-win-x64'

Push-Location $projectRoot
try {
    & $dotnetExecutable publish $projectPath --configuration Release --runtime win-x64 --self-contained true --output $outputPath -p:PublishSingleFile=false -p:RestoreLockedMode=true
    if ($LASTEXITCODE -ne 0) { throw '便携版发布失败。' }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs\user-guide.md') -Destination (Join-Path $outputPath '使用说明.md') -Force
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
        $archivePath = Join-Path $projectRoot 'artifacts\IconAdapt-win-x64.zip'
        Compress-Archive -Path $outputPath -DestinationPath $archivePath -Force
        $archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
        "$archiveHash  IconAdapt-win-x64.zip" | Set-Content -LiteralPath ($archivePath + '.sha256') -Encoding ASCII
        Write-Output "ZIP：$archivePath"
    }
    Write-Output "启动文件：$(Join-Path $outputPath 'IconAdapt.exe')"
}
finally { Pop-Location }
