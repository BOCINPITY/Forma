using System.Globalization;
using IconAdapt.Core;
using SkiaSharp;

namespace IconAdapt.Imaging;

/// <summary>Stateless CPU renderer. Each invocation owns and disposes its native resources.</summary>
public sealed class IconRenderer : IIconRenderer
{
    private static readonly int[] IconSizes = [16, 24, 32, 48, 64, 128, 256];
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    public RenderResult Render(byte[] sourcePng, IconStyle style)
    {
        ArgumentNullException.ThrowIfNull(sourcePng);
        ArgumentNullException.ThrowIfNull(style);
        var color = ValidateStyle(style);
        if (sourcePng.Length is < 24 or > 32 * 1024 * 1024 || !sourcePng.AsSpan(0, 8).SequenceEqual(PngSignature))
            throw new ArgumentException("输入必须是有效 PNG，且不得超过 32 MiB。", nameof(sourcePng));

        using var data = SKData.CreateCopy(sourcePng);
        using var codec = SKCodec.Create(data);
        if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Png)
            throw new ArgumentException("无法读取 PNG 图标。", nameof(sourcePng));
        var info = codec.Info;
        if (info.Width is < 1 or > 4096 || info.Height is < 1 or > 4096)
            throw new ArgumentException("PNG 图标宽高必须在 1–4096 像素之间。", nameof(sourcePng));
        using var bitmap = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ArgumentException("PNG 图标已损坏或不完整。", nameof(sourcePng));

        var visible = FindVisibleBounds(bitmap);
        var bounds = style.CropTransparent ? visible : new SKRect(0, 0, bitmap.Width, bitmap.Height);
        using var source = SKImage.FromBitmap(bitmap);
        var frames = IconSizes.Select(size => RenderFrame(source, bounds, style, color, size)).ToArray();
        return new RenderResult(frames[^1], EncodeIcon(frames));
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

    private static SKRect FindVisibleBounds(SKBitmap bitmap)
    {
        // Ignore nearly invisible padding, but retain genuinely faint artwork.
        byte maximumAlpha = 0;
        var pixels = bitmap.Pixels;
        foreach (var pixel in pixels) maximumAlpha = Math.Max(maximumAlpha, pixel.Alpha);
        if (maximumAlpha == 0)
            throw new ArgumentException("PNG 图标完全透明，没有可适配的内容。", "sourcePng");
        var threshold = Math.Min(8, (int)maximumAlpha);
        var left = bitmap.Width;
        var top = bitmap.Height;
        var right = -1;
        var bottom = -1;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            if (pixels[y * bitmap.Width + x].Alpha < threshold) continue;
            left = Math.Min(left, x);
            top = Math.Min(top, y);
            right = Math.Max(right, x);
            bottom = Math.Max(bottom, y);
        }
        return new SKRect(left, top, right + 1, bottom + 1);
    }

    private static byte[] RenderFrame(SKImage source, SKRect bounds, IconStyle style, SKColor color, int size)
    {
        using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
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
            using var paint = new SKPaint { IsAntialias = true, Color = color };
            using var gradient = style.Backdrop == BackdropStyle.Gradient
                ? SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(size, size),
                    [Mix(color, SKColors.White, 0.18f), Mix(color, SKColors.Black, 0.12f)], SKShaderTileMode.Clamp)
                : null;
            paint.Shader = gradient;
            canvas.DrawRoundRect(plate, radius, radius, paint);
        }

        var edge = size * (float)style.ContentScale;
        var factor = edge / Math.Max(bounds.Width, bounds.Height);
        // At native small sizes a very narrow mark would otherwise disappear between pixels.
        var width = Math.Max(1, bounds.Width * factor);
        var height = Math.Max(1, bounds.Height * factor);
        var target = SKRect.Create((size - width) / 2, (size - height) / 2, width, height);
        using var foreground = new SKPaint { IsAntialias = true };
        canvas.DrawImage(source, bounds, target, new SKSamplingOptions(SKCubicResampler.Mitchell), foreground);
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
