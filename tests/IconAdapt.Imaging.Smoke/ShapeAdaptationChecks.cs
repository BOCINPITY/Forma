using System.Buffers.Binary;
using System.IO;
using IconAdapt.Core;
using IconAdapt.Imaging;
using SkiaSharp;

internal static class ShapeAdaptationChecks
{
    private static readonly IconStyle Style = new() { Backdrop = BackdropStyle.MacOS, ContentScale = .8, CornerRadius = .23, CropTransparent = true };
    private static readonly SKColor Ink = new(30, 80, 170);
    internal static Dictionary<string, byte[]> Samples() => new()
    {
        ["Existing plate"] = Draw(c =>
        {
            using var paint = Paint(new SKColor(48, 113, 192));
            c.DrawRoundRect(new SKRect(16, 16, 240, 240), 40, 40, paint);
            using var white = Paint(SKColors.White);
            c.DrawRect(new SKRect(76, 64, 94, 192), white);
            c.DrawRect(new SKRect(94, 64, 178, 82), white);
            c.DrawRect(new SKRect(94, 116, 160, 134), white);
        }),
        ["Circular mark"] = Draw(c =>
        {
            using var paint = Paint(Ink); c.DrawCircle(128, 128, 106, paint);
            using var white = Paint(SKColors.White); c.DrawCircle(128, 128, 28, white);
        }),
        ["Transparent mark"] = Draw(c =>
        {
            using var paint = Paint(Ink);
            c.DrawRoundRect(new SKRect(24, 98, 232, 158), 12, 12, paint);
            c.DrawRoundRect(new SKRect(98, 24, 158, 232), 12, 12, paint);
        }),
        ["Fine ring"] = Draw(c =>
        {
            using var paint = Paint(Ink); paint.Style = SKPaintStyle.Stroke; paint.StrokeWidth = 3;
            c.DrawCircle(128, 128, 104, paint);
        }),
        ["Fine mesh"] = Draw(c =>
        {
            using var paint = Paint(Ink); paint.StrokeWidth = 2;
            for (int p = 24; p <= 232; p += 16) { c.DrawLine(p, 24, p, 232, paint); c.DrawLine(24, p, 232, p, paint); }
        }),
        ["Wide lettering"] = Draw(c =>
        {
            using var paint = Paint(Ink); paint.StrokeWidth = 5;
            for (int x = 22; x < 220; x += 34)
            { c.DrawLine(x, 102, x, 154, paint); c.DrawLine(x, 102, x + 22, 102, paint); c.DrawLine(x, 128, x + 18, 128, paint); }
        }),
        ["Diagonal tips"] = Draw(c =>
        {
            using var paint = Paint(Ink); paint.StrokeWidth = 7; paint.StrokeCap = SKStrokeCap.Round;
            c.DrawLine(26, 26, 230, 230, paint); c.DrawLine(230, 26, 26, 230, paint);
            foreach (var p in new[] { new SKPoint(26, 26), new SKPoint(230, 26), new SKPoint(26, 230), new SKPoint(230, 230) })
                c.DrawCircle(p, 8, paint);
        }),
        ["Translucent mark"] = Draw(c =>
        {
            using var paint = Paint(Ink.WithAlpha(70)); c.DrawCircle(128, 128, 106, paint);
        })
    };

    internal static int Run(IIconRenderer renderer, string output)
    {
        int checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); checks++; }
        var samples = Samples();
        var expected = new[] { MacIconMaterial.ShapeKind.ExistingPlate, MacIconMaterial.ShapeKind.CircularMark,
            MacIconMaterial.ShapeKind.TransparentMark, MacIconMaterial.ShapeKind.FineOrComplex,
            MacIconMaterial.ShapeKind.FineOrComplex, MacIconMaterial.ShapeKind.FineOrComplex,
            MacIconMaterial.ShapeKind.FineOrComplex, MacIconMaterial.ShapeKind.TransparentMark };
        var appearances = samples.ToDictionary(x => x.Key, x => Analyze(x.Value));
        var outputs = samples.ToDictionary(x => x.Key, x => renderer.Render(x.Value, Style));
        var report = new List<string>();
        int index = 0;
        foreach (var (name, source) in samples)
        {
            var appearance = appearances[name];
            report.Add($"{name}: {appearance.Shape}; coverage={appearance.Coverage:F4}; effective span={MacIconMaterial.ContentScale(Style, appearance):F4}");
            Check(appearance.Shape == expected[index++], $"Conservative silhouette classification: {name} was {appearance.Shape}.");
            foreach (int size in new[] { 16, 32, 48, 96, 256 })
            {
                using var bitmap = SKBitmap.Decode(Frame(outputs[name].IconFile, size));
                Check(bitmap.Width == size && bitmap.Height == size && bitmap.GetPixel(0, 0).Alpha <= 2,
                    $"Shape adaptation retains output dimensions and clean outside corners: {name}/{size}.");
            }
            Check(renderer.Render(source, Style).IconFile.SequenceEqual(outputs[name].IconFile), $"Shape analysis is deterministic: {name}.");
        }
        File.WriteAllLines(Path.Combine(output, "shape-analysis.txt"), report);
        Check(MacIconMaterial.ContentScale(Style, appearances["Circular mark"]) > .82,
            "Circular marks receive bounded optical size compensation rather than a generic small frame.");
        Check(MacIconMaterial.ContentScale(Style, appearances["Fine ring"]) > .83,
            "Fine circular lines use more available pixels without thickening their strokes.");
        Check(MacIconMaterial.ContentScale(Style, appearances["Diagonal tips"]) < .8,
            "Detached diagonal tips receive a safe margin instead of being clipped by the outer material.");
        Check(!outputs["Circular mark"].Notes.Any(x => x.Contains("形态")) && outputs["Diagonal tips"].Notes.Any(x => x.Contains("安全区")),
            "Normal classification is not a warning; an actual geometry limit is explained.");

        // A shape-aware placement must retain all fully opaque source features, including corner
        // tips. Compare actual foreground pixels to the material result, not the classifier score.
        foreach (string name in new[] { "Diagonal tips", "Fine mesh", "Fine ring" })
        {
            using var input = DecodeRgba(samples[name]);
            var bounds = Visible(input);
            var sampler = new PixelResampler(input, bounds);
            foreach (double radius in new[] { .23, .5 })
            {
                var style = Style with { CornerRadius = radius };
                using var expectedForeground = sampler.Render(256, MacIconMaterial.ContentScale(style, appearances[name]));
                using var actual = SKBitmap.Decode(renderer.Render(samples[name], style).PreviewPng);
                int tested = 0;
                bool preserved = true;
                string? mismatch = null;
                for (int y = 0; y < 256; y++)
                for (int x = 0; x < 256; x++)
                {
                    var pixel = expectedForeground.GetPixel(x, y);
                    if (pixel.Alpha != 255) continue;
                    tested++;
                    var result = actual.GetPixel(x, y);
                    if (result.Alpha != 255 || Distance(result, pixel) > 4)
                    { preserved = false; mismatch ??= $" at {x},{y}: expected {pixel}, actual {result}"; }
                }
                Check(preserved, $"Full-coverage line/tip pixels must survive composition unchanged: {name}/{radius}{mismatch}.");
                Check(tested > 10, $"Corner-preservation fixture must include opaque detail: {name}.");
            }
        }

        var translucentPlate = Draw(c => c.Clear(Ink.WithAlpha(180)));
        Check(Analyze(translucentPlate).Shape == MacIconMaterial.ShapeKind.TransparentMark,
            "An uncertain translucent square must not be asserted to have an opaque plate.");
        var complexPlate = Draw(c =>
        {
            c.Clear(Ink);
            using var line = Paint(SKColors.White); line.StrokeWidth = 1;
            for (int p = 18; p < 238; p += 8) { c.DrawLine(p, 20, p, 236, line); c.DrawLine(20, p, 236, p, line); }
        });
        Check(Analyze(complexPlate).Shape == MacIconMaterial.ShapeKind.ExistingPlate,
            "A detailed existing plate retains plate treatment; internal texture does not trigger extra enlargement.");
        var padded = Draw(c => { using var ink = Paint(Ink); c.DrawCircle(180, 128, 36, ink); });
        using (var bitmap = SKBitmap.Decode(padded))
        {
            var appearance = MacIconMaterial.Analyze(bitmap, Visible(bitmap), new SKRectI(0, 0, 256, 256));
            Check(Math.Abs(MacIconMaterial.ContentScale(Style with { CropTransparent = false }, appearance) - .8) < .0001,
                "Disabling crop preserves intentional canvas padding and does not optically enlarge its mark.");
        }

        // The native frame is a different, deliberately simplified design with diagonal tips.
        // Selecting it and then using only the round primary's envelope would cut those tips off.
        var nativeTips = Draw(c =>
        {
            using var paint = Paint(new SKColor(225, 40, 55)); paint.StrokeWidth = 3;
            c.DrawLine(1, 1, 31, 31, paint); c.DrawLine(31, 1, 1, 31, paint);
        }, 32);
        var nativeAppearance = Analyze(nativeTips);
        var primaryAppearance = appearances["Circular mark"];
        double nativeScale = MacIconMaterial.ContentScale(Style, primaryAppearance, nativeAppearance);
        Check(nativeScale < MacIconMaterial.ContentScale(Style, primaryAppearance),
            "Native frame safety limits are included even when its contour differs from the canonical source.");
        var nativeResult = renderer.Render(samples["Circular mark"], Style, [new IconFrame(32, 32, nativeTips)]);
        using (var nativeBitmap = DecodeRgba(nativeTips))
        using (var expectedNative = SKBitmap.Decode(MacIconMaterial.Render(new PixelResampler(nativeBitmap, Visible(nativeBitmap)), primaryAppearance, Style, 40, nativeScale)))
        using (var actual = SKBitmap.Decode(Frame(nativeResult.IconFile, 40)))
        {
            var actualPixels = actual.Pixels;
            var expectedPixels = expectedNative.Pixels;
            int difference = Enumerable.Range(0, actualPixels.Length).FirstOrDefault(i => actualPixels[i] != expectedPixels[i]);
            Check(actualPixels.SequenceEqual(expectedPixels),
                $"Frame selection and rendering share the native-safe effective span (40px output uses the available 32px tips). Native bounds {Visible(nativeBitmap)}, scale {nativeScale}; diff {difference}: actual {actualPixels[difference]}, expected {expectedPixels[difference]}.");
            Check(actualPixels.Any(p => p.Red > 160 && p.Green < 110), "The chosen small frame's distinct red artwork is retained.");
        }
        foreach (var mode in new[] { BackdropStyle.Gradient, BackdropStyle.Solid, BackdropStyle.None })
        {
            var originalStyle = Style with { Backdrop = mode };
            var before = renderer.Render(samples["Fine mesh"], originalStyle).IconFile;
            renderer.Render(samples["Fine mesh"], Style);
            Check(before.SequenceEqual(renderer.Render(samples["Fine mesh"], originalStyle).IconFile),
                $"Opt-in shape analysis does not leak into the legacy {mode} renderer.");
        }
        MakeSheet(samples, outputs, output);
        return checks;
    }

    private static MacIconMaterial.SourceAppearance Analyze(byte[] png)
    { using var bitmap = SKBitmap.Decode(png); return MacIconMaterial.Analyze(bitmap, Visible(bitmap)); }
    private static SKBitmap DecodeRgba(byte[] png)
    { using var bitmap = SKBitmap.Decode(png); return bitmap.Copy(SKColorType.Rgba8888); }
    private static SKRectI Visible(SKBitmap bitmap)
    {
        var pixels = bitmap.Pixels;
        int left = bitmap.Width, top = bitmap.Height, right = 0, bottom = 0;
        for (int y = 0; y < bitmap.Height; y++)
        for (int x = 0; x < bitmap.Width; x++)
            if (pixels[y * bitmap.Width + x].Alpha > 0)
            { left = Math.Min(left, x); top = Math.Min(top, y); right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1); }
        return new(left, top, right, bottom);
    }
    private static int Distance(SKColor a, SKColor b) => Math.Max(Math.Abs(a.Red - b.Red), Math.Max(Math.Abs(a.Green - b.Green), Math.Abs(a.Blue - b.Blue)));

    private static void MakeSheet(Dictionary<string, byte[]> samples, Dictionary<string, RenderResult> results, string output)
    {
        string baseline = Path.Combine(output, "shape-baseline");
        bool hasBaseline = samples.Keys.All(name => File.Exists(Path.Combine(baseline, name + ".ico")));
        using var surface = SKSurface.Create(new SKImageInfo(1940, hasBaseline ? 760 : 470));
        var canvas = surface.Canvas; canvas.Clear(new SKColor(238, 240, 244));
        using var typeface = SKTypeface.FromFamilyName("Segoe UI");
        using var font = new SKFont(typeface, 14);
        using var text = Paint(new SKColor(50, 59, 72));
        canvas.DrawText("Same inputs / 80% content / 23% corners / actual exported pixels (no preview rescaling)", 24, 30, SKTextAlign.Left, font, text);
        canvas.DrawText("Source", 24, 124, SKTextAlign.Left, font, text);
        canvas.DrawText("Shape-aware 96px", 24, 264, SKTextAlign.Left, font, text);
        canvas.DrawText("Shape-aware 32 / 48px", 24, 384, SKTextAlign.Left, font, text);
        if (hasBaseline)
        {
            canvas.DrawText("preview.8 96px", 24, 530, SKTextAlign.Left, font, text);
            canvas.DrawText("preview.8 32 / 48px", 24, 650, SKTextAlign.Left, font, text);
        }
        int column = 0;
        foreach (var (name, source) in samples)
        {
            int x = 210 + column++ * 212;
            canvas.DrawText(name, x, 65, SKTextAlign.Left, font, text);
            using (var image = SKImage.FromEncodedData(source)) canvas.DrawImage(image, new SKRect(x, 80, x + 96, 176), new SKSamplingOptions(SKFilterMode.Linear));
            void DrawFrame(byte[] ico, int size, int dx, int y)
            { using var image = SKImage.FromEncodedData(Frame(ico, size)); canvas.DrawImage(image, x + dx, y, new SKSamplingOptions(SKFilterMode.Nearest)); }
            DrawFrame(results[name].IconFile, 96, 0, 206);
            DrawFrame(results[name].IconFile, 32, 0, 348); DrawFrame(results[name].IconFile, 48, 65, 340);
            if (hasBaseline)
            {
                var old = File.ReadAllBytes(Path.Combine(baseline, name + ".ico"));
                DrawFrame(old, 96, 0, 472); DrawFrame(old, 32, 0, 614); DrawFrame(old, 48, 65, 606);
            }
        }
        canvas.DrawText("Source row is a 96px thumbnail. Output rows are native ICO frames. Thin details still have physical pixel limits.", 24, hasBaseline ? 724 : 434, SKTextAlign.Left, font, text);
        using var imageSheet = surface.Snapshot(); using var data = imageSheet.Encode(SKEncodedImageFormat.Png, 100);
        File.WriteAllBytes(Path.Combine(output, "shape-adaptation-comparison.png"), data.ToArray());
    }

    private static SKPaint Paint(SKColor color) => new() { Color = color, IsAntialias = true };
    private static byte[] Draw(Action<SKCanvas> action, int size = 256)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        surface.Canvas.Clear(SKColors.Transparent); action(surface.Canvas);
        using var image = surface.Snapshot(); using var data = image.Encode(SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    private static byte[] Frame(byte[] ico, int size)
    {
        for (int i = 0; i < BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4)); i++)
        {
            var entry = ico.AsSpan(6 + i * 16, 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size) continue;
            return ico.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..])).ToArray();
        }
        throw new InvalidOperationException("Missing ICO size.");
    }
}
