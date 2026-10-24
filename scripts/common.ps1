function Resolve-IconAdaptDotnet {
    param([Parameter(Mandatory = $true)][string] $ProjectRoot)

    $localDotnet = Join-Path $ProjectRoot '.local\dotnet\dotnet.exe'
    if (Test-Path -LiteralPath $localDotnet) {
        $env:DOTNET_ROOT = Split-Path -Parent $localDotnet
        return $localDotnet
    }

    $installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($installedDotnet) { return $installedDotnet.Source }

    throw '未找到 .NET SDK。请先运行 scripts\bootstrap.ps1，或安装 .NET 10 SDK。'
}
