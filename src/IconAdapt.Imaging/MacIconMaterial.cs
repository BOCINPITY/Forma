using SkiaSharp;
using IconAdapt.Core;

namespace IconAdapt.Imaging;

/// <summary>Opt-in continuous-corner material. Keeps the foreground on the existing native-pixel sampler.</summary>
internal static class MacIconMaterial
{
    internal enum ShapeKind { ExistingPlate, CircularMark, TransparentMark, FineOrComplex }
    internal readonly record struct SourceAppearance(ShapeKind Shape, SKColor Color, double Coverage, IReadOnlyList<SKPoint> Envelope)
    {
        internal bool HasPlate => Shape == ShapeKind.ExistingPlate;
    }

    internal static SourceAppearance Analyze(SKBitmap bitmap, SKRectI bounds, SKRectI? layoutBounds = null)
    {
        // Area coverage, not point samples: a one-pixel stroke must not disappear from
        // classification merely because it happens to fall between grid sample centers.
        const int grid = 32;
        var pixels = bitmap.Pixels;
        var alpha = new double[grid, grid];
        var envelope = new List<SKPoint>();
        var layout = layoutBounds ?? bounds;
        double span = Math.Max(layout.Width, layout.Height);
        double centerX = (layout.Left + layout.Right) / 2d, centerY = (layout.Top + layout.Bottom) / 2d;
        int filled = 0;
        double alphaMass = 0, rawAlpha = 0, occupiedPixels = 0, circleDifference = 0;
        var edgeColors = new Dictionary<int, int>();
        var colors = new Dictionary<int, int>();
        for (int y = 0; y < grid; y++)
        for (int x = 0; x < grid; x++)
        {
            int left = bounds.Left + x * bounds.Width / grid, top = bounds.Top + y * bounds.Height / grid;
            int right = Math.Min(bounds.Right, Math.Max(left + 1, bounds.Left + (x + 1) * bounds.Width / grid));
            int bottom = Math.Min(bounds.Bottom, Math.Max(top + 1, bounds.Top + (y + 1) * bounds.Height / grid));
            double mass = 0, r = 0, g = 0, b = 0;
            bool occupied = false;
            for (int py = top; py < bottom; py++)
            for (int px = left; px < right; px++)
            {
                var pixel = pixels[py * bitmap.Width + px];
                double coverage = pixel.Alpha / 255d;
                mass += coverage; r += pixel.Red * coverage; g += pixel.Green * coverage; b += pixel.Blue * coverage;
                rawAlpha += coverage;
                if (pixel.Alpha > 0) { occupiedPixels++; occupied = true; }
            }
            double value = mass / ((right - left) * (bottom - top));
            alpha[x, y] = value; alphaMass += value;
            double nx = (x + .5) * 2 / grid - 1, ny = (y + .5) * 2 / grid - 1;
            circleDifference += Math.Abs(value - (nx * nx + ny * ny <= 1 ? 1 : 0));
            if (occupied)
                envelope.Add(new((float)(2 * Math.Max(Math.Abs(left - centerX), Math.Abs(right - centerX)) / span),
                    (float)(2 * Math.Max(Math.Abs(top - centerY), Math.Abs(bottom - centerY)) / span)));
            if (value < .88) continue;
            filled++;
            var color = new SKColor((byte)Math.Round(r / mass), (byte)Math.Round(g / mass), (byte)Math.Round(b / mass));
            int key = (color.Red >> 4) << 8 | (color.Green >> 4) << 4 | (color.Blue >> 4);
            int high = Math.Max(color.Red, Math.Max(color.Green, color.Blue));
            int low = Math.Min(color.Red, Math.Min(color.Green, color.Blue));
            if (high - low >= 28 && high >= 70)
                colors[key] = colors.GetValueOrDefault(key) + 1;
            if ((x is 3 or 28 && y is >= 6 and <= 25) || (y is 3 or 28 && x is >= 6 and <= 25))
                edgeColors[key] = edgeColors.GetValueOrDefault(key) + 1;
        }
        double aspect = (double)bounds.Width / bounds.Height;
        double coverageFraction = alphaMass / (grid * grid);
        double sourceOpacity = rawAlpha / Math.Max(1, occupiedPixels);
        int edgeCoverage = edgeColors.Values.Sum();
        bool enoughShape = Math.Min(bounds.Width, bounds.Height) >= 8;
        bool hasPlate = enoughShape && aspect is >= .85 and <= 1.18 && filled >= 891 && edgeCoverage >= 76 && sourceOpacity >= .95;
        bool circular = enoughShape && aspect is >= .94 and <= 1.06 && filled >= 706 &&
            circleDifference / (grid * grid) < .055 && sourceOpacity >= .93;
        double variation = 0;
        for (int y = 0; y < grid; y++)
        for (int x = 0; x < grid; x++)
        {
            if (x > 0) variation += Math.Abs(alpha[x, y] - alpha[x - 1, y]);
            if (y > 0) variation += Math.Abs(alpha[x, y] - alpha[x, y - 1]);
        }
        bool fine = sourceOpacity >= .6 && (aspect < .38 || aspect > 2.65 || coverageFraction < .28 || variation / Math.Max(1, alphaMass) > .5);
        var shape = hasPlate ? ShapeKind.ExistingPlate : circular ? ShapeKind.CircularMark :
            fine ? ShapeKind.FineOrComplex : ShapeKind.TransparentMark;
        var histogram = hasPlate ? edgeColors : colors;
        var dominant = histogram.Count == 0 ? new SKColor(234, 239, 246) : Decode(histogram.MaxBy(x => x.Value).Key);
        return new(shape, hasPlate ? dominant : Mix(dominant, SKColors.White, .94f), coverageFraction, envelope);
    }

    private static SKColor Decode(int value) => new((byte)(((value >> 8) & 15) * 16 + 8),
        (byte)(((value >> 4) & 15) * 16 + 8), (byte)((value & 15) * 16 + 8));

    internal static double ContentScale(IconStyle style, SourceAppearance appearance, SourceAppearance? nativeAppearance = null)
    {
        // Preserve a user's deliberate canvas padding. Optical compensation is bounded and
        // never stretches a thin axis independently, sharpens strokes, or invents missing pixels.
        double optical = !style.CropTransparent ? 1 : appearance.Shape switch
        {
            ShapeKind.ExistingPlate => .89 / .80,
            ShapeKind.CircularMark => 1.055,
            ShapeKind.FineOrComplex => 1.075,
            _ => Math.Clamp(Math.Sqrt(.54 / Math.Max(.01, appearance.Coverage)), 1, 1.04)
        };
        double desired = Math.Min(.94, style.ContentScale * optical);
        double canonicalScale = appearance.HasPlate ? desired : Math.Min(desired, SafeContentScale(style, appearance));
        // Native small frames can deliberately redraw the mark. Keep the canonical material
        // and optical intent, but protect any additional tips present in the selected frame.
        return nativeAppearance is { } native && !native.HasPlate
            ? Math.Min(canonicalScale, SafeContentScale(style, native)) : canonicalScale;
    }

    internal static double SafeContentScale(IconStyle style, SourceAppearance appearance)
    {
        double exponent = 2 + (.5 - style.CornerRadius) * 9;
        double extent = appearance.Envelope.Max(point => style.CornerRadius < .001 ? Math.Max(point.X, point.Y) :
            Math.Pow(Math.Pow(point.X, exponent) + Math.Pow(point.Y, exponent), 1 / exponent));
        // Leave a narrow safety margin inside the final silhouette. Every nonzero-alpha
        // source cell participates, so detached tips and translucent fringes are not discarded.
        return .878 / Math.Max(.001, extent);
    }

    internal static byte[] Render(PixelResampler source, SourceAppearance appearance, IconStyle style, int size, double contentScale)
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
        using var foreground = source.Render(size, contentScale);
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
