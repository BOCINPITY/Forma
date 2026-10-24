using System.Buffers.Binary;
using System.IO;
using System.Windows.Media.Imaging;
using IconAdapt.Core;
using SkiaSharp;

internal static class MacMaterialChecks
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 192, 256];

    internal static int Run(IIconRenderer renderer, string output)
    {
        int checks = 0;
        void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
            checks++;
        }
        var mac = new IconStyle { Backdrop = BackdropStyle.MacOS, ContentScale = .8, CornerRadius = .23 };
        var classic = mac with { Backdrop = BackdropStyle.Gradient, BackgroundColor = "#7772EE" };
        var red = new SKColor(230, 48, 60);
        var blue = new SKColor(38, 113, 196);
        var sources = new Dictionary<string, byte[]>
        {
            ["Transparent mark"] = Fixture(canvas =>
            {
                using var paint = Paint(red);
                canvas.DrawRoundRect(new SKRect(24, 96, 232, 160), 16, 16, paint);
                canvas.DrawRoundRect(new SKRect(96, 24, 160, 232), 16, 16, paint);
                using var white = Paint(SKColors.White);
                canvas.DrawRect(new SKRect(124, 124, 132, 132), white);
            }),
            ["Existing tile"] = Fixture(canvas =>
            {
                using var paint = Paint(blue);
                canvas.DrawRoundRect(new SKRect(12, 12, 244, 244), 48, 48, paint);
                Glyph(canvas, SKColors.White);
            }),
            ["Circular mark"] = Fixture(canvas =>
            {
                using var paint = Paint(new SKColor(35, 168, 119));
                canvas.DrawCircle(128, 128, 112, paint);
                Glyph(canvas, SKColors.White);
            }),
            ["Light tile"] = Fixture(canvas =>
            {
                using var paint = Paint(new SKColor(249, 247, 239));
                canvas.DrawRoundRect(new SKRect(12, 12, 244, 244), 48, 48, paint);
                Glyph(canvas, new SKColor(219, 147, 45));
            }),
            ["Dark tile"] = Fixture(canvas =>
            {
                using var paint = Paint(new SKColor(31, 43, 61));
                canvas.DrawRoundRect(new SKRect(12, 12, 244, 244), 48, 48, paint);
                Glyph(canvas, new SKColor(193, 222, 238));
            }),
            ["Detailed tile"] = Fixture(canvas =>
            {
                using var gradient = SKShader.CreateLinearGradient(new(0, 0), new(256, 256),
                    [new SKColor(129, 68, 181), new SKColor(47, 86, 161)], SKShaderTileMode.Clamp);
                using var paint = new SKPaint { Shader = gradient, IsAntialias = true };
                canvas.DrawRoundRect(new SKRect(12, 12, 244, 244), 48, 48, paint);
                using var line = new SKPaint { Color = new SKColor(255, 255, 255, 150), StrokeWidth = 2, IsAntialias = true };
                for (int x = 60; x <= 196; x += 17) canvas.DrawLine(x, 56, x, 200, line);
                for (int y = 56; y <= 200; y += 18) canvas.DrawLine(60, y, 196, y, line);
                Glyph(canvas, SKColors.White);
            })
        };
        var results = sources.ToDictionary(x => x.Key, x => renderer.Render(x.Value, mac));
        foreach (var (name, result) in results)
        {
            using var stream = new MemoryStream(result.IconFile);
            var wic = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            Check(wic.Frames.Count == Sizes.Length && wic.Frames.Select(x => x.PixelWidth).Order().SequenceEqual(Sizes),
                $"macOS material must retain every WIC-readable native output size: {name}.");
            foreach (int size in Sizes)
            {
                using var bitmap = SKBitmap.Decode(Frame(result.IconFile, size));
                Check(bitmap.Width == size && bitmap.Height == size, $"macOS output dimensions must match ICO directory: {name}/{size}.");
                Check(bitmap.GetPixel(0, 0).Alpha <= 2 && bitmap.GetPixel(size / 2, size / 2).Alpha == 255,
                    $"macOS material must keep transparent outside corners and an opaque center: {name}/{size}.");
            }
        }
        using (var mark = SKBitmap.Decode(results["Transparent mark"].PreviewPng))
        {
            var pale = mark.GetPixel(128, 20);
            Check(pale.Red > 235 && pale.Green > 220 && pale.Blue > 220,
                "Transparent color marks must receive a pale automatic material, not a saturated enclosing frame.");
            Check(mark.GetPixel(128, 150) == red, "The material must not tint the original foreground color.");
            var detail = mark.GetPixel(128, 128);
            Check(detail.Red >= 250 && detail.Green >= 250 && detail.Blue >= 250,
                "Fine central foreground detail must remain intact beneath the material treatment.");
        }
        using (var tile = SKBitmap.Decode(results["Existing tile"].PreviewPng))
        {
            foreach (var point in new[] { new SKPointI(128, 24), new SKPointI(128, 230), new SKPointI(24, 128), new SKPointI(230, 128) })
                Check(Distance(tile.GetPixel(point.X, point.Y), blue) <= 5,
                    "An existing rounded tile must fill the new outline instead of gaining a second colored frame.");
        }
        var diamond = Fixture(canvas =>
        {
            using var builder = new SKPathBuilder();
            builder.MoveTo(128, 12); builder.LineTo(244, 128); builder.LineTo(128, 244); builder.LineTo(12, 128); builder.Close();
            using var path = builder.Detach();
            using var paint = Paint(red);
            canvas.DrawPath(path, paint);
        });
        foreach (var input in new[] { sources["Circular mark"], diamond })
        {
            using var bitmap = SKBitmap.Decode(renderer.Render(input, mac).PreviewPng);
            var edge = bitmap.GetPixel(128, 20);
            Check(edge.Red > 220 && edge.Green > 220 && edge.Blue > 220,
                "Circular and freeform marks must not be misclassified as opaque rectangular tiles.");
        }

        // Alpha at the outside must depend on the final silhouette, not on the
        // foreground's opacity or color: catches double clipping of AA coverage.
        foreach (int size in Sizes)
        {
            using var transparent = SKBitmap.Decode(Frame(results["Transparent mark"].IconFile, size));
            using var opaque = SKBitmap.Decode(Frame(results["Existing tile"].IconFile, size));
            Check(transparent.Pixels.Select(x => x.Alpha).SequenceEqual(opaque.Pixels.Select(x => x.Alpha)),
                $"Outer alpha must be applied once after composition: {size}px.");
            Check(opaque.Pixels.Any(x => x.Alpha is > 0 and < 255), $"The outer silhouette must retain antialiased coverage: {size}px.");
        }
        byte[] Solid(int size, SKColor color) => Fixture(canvas => canvas.Clear(color), size);
        var primary = Solid(256, red);
        var native = renderer.Render(primary, mac, [new(16, 16, Solid(16, SKColors.Blue)), new(32, 32, Solid(32, SKColors.Green))]);
        foreach (var (size, expected) in new[] { (16, SKColors.Blue), (32, SKColors.Green), (40, red), (256, red) })
        {
            using var bitmap = SKBitmap.Decode(Frame(native.IconFile, size));
            Check(bitmap.GetPixel(size / 2, size / 2) == expected,
                $"macOS material must keep native small-frame selection and canonical large artwork: {size}px.");
        }
        foreach (double scale in new[] { .1, 1.0 })
        foreach (double radius in new[] { 0.0, .5 })
        {
            var result = renderer.Render(sources["Transparent mark"], mac with { ContentScale = scale, CornerRadius = radius });
            using var bitmap = SKBitmap.Decode(result.PreviewPng);
            Check(bitmap.GetPixel(128, 128).Alpha == 255, "All legal extreme size/radius settings must render a complete center.");
            Check(radius == 0 ? bitmap.GetPixel(20, 20).Alpha == 255 : bitmap.GetPixel(20, 20).Alpha < 10,
                "Zero radius must remain square and maximum radius round.");
        }
        Parallel.For(0, 8, index =>
        {
            string key = sources.Keys.ElementAt(index % sources.Count);
            if (!renderer.Render(sources[key], mac).IconFile.SequenceEqual(results[key].IconFile))
                throw new InvalidOperationException("macOS material must be deterministic across parallel calls.");
        });
        checks++;
        MakeSheet(sources, results, renderer, classic, Path.Combine(output, "mac-style-comparison.png"));
        File.WriteAllBytes(Path.Combine(output, "mac-material.ico"), results["Transparent mark"].IconFile);
        return checks;
    }

    private static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true };
    private static void Glyph(SKCanvas canvas, SKColor color)
    {
        using var paint = new SKPaint { Color = color, IsAntialias = true, StrokeWidth = 15, StrokeCap = SKStrokeCap.Round };
        canvas.DrawLine(88, 95, 116, 124, paint);
        canvas.DrawLine(116, 124, 88, 153, paint);
        canvas.DrawLine(138, 159, 174, 159, paint);
    }
    private static byte[] Fixture(Action<SKCanvas> draw, int size = 256)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent);
        draw(surface.Canvas);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }
    private static int Distance(SKColor a, SKColor b) => Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));
    private static byte[] Frame(byte[] ico, int size)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + i * 16, 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size) continue;
            return ico.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]),
                (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..])).ToArray();
        }
        throw new InvalidOperationException($"Missing ICO frame: {size}.");
    }
    private static void MakeSheet(Dictionary<string, byte[]> sources, Dictionary<string, RenderResult> results,
        IIconRenderer renderer, IconStyle classic, string output)
    {
        using var surface = SKSurface.Create(new SKImageInfo(1840, 1000));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(241, 243, 247));
        using var typeface = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(typeface, 16);
        using var text = new SKPaint { Color = new SKColor(62, 73, 86), IsAntialias = true };
        canvas.DrawText("Same inputs / same 80% content setting / actual exported pixels", 24, 28, SKTextAlign.Left, font, text);
        canvas.DrawText("Classic", 24, 204, SKTextAlign.Left, font, text);
        canvas.DrawText("macOS", 24, 494, SKTextAlign.Left, font, text);
        int column = 0;
        foreach (var (name, source) in sources)
        {
            int x = 140 + column++ * 280;
            canvas.DrawText(name, x + 8, 66, SKTextAlign.Left, font, text);
            var old = renderer.Render(source, classic);
            using (var image = SKImage.FromEncodedData(old.PreviewPng)) canvas.DrawImage(image, x, 88, new SKSamplingOptions(SKFilterMode.Nearest));
            using (var image = SKImage.FromEncodedData(results[name].PreviewPng)) canvas.DrawImage(image, x, 376, new SKSamplingOptions(SKFilterMode.Nearest));
            foreach (var (size, y) in new[] { (32, 686), (48, 758), (96, 848) })
            {
                using var before = SKImage.FromEncodedData(Frame(old.IconFile, size));
                using var after = SKImage.FromEncodedData(Frame(results[name].IconFile, size));
                canvas.DrawImage(before, x + 20, y, new SKSamplingOptions(SKFilterMode.Nearest));
                canvas.DrawImage(after, x + 148, y, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            canvas.DrawText("Classic", x + 16, 666, SKTextAlign.Left, font, text);
            canvas.DrawText("macOS", x + 144, 666, SKTextAlign.Left, font, text);
        }
        canvas.DrawText("32 px", 24, 708, SKTextAlign.Left, font, text);
        canvas.DrawText("48 px", 24, 788, SKTextAlign.Left, font, text);
        canvas.DrawText("96 px", 24, 900, SKTextAlign.Left, font, text);
        using var sheet = surface.Snapshot();
        using var png = sheet.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(output, png.ToArray());
    }
}
