function Resolve-FormaPackageTools {
    param([Parameter(Mandatory = $true)][string] $ProjectRoot)

    $sevenZip = Get-Command 7z -ErrorAction SilentlyContinue
    $sevenZipPath = if ($sevenZip) { $sevenZip.Source } else { Join-Path $env:ProgramFiles '7-Zip\7z.exe' }
    if (-not (Test-Path -LiteralPath $sevenZipPath)) { throw 'Packaging requires 7-Zip (7z.exe). Install it from https://www.7-zip.org/.' }

    $toolRoot = Join-Path $ProjectRoot '.local\package-tools'
    $archive = Join-Path $toolRoot 'lzma2601.7z'
    $sdkRoot = Join-Path $toolRoot 'lzma2601'
    $expectedHash = 'b860f17f9df3c0524dd2ef2c639ab5e43ad0006b77b8f7bb6d191bf528536885'
    New-Item -ItemType Directory -Path $toolRoot -Force | Out-Null
    if (-not (Test-Path -LiteralPath $archive)) {
        $ProgressPreference = 'SilentlyContinue'
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -Uri 'https://github.com/ip7z/7zip/releases/download/26.01/lzma2601.7z' -OutFile $archive -UseBasicParsing
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $expectedHash) {
        throw 'LZMA SDK checksum mismatch. Remove .local\package-tools\lzma2601.7z and retry.'
    }
    # Always extract the verified archive, never trust a stale cached executable.
    & $sevenZipPath x $archive "-o$sdkRoot" -y -bso0 -bsp0
    if ($LASTEXITCODE -ne 0) { throw 'LZMA SDK extraction failed.' }
    return @{ Zip = $sevenZipPath; Solid = (Join-Path $sdkRoot 'bin\x64\7zr.exe'); Sfx = (Join-Path $sdkRoot 'bin\7zSD.sfx') }
}
