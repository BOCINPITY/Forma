param([string] $ScanFolder, [string] $DataRoot)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'common.ps1')
$dotnetExecutable = Resolve-IconAdaptDotnet -ProjectRoot $projectRoot
$appArguments = @()
if ($ScanFolder) { $appArguments += @('--scan-folder', [System.IO.Path]::GetFullPath($ScanFolder)) }
if ($DataRoot) { $appArguments += @('--data-root', [System.IO.Path]::GetFullPath($DataRoot)) }

Push-Location $projectRoot
try {
    & $dotnetExecutable run --project (Join-Path $projectRoot 'src\IconAdapt.App\IconAdapt.App.csproj') -- @appArguments
    if ($LASTEXITCODE -ne 0) { throw '应用退出异常。' }
}
finally { Pop-Location }
