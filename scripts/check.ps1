param([switch] $SkipSmoke)

$ErrorActionPreference = 'Stop'

$projectRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $projectRoot 'IconAdapt.slnx'

. (Join-Path $PSScriptRoot 'common.ps1')
$dotnetExecutable = Resolve-IconAdaptDotnet -ProjectRoot $projectRoot

Push-Location $projectRoot
try {
    & $dotnetExecutable --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK 解析失败，请检查 global.json 和已安装的 SDK。' }

    & $dotnetExecutable restore $solutionPath --locked-mode
    if ($LASTEXITCODE -ne 0) { throw '依赖还原失败。' }

    & $dotnetExecutable build $solutionPath --configuration Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Release 构建失败。' }

    if (-not $SkipSmoke) {
        $smokeProjects = @(
            'tests\IconAdapt.Imaging.Smoke\IconAdapt.Imaging.Smoke.csproj',
            'tests\IconAdapt.Windows.Smoke\IconAdapt.Windows.Smoke.csproj',
            'tests\IconAdapt.Integration.Smoke\IconAdapt.Integration.Smoke.csproj',
            'tests\IconAdapt.App.Smoke\IconAdapt.App.Smoke.csproj'
        )
        foreach ($smokeProject in $smokeProjects) {
            & $dotnetExecutable run --project (Join-Path $projectRoot $smokeProject) --configuration Release --no-build
            if ($LASTEXITCODE -ne 0) { throw "验证失败：$smokeProject" }
        }
    }

    Write-Output '检查通过。真实桌面效果、多 DPI 和安装体验仍需单独验证。'
}
finally {
    Pop-Location
}
