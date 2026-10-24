$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'IconAdapt.slnx'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '未找到 dotnet。请安装 .NET 10 SDK，重新打开 PowerShell 后再运行检查。'
}

Push-Location $projectRoot
try {
    & dotnet --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK 解析失败，请检查 global.json 和已安装的 SDK。' }

    & dotnet restore $solutionPath
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }

    & dotnet build $solutionPath --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }

    Write-Output 'Release 构建通过。当前尚无自动化测试工程，仍需执行界面和功能验证。'
}
finally {
    Pop-Location
}
