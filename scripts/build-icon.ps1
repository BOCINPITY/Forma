$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$projectRoot = Split-Path -Parent $PSScriptRoot
$assetPath = Join-Path $projectRoot 'src\IconAdapt.App\Assets\IconAdapt.ico'

function New-RoundedPath {
    param([single] $X, [single] $Y, [single] $Width, [single] $Height, [single] $Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = 2 * $Radius
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

# Geometry matches Assets\IconAdapt.svg. Draw at 4x resolution before resampling.
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $large = New-Object System.Drawing.Bitmap ($size * 4), ($size * 4)
    $graphics = [System.Drawing.Graphics]::FromImage($large)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.ScaleTransform(($size * 4 / 256), ($size * 4 / 256))
        $rectangles = @(@(12, 12, 232, 232, 60, '#7465DE'), @(56, 56, 64, 64, 16, '#FFFFFF'), @(136, 56, 64, 64, 16, '#DAD3FF'), @(56, 136, 64, 64, 16, '#DAD3FF'), @(136, 136, 64, 64, 16, '#FFFFFF'))
        foreach ($rectangle in $rectangles) {
            $path = New-RoundedPath $rectangle[0] $rectangle[1] $rectangle[2] $rectangle[3] $rectangle[4]
            $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($rectangle[5]))
            try { $graphics.FillPath($brush, $path) }
            finally { $path.Dispose(); $brush.Dispose() }
        }
        $frame = New-Object System.Drawing.Bitmap $size, $size
        $outputGraphics = [System.Drawing.Graphics]::FromImage($frame)
        $stream = New-Object System.IO.MemoryStream
        try {
            $outputGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $outputGraphics.DrawImage($large, 0, 0, $size, $size)
            $frame.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
            $frames.Add($stream.ToArray())
        }
        finally { $stream.Dispose(); $outputGraphics.Dispose(); $frame.Dispose() }
    }
    finally { $graphics.Dispose(); $large.Dispose() }
}

$file = [System.IO.File]::Create($assetPath)
$writer = New-Object System.IO.BinaryWriter $file
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($index = 0; $index -lt $sizes.Count; $index++) {
        $entrySize = $sizes[$index] % 256
        $writer.Write([byte]$entrySize); $writer.Write([byte]$entrySize)
        $writer.Write([byte]0); $writer.Write([byte]0)
        $writer.Write([uint16]1); $writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$index].Length); $writer.Write([uint32]$offset)
        $offset += $frames[$index].Length
    }
    foreach ($frame in $frames) { $writer.Write($frame) }
}
finally { $writer.Dispose(); $file.Dispose() }
Write-Output "已生成：$assetPath"
