param([string] $PreviewPath)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, PresentationFramework, WindowsBase
$projectRoot = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $projectRoot 'src\IconAdapt.App\Assets'
if (-not $PreviewPath) { $PreviewPath = Join-Path $projectRoot '.local\brand-preview.png' }

# Canonical Forma artwork. SVG, ICO and the WPF mark share paths and materials.
# No font glyphs, external images or image models are used for the mark.
$basePath = 'M68,8 H188 Q248,8 248,68 V188 Q248,248 188,248 H68 Q8,248 8,188 V68 Q8,8 68,8 Z'
$frontPath = 'M80,88 H144 Q176,88 176,120 V176 Q176,208 144,208 H80 Q48,208 48,176 V120 Q48,88 80,88 Z'
$gradients = [ordered]@{
    Base = @{ X1 = 24; Y1 = 8; X2 = 224; Y2 = 248; Stops = @(@(0, '#475E74'), @(.48, '#2D4257'), @(1, '#1B2A3B')) }
    Rear = @{ X1 = 128; Y1 = 48; X2 = 208; Y2 = 164; Stops = @(@(0, '#CAE5EF'), @(.5, '#A5CBDF'), @(1, '#729FBC')) }
    Porcelain = @{ X1 = 80; Y1 = 88; X2 = 144; Y2 = 208; Stops = @(@(0, '#FFFFFF'), @(.48, '#F8F6F0'), @(1, '#DCE1DD')) }
    Rim = @{ X1 = 0; Y1 = 8; X2 = 0; Y2 = 248; Stops = @(@(0, '#52FFFFFF'), @(.45, '#05FFFFFF'), @(1, '#18000000')) }
}
$shapes = @(
    @{ Fill = 'Base'; Path = $basePath },
    @{ Fill = 'Rear'; Path = 'M140,48 H180 Q208,48 208,76 V132 Q208,160 180,160 H140 Q112,160 112,132 V76 Q112,48 140,48 Z' },
    # Four faint silhouettes produce a restrained contact shadow without filters.
    # The compact mark omits them, keeping the negative space clean at <=32px.
    @{ Fill = '#06091624'; Path = $frontPath; X = 1; Y = 7; Detail = $true },
    @{ Fill = '#08091624'; Path = $frontPath; X = 1; Y = 5; Detail = $true },
    @{ Fill = '#0A091624'; Path = $frontPath; X = 1; Y = 3; Detail = $true },
    @{ Fill = '#10091624'; Path = $frontPath; X = 0; Y = 1; Detail = $true },
    @{ Fill = 'Porcelain'; Path = $frontPath },
    @{ Fill = 'Base'; Path = 'M92,120 H132 Q144,120 144,132 V164 Q144,176 132,176 H92 Q80,176 80,164 V132 Q80,120 92,120 Z' },
    @{ Stroke = 'Rim'; Width = 1.2; Path = $basePath; Detail = $true },
    @{ Stroke = '#D9FFFFFF'; Width = 1.5; Path = 'M52,114 Q57,89 80,89 H144 Q165,89 172,108'; Detail = $true }
)
$sizes = @(16, 20, 24, 32, 48, 64, 128, 256)
$utf8 = New-Object System.Text.UTF8Encoding $false
function Svg-Color {
    param([string] $Value)
    if ($Value.StartsWith('#') -and $Value.Length -eq 9) { return @{ Color = '#' + $Value.Substring(3); Opacity = ([Convert]::ToInt32($Value.Substring(1, 2), 16) / 255).ToString('0.######', [Globalization.CultureInfo]::InvariantCulture) } }
    return @{ Color = $Value; Opacity = '1' }
}
$svgLines = @('<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256" role="img" aria-label="Forma">', '  <title>Forma - two forms, one frame</title>', '  <defs>')
foreach ($key in $gradients.Keys) {
    $g = $gradients[$key]
    $svgLines += '    <linearGradient id="' + $key + '" gradientUnits="userSpaceOnUse" x1="' + $g.X1 + '" y1="' + $g.Y1 + '" x2="' + $g.X2 + '" y2="' + $g.Y2 + '">'
    foreach ($stop in $g.Stops) { $c = Svg-Color $stop[1]; $svgLines += '      <stop offset="' + $stop[0] + '" stop-color="' + $c.Color + '" stop-opacity="' + $c.Opacity + '"/>' }
    $svgLines += '    </linearGradient>'
}
$svgLines += '  </defs>'
foreach ($shape in $shapes) {
    $attributes = ''; if (-not $shape.Fill) { $attributes = ' fill="none"' }
    foreach ($kind in @('Fill', 'Stroke')) {
        if (-not $shape[$kind]) { continue }
        $c = Svg-Color $shape[$kind]
        $paint = $c.Color
        if ($gradients.Contains($shape[$kind])) { $paint = 'url(#' + $shape[$kind] + ')' }
        $attributes += ' ' + $kind.ToLowerInvariant() + '="' + $paint + '" ' + $kind.ToLowerInvariant() + '-opacity="' + $c.Opacity + '"'
    }
    if ($shape.Width) { $attributes += ' stroke-width="' + $shape.Width + '" stroke-linecap="round"' }
    if ($shape.X -or $shape.Y) { $attributes += ' transform="translate(' + [int]$shape.X + ' ' + [int]$shape.Y + ')"' }
    $svgLines += '  <path' + $attributes + ' d="' + $shape.Path + '"/>'
}
$svgLines += '</svg>'
[IO.File]::WriteAllText((Join-Path $assets 'IconAdapt.svg'), ($svgLines -join "`n") + "`n", $utf8)
$shapeCode = @()
foreach ($shape in $shapes) {
    $fillCode = 'null'; $strokeCode = 'null'; $width = 0
    if ($shape.Fill) { $fillCode = 'Paint("' + $shape.Fill + '")' }
    if ($shape.Stroke) { $strokeCode = 'Paint("' + $shape.Stroke + '")'; $width = $shape.Width }
    $guard = ''; if ($shape.Detail) { $guard = 'if (detailed) ' }
    $shapeCode += '        ' + $guard + 'Add(drawing, ' + $fillCode + ', ' + $strokeCode + ', ' + $width + ', "' + $shape.Path + '", ' + [int]$shape.X + ', ' + [int]$shape.Y + ');'
}
$gradientCode = @()
foreach ($key in $gradients.Keys) {
    $g = $gradients[$key]
    $stopsCode = @(); foreach ($stop in $g.Stops) { $stopsCode += 'new GradientStop(Color("' + $stop[1] + '"), ' + $stop[0] + ')' }
    $gradientCode += '            "' + $key + '" => new LinearGradientBrush(new GradientStopCollection { ' + ($stopsCode -join ', ') + ' }, new Point(' + $g.X1 + ', ' + $g.Y1 + '), new Point(' + $g.X2 + ', ' + $g.Y2 + ')) { MappingMode = BrushMappingMode.Absolute },'
}
$markSource = @'
// Generated by scripts/build-icon.ps1 from the same paths as the SVG and ICO.
// Edit the canonical geometry in that script, then regenerate all brand assets.
using System.Windows;
using System.Windows.Media;

namespace IconAdapt.App.Controls;

/// <summary>Forma's code-drawn mark, independent of fonts and bitmap scaling.</summary>
public sealed class BrandMark : FrameworkElement
{
    private static readonly DrawingGroup Artwork = CreateArtwork(true);
    private static readonly DrawingGroup CompactArtwork = CreateArtwork(false);

    public BrandMark()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    protected override Size MeasureOverride(Size availableSize) => new(28, 28);

    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);
        double side = Math.Min(ActualWidth, ActualHeight);
        if (side <= 0) return;
        context.PushTransform(new TranslateTransform((ActualWidth - side) / 2, (ActualHeight - side) / 2));
        context.PushTransform(new ScaleTransform(side / 256, side / 256));
        context.DrawDrawing(side > 32 ? Artwork : CompactArtwork);
        context.Pop();
        context.Pop();
    }

    private static Color Color(string color) => (Color)ColorConverter.ConvertFromString(color);

    private static Brush Paint(string key)
    {
        Brush brush = key switch
        {
__GRADIENTS__
            _ => new SolidColorBrush(Color(key))
        };
        brush.Freeze();
        return brush;
    }

    private static void Add(DrawingGroup drawing, Brush? fill, Brush? stroke, double width, string path, double x, double y)
    {
        var geometry = Geometry.Parse(path);
        if (x != 0 || y != 0) { geometry = geometry.Clone(); geometry.Transform = new TranslateTransform(x, y); }
        drawing.Children.Add(new GeometryDrawing(fill, stroke == null ? null : new Pen(stroke, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, geometry));
    }

    private static DrawingGroup CreateArtwork(bool detailed)
    {
        var drawing = new DrawingGroup();
__DRAWING_PATHS__
        drawing.Freeze();
        return drawing;
    }
}
'@
$markSource = $markSource.Replace('__DRAWING_PATHS__', ($shapeCode -join "`r`n"))
$markSource = $markSource.Replace('__GRADIENTS__', ($gradientCode -join "`r`n"))
$markSource = ($markSource -replace "`r?`n", "`r`n").TrimEnd() + "`r`n"
[IO.File]::WriteAllText((Join-Path $projectRoot 'src\IconAdapt.App\Controls\BrandMark.cs'), $markSource, $utf8)

function Draw-Brand {
    param($Context, [double] $X, [double] $Y, [double] $Size)
    $Context.PushTransform((New-Object System.Windows.Media.TranslateTransform $X, $Y))
    $Context.PushTransform((New-Object System.Windows.Media.ScaleTransform ($Size / 256), ($Size / 256)))
    foreach ($shape in $shapes) {
        if ($shape.Detail -and $Size -le 32) { continue }
        $fill = $null; $pen = $null
        if ($shape.Fill) { $fill = New-BrandPaint $shape.Fill }
        if ($shape.Stroke) { $pen = New-Object System.Windows.Media.Pen (New-BrandPaint $shape.Stroke), $shape.Width; $pen.StartLineCap = 'Round'; $pen.EndLineCap = 'Round' }
        $geometry = [System.Windows.Media.Geometry]::Parse($shape.Path)
        if ($shape.X -or $shape.Y) { $geometry = $geometry.Clone(); $geometry.Transform = New-Object System.Windows.Media.TranslateTransform ([double]$shape.X), ([double]$shape.Y) }
        $Context.DrawGeometry($fill, $pen, $geometry)
    }
    $Context.Pop()
    $Context.Pop()
}

function New-BrandPaint {
    param([string] $Key)
    if ($gradients.Contains($Key)) {
        $g = $gradients[$Key]
        $brush = New-Object System.Windows.Media.LinearGradientBrush
        $brush.MappingMode = 'Absolute'
        $brush.StartPoint = New-Object System.Windows.Point $g.X1, $g.Y1
        $brush.EndPoint = New-Object System.Windows.Point $g.X2, $g.Y2
        foreach ($stop in $g.Stops) { $brush.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.ColorConverter]::ConvertFromString($stop[1])), ([double]$stop[0]))) }
        return $brush
    }
    return New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString($Key))
}

function Save-Visual {
    param($Visual, [int] $Width, [int] $Height)
    $bitmap = New-Object System.Windows.Media.Imaging.RenderTargetBitmap $Width, $Height, 96, 96, ([System.Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($Visual)
    $encoder = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = New-Object System.IO.MemoryStream
    try { $encoder.Save($stream); return ,$stream.ToArray() }
    finally { $stream.Dispose() }
}

# Each ICO frame is rendered at its own size; no large raster is resampled.
$frames = New-Object 'System.Collections.Generic.List[byte[]]'
foreach ($size in $sizes) {
    $visual = New-Object System.Windows.Media.DrawingVisual
    $context = $visual.RenderOpen()
    try { Draw-Brand $context 0 0 $size }
    finally { $context.Close() }
    $frames.Add((Save-Visual $visual $size $size))
}
$file = [IO.File]::Create((Join-Path $assets 'IconAdapt.ico'))
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
finally { $writer.Dispose() }

# Review the actual small sizes on both light and dark backgrounds.
$sheet = New-Object System.Windows.Media.DrawingVisual
$context = $sheet.RenderOpen()
try {
    $light = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#F4F5F6'))
    $dark = New-Object System.Windows.Media.SolidColorBrush ([System.Windows.Media.ColorConverter]::ConvertFromString('#151D27'))
    $context.DrawRectangle($light, $null, (New-Object System.Windows.Rect 0, 0, 1000, 520))
    $context.DrawRectangle($dark, $null, (New-Object System.Windows.Rect 0, 300, 1000, 220))
    Draw-Brand $context 42 24 240
    $x = 330
    foreach ($size in @(16, 20, 24, 32, 48, 64, 128)) {
        Draw-Brand $context $x 116 $size
        Draw-Brand $context $x 360 $size
        $label = New-Object System.Windows.Media.FormattedText (($size.ToString() + ' px'), [Globalization.CultureInfo]::InvariantCulture, [System.Windows.FlowDirection]::LeftToRight, (New-Object System.Windows.Media.Typeface 'Segoe UI'), 12, [System.Windows.Media.Brushes]::SlateGray, 1.0)
        $context.DrawText($label, (New-Object System.Windows.Point $x, 264))
        $x += [Math]::Max(64, $size + 28)
    }
}
finally { $context.Close() }
[IO.Directory]::CreateDirectory((Split-Path -Parent $PreviewPath)) | Out-Null
[IO.File]::WriteAllBytes($PreviewPath, (Save-Visual $sheet 1000 520))
Write-Output 'Generated Forma SVG, 8-frame ICO and vector BrandMark from canonical paths.'
Write-Output "Preview: $PreviewPath"
