using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using IconAdapt.Core;
using SkiaSharp;

internal static class QualityChecks
{
    internal static int Run(IIconRenderer renderer, string output)
    {
        int checks = 0;
        void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks++;
        }
        var style = new IconStyle { Backdrop = BackdropStyle.None, CropTransparent = false, ContentScale = 1 };
        var sourceStripes = Raster(240, 240, (x, _) => x % 5 < 2 ? SKColors.White : SKColors.Black);
        var stripeFrame = Frame(renderer.Render(sourceStripes, style).IconFile, 16);
        var stripeBefore = LegacyFrame(sourceStripes, 16);
        // Every destination pixel covers exactly three complete five-pixel periods:
        // two white + three black pixels per period, so the analytic mean is 102.
        double stripeError = ColorMse(stripeFrame, (_, _) => new SKColor(102, 102, 102));
        double stripeErrorBefore = ColorMse(stripeBefore, (_, _) => new SKColor(102, 102, 102));
        Check(stripeError <= 1, "Strong reduction must integrate the full footprint rather than alias a high-frequency pattern.");
        Check(stripeErrorBefore > 1000 && stripeError < stripeErrorBefore * .01, "Reduction should remove the known fixed-kernel aliasing regression.");

        var sourceDiagonal = Raster(240, 240, (x, y) => x == y ? SKColors.Red : SKColors.Transparent);
        var diagonalFrame = Frame(renderer.Render(sourceDiagonal, style).IconFile, 16);
        var diagonalBefore = LegacyFrame(sourceDiagonal, 16);
        // 240 source pixels at alpha 255, with an isotropic 1/15 scale in both axes.
        const double expectedDiagonalMass = 240 * 255.0 / (15 * 15);
        double diagonalError = Math.Abs(AlphaMass(diagonalFrame) - expectedDiagonalMass);
        double diagonalErrorBefore = Math.Abs(AlphaMass(diagonalBefore) - expectedDiagonalMass);
        Check(diagonalError <= 16, "A diagonal hairline should conserve opacity coverage when reduced.");
        Check(diagonalError < diagonalErrorBefore * .1, "Diagonal coverage error should be substantially lower than fixed cubic sampling.");
        using (var diagonal = SKBitmap.Decode(diagonalFrame))
        {
            Check(Enumerable.Range(0, 16).All(i => diagonal.GetPixel(i, i).Alpha is >= 16 and <= 18), "The diagonal should remain continuous without broken or inflated stroke segments.");
            Check(Enumerable.Range(0, 16).All(y => Enumerable.Range(0, 16).All(x => x == y || diagonal.GetPixel(x, y).Alpha == 0)), "Reduction must not produce ringing halos beside the diagonal.");
        }

        string[] glyph = ["011100100", "100010100", "100010100", "111110100", "100010100", "100010100", "100010111"];
        var sourceText = Raster(24, 24, (x, y) => y >= 8 && y < 15 && x >= 7 && x < 16 && glyph[y - 8][x - 7] == '1' ? SKColors.Black : SKColors.White);
        var textFrame = Frame(renderer.Render(sourceText, style).IconFile, 24);
        var textBefore = LegacyFrame(sourceText, 24);
        using (var source = SKBitmap.Decode(sourceText))
        using (var actual = SKBitmap.Decode(textFrame))
            Check(source.Pixels.SequenceEqual(actual.Pixels), "Native-size pixel lettering must remain pixel exact, with no unconditional cubic blur.");
        double textError = DifferenceMse(sourceText, textFrame);
        double textErrorBefore = DifferenceMse(sourceText, textBefore);
        Check(textError == 0 && textErrorBefore > 20, "Native-size fine text should improve over the smoothing cubic kernel.");

        // An antialiased red fringe is below the old alpha>=8 crop threshold. It must
        // survive alongside the opaque blue artwork instead of being cut away.
        var sourceFringe = Raster(16, 8, (x, _) => x == 0 ? new SKColor(255, 0, 0, 4) : x >= 8 ? SKColors.Blue : SKColors.Transparent);
        var fringeFrame = Frame(renderer.Render(sourceFringe, style with { CropTransparent = true }).IconFile, 16);
        using (var fringe = SKBitmap.Decode(fringeFrame))
            Check(fringe.GetPixel(0, 4).Alpha == 4 && fringe.GetPixel(0, 4).Red > 240, "Crop must preserve faint antialiased edge pixels without hard alpha truncation.");

        var sourceAlphaEdge = Raster(240, 240, (x, _) => x % 5 < 2 ? SKColors.Red : SKColors.Transparent);
        var alphaEdge = Frame(renderer.Render(sourceAlphaEdge, style).IconFile, 16);
        using (var image = SKBitmap.Decode(alphaEdge))
            Check(image.Pixels.All(p => p.Alpha == 102 && p.Red == 255 && p.Green == 0 && p.Blue == 0), "Premultiplied-alpha filtering must retain saturated edge color without dark or colored fringes.");

        Check(renderer.Render(sourceText, style).Notes.Count > 0, "Low-resolution sources should explain enlargement limits.");
        Check(renderer.Render(sourceStripes, style with { ContentScale = .5 }).Notes.Count > 0, "Small content scale should explain reduced legibility.");

        var nativePrimary = Raster(256, 256, (_, _) => SKColors.Red);
        var native16 = Raster(16, 16, (_, _) => SKColors.Blue);
        var native32 = Raster(32, 32, (_, _) => SKColors.Green);
        var nativeResult = renderer.Render(nativePrimary, style, [new(16, 16, native16), new(32, 32, native32)]);
        using (var image = SKBitmap.Decode(Frame(nativeResult.IconFile, 16)))
            Check(image.GetPixel(8, 8) == SKColors.Blue, "The native 16-pixel design must supply the matching small frame.");
        using (var image = SKBitmap.Decode(Frame(nativeResult.IconFile, 20)))
            Check(image.GetPixel(10, 10) == SKColors.Green, "A 20-pixel output must use native 32 rather than enlarge native 16.");
        using (var image = SKBitmap.Decode(Frame(nativeResult.IconFile, 40)))
            Check(image.GetPixel(20, 20) == SKColors.Red, "Unavailable larger native sizes must fall back to the primary source.");
        Check(nativeResult.PreviewPng.SequenceEqual(renderer.Render(nativePrimary, style).PreviewPng), "Native small-frame selection must not replace the canonical 256-pixel preview.");
        var duplicatePrimary = renderer.Render(nativePrimary, style, [new(256, 256, nativePrimary)]);
        Check(duplicatePrimary.Notes.Count == 0 && duplicatePrimary.IconFile.SequenceEqual(renderer.Render(nativePrimary, style).IconFile), "The shared primary native buffer must not be decoded or charged to the budget twice.");
        var paddedNative = Raster(32, 32, (x, y) => x is >= 12 and < 20 && y is >= 12 and < 20 ? SKColors.Blue : SKColors.Transparent);
        var visibleSpanResult = renderer.Render(nativePrimary, style with { CropTransparent = true }, [new(32, 32, paddedNative)]);
        using (var image = SKBitmap.Decode(Frame(visibleSpanResult.IconFile, 16)))
            Check(image.GetPixel(8, 8) == SKColors.Red, "Selection must check visible native span; a padded 32-pixel canvas with an 8-pixel mark must not be enlarged to 16.");
        var tinyPrimary = Raster(4, 4, (_, _) => SKColors.Red);
        var tinyNative = Raster(8, 8, (_, _) => SKColors.Blue);
        using (var image = SKBitmap.Decode(Frame(renderer.Render(tinyPrimary, style, [new(8, 8, tinyNative)]).IconFile, 16)))
            Check(image.GetPixel(8, 8) == SKColors.Blue, "When all sources are smaller than the target, use the largest available source.");
        var malformedNative = renderer.Render(nativePrimary, style,
            [new(32, 32, native16), new(4097, 1, native16), new(16, 16, [1, 2, 3]), null!, new(16, 16, native16)]);
        Check(malformedNative.Notes.Count > 0, "Invalid native resources must be skipped with a useful note.");
        using (var image = SKBitmap.Decode(Frame(malformedNative.IconFile, 16)))
            Check(image.GetPixel(8, 8) == SKColors.Blue, "A malformed native resource must not prevent a later usable native frame from rendering.");
        var overCount = renderer.Render(nativePrimary, style, Enumerable.Repeat(new IconFrame(16, 16, native16), 65).ToArray());
        Check(overCount.Notes.Count > 0, "Native resources must obey the 64-entry budget.");
        var overByteBudget = renderer.Render(nativePrimary, style, [new(1, 1, new byte[32 * 1024 * 1024])]);
        Check(overByteBudget.Notes.Count > 0, "Combined encoded inputs must obey the 32 MiB budget.");
        var overPixelBudget = renderer.Render(nativePrimary, style, [new(4096, 4096, native16)]);
        Check(overPixelBudget.Notes.Count > 0, "Combined decoded source pixels must obey the memory budget before allocation.");

        var opaqueWhiteSquare = Raster(256, 256, (_, _) => SKColors.White);
        var circleStyle = new IconStyle { Backdrop = BackdropStyle.Solid, BackgroundColor = "#FFFFFF", CornerRadius = .5, ContentScale = .1 };
        var referenceCircle = renderer.Render(opaqueWhiteSquare, circleStyle);
        foreach (double scale in new[] { .8, .9 })
        {
            var circle = renderer.Render(opaqueWhiteSquare, circleStyle with { ContentScale = scale });
            foreach (int size in new[] { 32, 48, 96, 256 })
            {
                using var reference = SKBitmap.Decode(Frame(referenceCircle.IconFile, size));
                using var actual = SKBitmap.Decode(Frame(circle.IconFile, size));
                Check(reference.Pixels.SequenceEqual(actual.Pixels), "An opaque square over an identically colored plate must preserve exactly one AA boundary, without corners leaking past the circle or multiplied edge coverage.");
            }
            Check(circle.Notes.Any(note => note.Contains("裁切")), "High content scale should explain the plate-boundary clipping.");
        }
        using (var unclipped = SKBitmap.Decode(renderer.Render(opaqueWhiteSquare, style with { ContentScale = .9 }).PreviewPng))
        using (var clipped = SKBitmap.Decode(renderer.Render(opaqueWhiteSquare, circleStyle with { ContentScale = .9 }).PreviewPng))
            Check(unclipped.GetPixel(20, 20).Alpha == 255 && clipped.GetPixel(20, 20).Alpha < 2, "None mode must preserve square corners while a circular plate clips them, leaving only the independent shadow.");

        var measurements = new
        {
            StripeMeanSquaredErrorBefore = stripeErrorBefore,
            StripeMeanSquaredErrorAfter = stripeError,
            DiagonalExpectedAlphaMass = expectedDiagonalMass,
            DiagonalAlphaMassBefore = AlphaMass(diagonalBefore),
            DiagonalAlphaMassAfter = AlphaMass(diagonalFrame),
            NativeTextMeanSquaredErrorBefore = textErrorBefore,
            NativeTextMeanSquaredErrorAfter = textError
        };
        File.WriteAllText(Path.Combine(output, "quality-metrics.json"), JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
        var files = new Dictionary<string, byte[]>
        {
            ["stripes-source"] = sourceStripes, ["stripes-before-16"] = stripeBefore, ["stripes-after-16"] = stripeFrame,
            ["diagonal-source"] = sourceDiagonal, ["diagonal-before-16"] = diagonalBefore, ["diagonal-after-16"] = diagonalFrame,
            ["text-source"] = sourceText, ["text-before-24"] = textBefore, ["text-after-24"] = textFrame,
            ["fringe-source"] = sourceFringe, ["fringe-after-16"] = fringeFrame, ["alpha-edge-after-16"] = alphaEdge
        };
        foreach (var (name, png) in files) File.WriteAllBytes(Path.Combine(output, name + ".png"), png);
        using (var surface = SKSurface.Create(new SKImageInfo(640, 3 * 192)))
        {
            surface.Canvas.Clear(new SKColor(225, 228, 236));
            var rows = new[] { new[] { stripeBefore, stripeFrame }, new[] { diagonalBefore, diagonalFrame }, new[] { textBefore, textFrame } };
            for (int row = 0; row < rows.Length; row++)
            for (int column = 0; column < 2; column++)
            {
                using var image = SKImage.FromEncodedData(rows[row][column]);
                surface.Canvas.DrawImage(image, new SKRect(column * 320 + 16, row * 192 + 16, column * 320 + 176, row * 192 + 176), new SKSamplingOptions(SKFilterMode.Nearest));
                surface.Canvas.DrawImage(image, column * 320 + 208, row * 192 + 88, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            using var sheet = surface.Snapshot();
            using var data = sheet.Encode(SKEncodedImageFormat.Png, 100);
            File.WriteAllBytes(Path.Combine(output, "quality-comparison.png"), data.ToArray());
        }
        Console.WriteLine("Quality metrics: " + JsonSerializer.Serialize(measurements));
        return checks;
    }

    private static byte[] Raster(int width, int height, Func<int, int, SKColor> pixel)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Pixels = Enumerable.Range(0, width * height).Select(i => pixel(i % width, i / width)).ToArray();
        using var image = SKImage.FromBitmap(bitmap);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static byte[] Frame(byte[] ico, int size)
    {
        int count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (int i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + 16 * i, 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size) continue;
            int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
            int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]));
            return ico.AsSpan(offset, length).ToArray();
        }
        throw new InvalidOperationException($"Missing native {size} frame.");
    }

    // Baseline reproduces the previous full-canvas, fixed Mitchell sampler; it is
    // intentionally confined to these analytical regressions, never used by the product.
    private static byte[] LegacyFrame(byte[] source, int size)
    {
        using var input = SKImage.FromEncodedData(source);
        using var surface = SKSurface.Create(new SKImageInfo(size, size));
        surface.Canvas.Clear(SKColors.Transparent);
        using var paint = new SKPaint { IsAntialias = true };
        surface.Canvas.DrawImage(input, new SKRect(0, 0, input.Width, input.Height), new SKRect(0, 0, size, size), new SKSamplingOptions(SKCubicResampler.Mitchell), paint);
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static double AlphaMass(byte[] png)
    {
        using var image = SKBitmap.Decode(png);
        return image.Pixels.Sum(pixel => (double)pixel.Alpha);
    }

    private static double ColorMse(byte[] png, Func<int, int, SKColor> reference)
    {
        using var actual = SKBitmap.Decode(png);
        double error = 0;
        for (int y = 0; y < actual.Height; y++)
        for (int x = 0; x < actual.Width; x++)
        {
            var a = actual.GetPixel(x, y);
            var b = reference(x, y);
            error += Math.Pow(a.Red - b.Red, 2) + Math.Pow(a.Green - b.Green, 2) + Math.Pow(a.Blue - b.Blue, 2);
        }
        return error / (actual.Width * actual.Height * 3);
    }

    private static double DifferenceMse(byte[] reference, byte[] actual)
    {
        using var image = SKBitmap.Decode(reference);
        return ColorMse(actual, image.GetPixel);
    }
}
