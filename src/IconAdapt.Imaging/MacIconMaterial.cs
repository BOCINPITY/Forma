using SkiaSharp;
using IconAdapt.Core;

namespace IconAdapt.Imaging;

/// <summary>Opt-in continuous-corner material. Keeps the foreground on the existing native-pixel sampler.</summary>
internal static class MacIconMaterial
{
    internal readonly record struct SourceAppearance(bool HasPlate, SKColor Color);

    internal static SourceAppearance Analyze(SKBitmap bitmap, SKRectI bounds)
    {
        // Broad, opaque straight edges distinguish an existing tile from a circular
        // or freeform mark. This conservative heuristic cannot identify every design.
        int filled = 0;
        var edgeColors = new Dictionary<int, int>();
        var colors = new Dictionary<int, int>();
        for (int y = 0; y < 24; y++)
        for (int x = 0; x < 24; x++)
        {
            int px = bounds.Left + Math.Min(bounds.Width - 1, (int)((x + .5) * bounds.Width / 24));
            int py = bounds.Top + Math.Min(bounds.Height - 1, (int)((y + .5) * bounds.Height / 24));
            var color = bitmap.GetPixel(px, py);
            if (color.Alpha < 224) continue;
            filled++;
            int key = (color.Red >> 4) << 8 | (color.Green >> 4) << 4 | (color.Blue >> 4);
            int high = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
            int low = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
            if (high - low >= 28 && high >= 70)
                colors[key] = colors.GetValueOrDefault(key) + 1;
            if ((x is 2 or 21 && y is >= 5 and <= 18) || (y is 2 or 21 && x is >= 5 and <= 18))
                edgeColors[key] = edgeColors.GetValueOrDefault(key) + 1;
        }
        double aspect = (double)bounds.Width / bounds.Height;
        int edgeCoverage = edgeColors.Values.Sum();
        bool hasPlate = aspect is >= .85 and <= 1.18 && filled >= 500 && edgeCoverage >= 53;
        var histogram = hasPlate ? edgeColors : colors;
        var dominant = histogram.Count == 0 ? new SKColor(234, 239, 246) : Decode(histogram.MaxBy(x => x.Value).Key);
        return new(hasPlate, hasPlate ? dominant : Mix(dominant, SKColors.White, .94f));
    }

    private static SKColor Decode(int value) => new((byte)(((value >> 8) & 15) * 16 + 8),
        (byte)(((value >> 4) & 15) * 16 + 8), (byte)((value & 15) * 16 + 8));

    internal static double ContentScale(IconStyle style, SourceAppearance appearance) =>
        appearance.HasPlate ? Math.Min(.94, style.ContentScale * .89 / .80) : style.ContentScale;

    internal static byte[] Render(PixelResampler source, SourceAppearance appearance, IconStyle style, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        float inset = size * .055f;
        var plate = new SKRect(inset, inset, size - inset, size - inset);
        using var silhouette = ContinuousOutline(plate, style.CornerRadius);
        using (var blur = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * .018f))
        using (var shadow = new SKPaint { Color = new SKColor(16, 28, 44, 30), MaskFilter = blur, IsAntialias = true })
        {
            canvas.Save();
            canvas.Translate(0, size * .018f);
            canvas.DrawPath(silhouette, shadow);
            canvas.Restore();
        }

        using var content = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var baseColor = appearance.Color;
        using (var shader = SKShader.CreateLinearGradient(new(0, 0), new(0, size),
            [Mix(baseColor, SKColors.White, .32f), baseColor, Mix(baseColor, SKColors.Black, .035f)],
            [0, .55f, 1], SKShaderTileMode.Clamp))
        using (var paint = new SKPaint { Shader = shader })
            content.Canvas.DrawPaint(paint);

        // Existing tiles fill the silhouette; transparent marks keep a breathing margin.
        // No sharpening, foreground glow, or secondary bitmap resampling is introduced.
        using var foreground = source.Render(size, ContentScale(style, appearance));
        using var foregroundImage = SKImage.FromBitmap(foreground);
        content.Canvas.DrawImage(foregroundImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));

        if (size >= 32)
        {
            // A narrow inner rim adds material at the outside, away from fine logo detail.
            float line = Math.Max(.45f, size * .003f);
            var rimBounds = new SKRect(plate.Left + line, plate.Top + line, plate.Right - line, plate.Bottom - line);
            using var rim = ContinuousOutline(rimBounds, style.CornerRadius);
            using var light = SKShader.CreateLinearGradient(new(0, plate.Top), new(0, plate.Bottom),
                [new SKColor(255, 255, 255, 95), new SKColor(255, 255, 255, 0), new SKColor(10, 25, 44, 15)],
                [0, .45f, 1], SKShaderTileMode.Clamp);
            using var rimPaint = new SKPaint { Shader = light, Style = SKPaintStyle.Stroke, StrokeWidth = line, IsAntialias = true };
            content.Canvas.DrawPath(rim, rimPaint);
        }

        using var image = content.Snapshot();
        canvas.Save();
        canvas.ClipPath(silhouette, SKClipOperation.Intersect, antialias: true);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        canvas.Restore();
        using var result = surface.Snapshot();
        using var png = result.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static SKPath ContinuousOutline(SKRect bounds, double radius)
    {
        using var path = new SKPathBuilder();
        if (radius < .001) { path.AddRect(bounds); return path.Detach(); }
        // A superellipse gives a continuously varying corner instead of a straight
        // line abruptly meeting a circular arc. 0.5 remains a circle for round styles.
        double exponent = 2 + (0.5 - radius) * 9;
        float cx = bounds.MidX, cy = bounds.MidY, halfWidth = bounds.Width / 2, halfHeight = bounds.Height / 2;
        const int segments = 192;
        for (int i = 0; i < segments; i++)
        {
            double angle = i * Math.PI * 2 / segments;
            double cos = Math.Cos(angle), sin = Math.Sin(angle);
            float x = cx + halfWidth * (float)(Math.Sign(cos) * Math.Pow(Math.Abs(cos), 2 / exponent));
            float y = cy + halfHeight * (float)(Math.Sign(sin) * Math.Pow(Math.Abs(sin), 2 / exponent));
            if (i == 0) path.MoveTo(x, y); else path.LineTo(x, y);
        }
        path.Close();
        return path.Detach();
    }

    private static SKColor Mix(SKColor from, SKColor to, float amount) => new(
        (byte)Math.Round(from.Red + (to.Red - from.Red) * amount),
        (byte)Math.Round(from.Green + (to.Green - from.Green) * amount),
        (byte)Math.Round(from.Blue + (to.Blue - from.Blue) * amount));
}
