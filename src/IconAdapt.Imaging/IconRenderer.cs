using System.Globalization;
using IconAdapt.Core;
using SkiaSharp;

namespace IconAdapt.Imaging;

/// <summary>Stateless CPU renderer. Each invocation owns and disposes its native resources.</summary>
public sealed class IconRenderer : IIconRenderer
{
    private static readonly int[] IconSizes = [16, 20, 24, 32, 40, 48, 64, 80, 96, 128, 192, 256];
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public RenderResult Render(byte[] sourcePng, IconStyle style) => Render(sourcePng, style, []);

    public RenderResult Render(byte[] sourcePng, IconStyle style, IReadOnlyList<IconFrame> nativeFrames)
    {
        ArgumentNullException.ThrowIfNull(sourcePng);
        ArgumentNullException.ThrowIfNull(style);
        ArgumentNullException.ThrowIfNull(nativeFrames);
        var color = ValidateStyle(style);
        var primary = Prepare(sourcePng, style.CropTransparent);
        var sources = new List<PreparedSource> { primary };
        long byteBudget = 32 * 1024 * 1024L - sourcePng.Length;
        long pixelBudget = 16 * 1024 * 1024L - primary.PixelCount;
        bool skipped = nativeFrames.Count > 64;
        foreach (var frame in nativeFrames.Take(64))
        {
            if (frame is not null && ReferenceEquals(frame.Png, sourcePng))
            {
                if (frame.PixelWidth != primary.Width || frame.PixelHeight != primary.Height) skipped = true;
                continue;
            }
            if (frame is null || frame.Png is null || frame.Png.Length > byteBudget ||
                frame.PixelWidth is < 1 or > 4096 || frame.PixelHeight is < 1 or > 4096 ||
                (long)frame.PixelWidth * frame.PixelHeight > pixelBudget)
            { skipped = true; continue; }
            byteBudget -= frame.Png.Length;
            try
            {
                var prepared = Prepare(frame.Png, style.CropTransparent, frame.PixelWidth, frame.PixelHeight);
                pixelBudget -= prepared.PixelCount;
                sources.Add(prepared);
            }
            catch (ArgumentException) { skipped = true; }
        }
        var frames = IconSizes.Select(size =>
        {
            // Keep the canonical source for the 256 px preview/ICO. Small native resources
            // can contain deliberately simplified artwork; select by actual drawn span,
            // not metadata dimensions, so transparent padding cannot trigger enlargement.
            double target = size * (style.Backdrop == BackdropStyle.MacOS
                ? MacIconMaterial.ContentScale(style, primary.Appearance) : style.ContentScale);
            var selected = size == 256 ? primary : sources.Where(source => source.Span >= target)
                .OrderBy(source => source.Span).FirstOrDefault() ?? sources.MaxBy(source => source.Span)!;
            return style.Backdrop == BackdropStyle.MacOS
                ? MacIconMaterial.Render(selected.Pixels, primary.Appearance, style, size)
                : RenderFrame(selected.Pixels, style, color, size);
        }).ToArray();
        var notes = new List<string>();
        if (256 * style.ContentScale > primary.Span * 1.05)
            notes.Add("原图可见图案分辨率较低，放大后无法补回细节。");
        if (style.ContentScale < 0.65)
            notes.Add("图案占比较小，小尺寸下的细节可能更难辨认。");
        if (style.Backdrop is BackdropStyle.Gradient or BackdropStyle.Solid && style.ContentScale >= 0.8)
            notes.Add("底板会裁切超出轮廓的图案边缘，可降低图案比例保留更多内容。");
        if (skipped)
            notes.Add("部分原始尺寸无法读取或超出处理上限，已使用可用图标生成。");
        return new RenderResult(frames[^1], EncodeIcon(frames)) { Notes = notes };
    }

    private static PreparedSource Prepare(byte[] sourcePng, bool crop, int expectedWidth = 0, int expectedHeight = 0)
    {
        if (sourcePng.Length is < 24 or > 32 * 1024 * 1024 || !sourcePng.AsSpan(0, 8).SequenceEqual(PngSignature))
            throw new ArgumentException("输入必须是有效 PNG，且不得超过 32 MiB。", nameof(sourcePng));

        using var data = SKData.CreateCopy(sourcePng);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new ArgumentException("无法读取 PNG 图标。", nameof(sourcePng));
        var info = codec.Info;
        if (info.Width is < 1 or > 4096 || info.Height is < 1 or > 4096)
            throw new ArgumentException("PNG 图标宽高必须在 1–4096 像素之间。", nameof(sourcePng));
        if (expectedWidth != 0 && (info.Width != expectedWidth || info.Height != expectedHeight))
            throw new ArgumentException("原始尺寸信息与 PNG 不匹配。", nameof(sourcePng));
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException("PNG 图标已损坏或不完整。", nameof(sourcePng));

        var visible = FindVisibleBounds(bitmap);
        var bounds = crop ? visible : new SKRectI(0, 0, bitmap.Width, bitmap.Height);
        var source = new PixelResampler(bitmap, bounds);
        return new(source, Math.Max(bounds.Width, bounds.Height), info.Width, info.Height, MacIconMaterial.Analyze(bitmap, visible));
    }

    private sealed record PreparedSource(PixelResampler Pixels, int Span, int Width, int Height, MacIconMaterial.SourceAppearance Appearance)
    {
        internal long PixelCount => (long)Width * Height;
    }

    private static SKColor ValidateStyle(IconStyle style)
    {
        if (!Enum.IsDefined(style.Backdrop))
            throw new ArgumentOutOfRangeException(nameof(style), "未知底板类型。");
        if (!double.IsFinite(style.ContentScale) || style.ContentScale < 0.1 || style.ContentScale > 1)
            throw new ArgumentOutOfRangeException(nameof(style), "图案比例必须在 0.1–1 之间。");
        if (!double.IsFinite(style.CornerRadius) || style.CornerRadius < 0 || style.CornerRadius > 0.5)
            throw new ArgumentOutOfRangeException(nameof(style), "圆角比例必须在 0–0.5 之间。");
        var text = style.BackgroundColor;
        if (text is null || text.Length != 7 || text[0] != '#' ||
            !uint.TryParse(text.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
            throw new ArgumentException("底板颜色必须使用 #RRGGBB 格式。", nameof(style));
        return new SKColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    private static SKRectI FindVisibleBounds(SKBitmap bitmap)
    {
        // Keep every nonzero-alpha pixel. A fixed alpha threshold cuts antialiased edges
        // and fine translucent detail before the downsampler has a chance to integrate it.
        var pixels = bitmap.Pixels;
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (pixels[y * bitmap.Width + x].Alpha == 0) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        if (right < left)
            throw new ArgumentException("PNG 图标完全透明，没有可适配的内容。", "sourcePng");
        return new SKRectI(left, top, right + 1, bottom + 1);
    }

    private static byte[] RenderFrame(PixelResampler source, IconStyle style, SKColor color, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        using var foreground = source.Render(size, style.ContentScale);
        using var foregroundImage = SKImage.FromBitmap(foreground);
        if (style.Backdrop != BackdropStyle.None)
        {
            var inset = size * 0.055f;
            var plate = new SKRect(inset, inset, size - inset, size - inset);
            var radius = Math.Min((float)style.CornerRadius * size, plate.Width / 2);
            using var shadowFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, size * 0.018f);
            using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(12, 16, 28, 42), MaskFilter = shadowFilter };
            canvas.Save();
            canvas.Translate(0, size * 0.018f);
            canvas.DrawRoundRect(plate, radius, radius, shadow);
            canvas.Restore();
            // Composite the opaque plate and artwork before applying shape coverage.
            // Clipping/drawing them separately would apply AA coverage more than once.
            using var content = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
            using var paint = new SKPaint { Color = color };
            using var gradient = style.Backdrop == BackdropStyle.Gradient
                ? SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                    [Mix(color, SKColors.White, 0.18f), Mix(color, SKColors.Black, 0.12f)], SKShaderTileMode.Clamp)
                : null;
            paint.Shader = gradient;
            content.Canvas.DrawPaint(paint);
            content.Canvas.DrawImage(foregroundImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            using var contentImage = content.Snapshot();
            using var shape = new SKRoundRect(plate, radius, radius);
            canvas.Save();
            canvas.ClipRoundRect(shape, SKClipOperation.Intersect, antialias: true);
            canvas.DrawImage(contentImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            canvas.Restore();
        }
        else
        {
            // Transparency-only mode deliberately retains the artwork's original shape.
            canvas.DrawImage(foregroundImage, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
        }
        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private static SKColor Mix(SKColor from, SKColor to, float amount) => new(
        (byte)Math.Round(from.Red + (to.Red - from.Red) * amount),
        (byte)Math.Round(from.Green + (to.Green - from.Green) * amount),
        (byte)Math.Round(from.Blue + (to.Blue - from.Blue) * amount));

    private static byte[] EncodeIcon(byte[][] frames)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0); // reserved
        writer.Write((ushort)1); // icon
        writer.Write((ushort)frames.Length);
        var offset = 6 + frames.Length * 16;
        for (var i = 0; i < frames.Length; i++)
        {
            writer.Write((byte)(IconSizes[i] == 256 ? 0 : IconSizes[i]));
            writer.Write((byte)(IconSizes[i] == 256 ? 0 : IconSizes[i]));
            writer.Write((byte)0); // RGBA, no palette
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)frames[i].Length);
            writer.Write((uint)offset);
            offset += frames[i].Length;
        }
        foreach (var frame in frames) writer.Write(frame);
        return stream.ToArray();
    }
}
