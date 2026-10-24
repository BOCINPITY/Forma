namespace IconAdapt.App.Controls;

public readonly record struct PixelImageBounds(double Left, double Top, double Width, double Height);

/// <summary>Fits images and aligns every edge in physical pixels before converting to WPF units.</summary>
public static class PixelImageGeometry
{
    public static PixelImageBounds Calculate(int pixelSize, int sourceWidth, int sourceHeight, bool allowUpscale,
        double dpiScaleX, double dpiScaleY, double originXDip = 0, double originYDip = 0)
    {
        if (pixelSize <= 0 || sourceWidth <= 0 || sourceHeight <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSize));
        if (!double.IsFinite(dpiScaleX) || !double.IsFinite(dpiScaleY) || dpiScaleX <= 0 || dpiScaleY <= 0 ||
            !double.IsFinite(originXDip) || !double.IsFinite(originYDip)) throw new ArgumentOutOfRangeException(nameof(dpiScaleX));
        var scale = Math.Min((double)pixelSize / sourceWidth, (double)pixelSize / sourceHeight);
        if (!allowUpscale) scale = Math.Min(1, scale);
        var widthPixels = Math.Clamp(Math.Round(sourceWidth * scale), 1, pixelSize);
        var heightPixels = Math.Clamp(Math.Round(sourceHeight * scale), 1, pixelSize);
        var leftPixels = Math.Floor((pixelSize - widthPixels) / 2);
        var topPixels = Math.Floor((pixelSize - heightPixels) / 2);
        var correctionX = Math.Round(originXDip * dpiScaleX) - originXDip * dpiScaleX;
        var correctionY = Math.Round(originYDip * dpiScaleY) - originYDip * dpiScaleY;
        return new PixelImageBounds((leftPixels + correctionX) / dpiScaleX, (topPixels + correctionY) / dpiScaleY,
            widthPixels / dpiScaleX, heightPixels / dpiScaleY);
    }
}
