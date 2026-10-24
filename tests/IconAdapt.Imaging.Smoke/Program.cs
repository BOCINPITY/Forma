using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;
using IconAdapt.Core;
using IconAdapt.Imaging;
using SkiaSharp;

var output = Path.GetFullPath(args.Length > 0 ? args[0] : ".local/render-samples");
Directory.CreateDirectory(output);
var renderer = new IconRenderer();
var checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}
void Reject(Action action, string message)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new InvalidOperationException(message);
}
byte[] Fixture(int width, int height, Action<SKCanvas> draw)
{
    using var surface = SKSurface.Create(new SKImageInfo(width, height));
    surface.Canvas.Clear(SKColors.Transparent);
    draw(surface.Canvas);
    using var image = surface.Snapshot();
    using var png = image.Encode(SKEncodedImageFormat.Png, 100);
    return png.ToArray();
}
(int Width, int Height, int Left, int Top) RedBounds(byte[] png)
{
    using var image = SKBitmap.Decode(png);
    var left = image.Width; var top = image.Height; var right = -1; var bottom = -1;
    for (var y = 0; y < image.Height; y++)
    for (var x = 0; x < image.Width; x++)
    {
        var pixel = image.GetPixel(x, y);
        if (pixel.Alpha < 32 || pixel.Red < 200 || pixel.Green > 80 || pixel.Blue > 80) continue;
        left = Math.Min(left, x); top = Math.Min(top, y);
        right = Math.Max(right, x); bottom = Math.Max(bottom, y);
    }
    return (right - left + 1, bottom - top + 1, left, top);
}

using var red = new SKPaint { Color = new SKColor(245, 45, 35), IsAntialias = true };
var padded = Fixture(256, 128, canvas => canvas.DrawRect(new SKRect(160, 32, 208, 80), red));
var compact = Fixture(48, 48, canvas => canvas.Clear(red.Color));
var noPlate = new IconStyle { Backdrop = BackdropStyle.None };
var cropped = renderer.Render(padded, noPlate);
var compactResult = renderer.Render(compact, noPlate);
var bounds = RedBounds(cropped.PreviewPng);
Check(bounds.Width is >= 172 and <= 176 && bounds.Height is >= 172 and <= 176, "Crop should normalize actual visible content.");
Check(Math.Abs(bounds.Left - (256 - bounds.Width) / 2) <= 1 && Math.Abs(bounds.Top - (256 - bounds.Height) / 2) <= 1, "Content should be centered.");
Check(RedBounds(compactResult.PreviewPng).Width == bounds.Width, "Different source padding should have equal visual span.");
Check(RedBounds(renderer.Render(padded, noPlate with { CropTransparent = false }).PreviewPng).Width < 40, "Disabling crop should retain the complete source canvas.");
using (var bitmap = SKBitmap.Decode(cropped.PreviewPng))
{
    Check(bitmap.Width == 256 && bitmap.Height == 256, "Preview size should be 256 pixels.");
    Check(bitmap.GetPixel(15, 15).Alpha == 0, "No backdrop should retain transparency.");
}
var solid = renderer.Render(padded, new IconStyle { Backdrop = BackdropStyle.Solid, BackgroundColor = "#335577" });
using (var bitmap = SKBitmap.Decode(solid.PreviewPng))
{
    Check(bitmap.GetPixel(128, 28) == new SKColor(0x33, 0x55, 0x77), "Solid plate must preserve requested RGB.");
    Check(bitmap.GetPixel(0, 0).Alpha == 0, "Rounded background should not fill outer corners.");
    Check(bitmap.GetPixel(128, 128).Red > 200, "Backdrop should not cover the foreground.");
}
var gradient = renderer.Render(padded, new IconStyle());
using (var bitmap = SKBitmap.Decode(gradient.PreviewPng))
    Check(bitmap.GetPixel(40, 40) != bitmap.GetPixel(216, 216), "Gradient should vary across the plate.");

var sizes = new[] { 16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 192, 256 };
void VerifyIcon(RenderResult result)
{
    var ico = result.IconFile;
    Check(BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(0)) == 0 && BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(2)) == 1, "ICO header.");
    Check(BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)) == sizes.Length, "ICO frame count.");
    var expectedOffset = 6 + sizes.Length * 16;
    for (var i = 0; i < sizes.Length; i++)
    {
        var entry = ico.AsSpan(6 + i * 16, 16);
        Check((entry[0] == 0 ? 256 : entry[0]) == sizes[i] && entry[0] == entry[1], "ICO width/height directory.");
        Check(BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]) == 1 && BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]) == 32, "ICO planes/bit depth.");
        var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
        var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
        Check(offset == expectedOffset && offset + length <= ico.Length, "ICO contiguous frame offsets.");
        var png = ico.AsSpan(offset, length).ToArray();
        using var bitmap = SKBitmap.Decode(png);
        Check(bitmap is not null && bitmap.Width == sizes[i] && bitmap.Height == sizes[i], "ICO PNG should independently decode.");
        if (i == sizes.Length - 1) Check(png.SequenceEqual(result.PreviewPng), "Preview and largest ICO frame should be identical.");
        expectedOffset += length;
    }
    Check(expectedOffset == ico.Length, "ICO should have no trailing bytes.");
    using var stream = new MemoryStream(ico);
    var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
    Check(decoder.Frames.Count == sizes.Length, "Windows WIC should decode every ICO frame.");
    Check(decoder.Frames.Select(f => f.PixelWidth).Order().SequenceEqual(sizes), "Windows WIC should report the intended frame sizes.");
}
VerifyIcon(gradient);
var thin = Fixture(256, 256, canvas => canvas.DrawRect(new SKRect(127, 0, 128, 256), red));
var thinResult = renderer.Render(thin, noPlate);
VerifyIcon(thinResult);
var thinEntry = thinResult.IconFile.AsSpan(6, 16);
var thinPng = thinResult.IconFile.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(thinEntry[12..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(thinEntry[8..])).ToArray();
using (var thinBitmap = SKBitmap.Decode(thinPng))
{
    double alphaMass = thinBitmap.Pixels.Sum(pixel => (double)pixel.Alpha);
    Check(alphaMass is > 90 and < 150, "Thin marks should retain true subpixel coverage without a forced one-pixel stretch.");
}
var faint = Fixture(16, 16, canvas => canvas.Clear(new SKColor(255, 0, 0, 2)));
Check(renderer.Render(faint, noPlate).PreviewPng.Length > 0, "Faint artwork should render.");
var tiny = Fixture(1, 1, canvas => canvas.Clear(red.Color));
Check(RedBounds(renderer.Render(tiny, noPlate).PreviewPng).Width > 100, "Tiny source should scale without disappearing.");

Reject(() => renderer.Render(null!, noPlate), "Null image must fail.");
Reject(() => renderer.Render(padded, null!), "Null style must fail.");
Reject(() => renderer.Render([], noPlate), "Empty PNG must fail.");
Reject(() => renderer.Render([1, 2, 3], noPlate), "Invalid PNG must fail.");
Reject(() => renderer.Render(padded[..(padded.Length / 2)], noPlate), "Truncated PNG must fail.");
Reject(() => renderer.Render(Fixture(8, 8, _ => { }), noPlate), "Fully transparent PNG must fail.");
Reject(() => renderer.Render(Fixture(4097, 1, canvas => canvas.Clear(red.Color)), noPlate), "Oversized dimensions must fail before bitmap allocation.");
foreach (var value in new[] { double.NaN, double.PositiveInfinity, -1, 0, 1.01 })
    Reject(() => renderer.Render(padded, noPlate with { ContentScale = value }), "Invalid scale must fail.");
foreach (var value in new[] { double.NaN, double.NegativeInfinity, -0.1, 0.51 })
    Reject(() => renderer.Render(padded, noPlate with { CornerRadius = value }), "Invalid radius must fail.");
foreach (var color in new[] { "red", "#GG0011", "#123", "#AA112233", "1234567", null })
    Reject(() => renderer.Render(padded, noPlate with { BackgroundColor = color! }), "Invalid color must fail.");
Reject(() => renderer.Render(padded, noPlate with { Backdrop = (BackdropStyle)9 }), "Invalid enum must fail.");
Parallel.For(0, 12, _ =>
{
    if (!renderer.Render(padded, new IconStyle()).IconFile.SequenceEqual(gradient.IconFile))
        throw new InvalidOperationException("Parallel calls should be deterministic.");
});
checks++;
checks += QualityChecks.Run(renderer, output);
checks += MacMaterialChecks.Run(renderer, output);
checks += ShapeAdaptationChecks.Run(renderer, output);

var fixtures = new Dictionary<string, byte[]>
{
    ["source-padded"] = padded,
    ["gradient"] = gradient.PreviewPng,
    ["solid"] = solid.PreviewPng,
    ["no-backdrop"] = cropped.PreviewPng,
    ["preserve-canvas"] = renderer.Render(padded, new IconStyle { CropTransparent = false }).PreviewPng,
    ["thin"] = thinResult.PreviewPng
};
foreach (var (name, png) in fixtures) File.WriteAllBytes(Path.Combine(output, name + ".png"), png);
File.WriteAllBytes(Path.Combine(output, "gradient.ico"), gradient.IconFile);
using (var sheet = SKSurface.Create(new SKImageInfo(256 * fixtures.Count, 288)))
{
    sheet.Canvas.Clear(new SKColor(232, 235, 244));
    var index = 0;
    foreach (var (_, png) in fixtures)
    {
        using var image = SKImage.FromEncodedData(png);
        var target = new SKRect(index * 256 + 16, 16, (index + 1) * 256 - 16, 240);
        sheet.Canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear));
        index++;
    }
    using var imageSheet = sheet.Snapshot();
    using var encoded = imageSheet.Encode(SKEncodedImageFormat.Png, 100);
    File.WriteAllBytes(Path.Combine(output, "contact-sheet.png"), encoded.ToArray());
}
Console.WriteLine($"PASS: {checks} imaging assertions including Windows WIC ICO decoding and parallel determinism.");
Console.WriteLine($"Samples: {output}");
