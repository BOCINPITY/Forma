param([string] $SdkVersion = '10.0.402')

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$projectRoot = Split-Path -Parent $PSScriptRoot
$toolsRoot = Join-Path $projectRoot '.local'
$sdkRoot = Join-Path $toolsRoot 'dotnet'
$dotnetExecutable = Join-Path $sdkRoot 'dotnet.exe'

if (Test-Path -LiteralPath $dotnetExecutable) {
    $actualVersion = & $dotnetExecutable --version
    if ($LASTEXITCODE -eq 0 -and $actualVersion -eq $SdkVersion) {
        Write-Output "项目本地 SDK 已可用：$actualVersion"
        exit 0
    }
}

$metadata = Invoke-RestMethod 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
$sdk = $metadata.releases | ForEach-Object { $_.sdks } | Where-Object { $_.version -eq $SdkVersion } | Select-Object -First 1
if (-not $sdk) {
    $sdk = $metadata.releases | ForEach-Object { $_.sdk } | Where-Object { $_.version -eq $SdkVersion } | Select-Object -First 1
}
if (-not $sdk) { throw "微软发布元数据中没有找到 SDK $SdkVersion。" }
$sdkFile = $sdk.files | Where-Object { $_.rid -eq 'win-x64' -and $_.name -like '*.zip' } | Select-Object -First 1
if (-not $sdkFile) { throw '没有找到 Windows x64 SDK ZIP。' }
$downloadUri = [uri]$sdkFile.url
if ($downloadUri.Scheme -ne 'https' -or $downloadUri.Host -notin @('builds.dotnet.microsoft.com', 'download.visualstudio.microsoft.com')) {
    throw 'SDK 下载地址未通过官方域名检查。'
}

New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
$archivePath = Join-Path $toolsRoot "dotnet-sdk-$SdkVersion-win-x64.zip"
if (-not (Test-Path -LiteralPath $archivePath) -or (Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $sdkFile.hash) {
    Write-Output "正在从微软下载 .NET SDK $SdkVersion…"
    $downloadClient = New-Object System.Net.WebClient
    try { $downloadClient.DownloadFile($sdkFile.url, $archivePath) }
    finally { $downloadClient.Dispose() }
}
if ((Get-FileHash -LiteralPath $archivePath -Algorithm SHA512).Hash -ne $sdkFile.hash) {
    throw 'SDK 校验失败，停止解压。'
}
Expand-Archive -LiteralPath $archivePath -DestinationPath $sdkRoot -Force
& $dotnetExecutable --version
if ($LASTEXITCODE -ne 0) { throw 'SDK 安装验证失败。' }
Write-Output 'SDK 已安装到项目 .local 目录，没有修改系统 PATH。'
