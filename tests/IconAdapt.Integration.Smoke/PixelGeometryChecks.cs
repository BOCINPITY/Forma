using IconAdapt.App.Controls;

internal static class PixelGeometryChecks
{
    public static void Run()
    {
        foreach (double dpi in new[] { 1.0, 1.25, 1.5, 2.0 })
        {
            var native = PixelImageGeometry.Calculate(48, 32, 32, false, dpi, dpi);
            Check(Near(native.Width * dpi, 32) && Near(native.Height * dpi, 32), $"A 32px source was enlarged at {dpi:P0} DPI.");
            Check(Near(native.Left * dpi, 8) && Near(native.Top * dpi, 8), "Native source should be centered without changing physical size.");
            foreach (int size in new[] { 32, 48, 64, 96 })
            {
                var exact = PixelImageGeometry.Calculate(size, size, size, true, dpi, dpi);
                Check(Near(exact.Width * dpi, size) && Near(exact.Height * dpi, size), "Actual output frames must stay 1:1 in physical pixels.");
            }

            const double originX = .25;
            const double originY = 17.375;
            var odd = PixelImageGeometry.Calculate(48, 31, 29, false, dpi, dpi, originX, originY);
            Check(Near(odd.Width * dpi, 31) && Near(odd.Height * dpi, 29), "Odd native source dimensions should not be resampled.");
            Check(IsInteger((originX + odd.Left) * dpi) && IsInteger((originY + odd.Top) * dpi), "Letterboxed left/top landed on half pixels.");
            Check(IsInteger((originX + odd.Left + odd.Width) * dpi) && IsInteger((originY + odd.Top + odd.Height) * dpi), "Letterboxed right/bottom landed on half pixels.");

            var downsampled = PixelImageGeometry.Calculate(48, 256, 161, false, dpi, dpi, originX, originY);
            Check(Near(downsampled.Width * dpi, 48) && downsampled.Height * dpi <= 48, "Large original did not fit the selected physical pixel size.");
            Check(IsInteger(downsampled.Height * dpi), "Downsampled aspect ratio introduced fractional device pixels.");
        }
        var differentAxes = PixelImageGeometry.Calculate(48, 31, 29, false, 1.25, 1.5, .3, .7);
        Check(Near(differentAxes.Width * 1.25, 31) && Near(differentAxes.Height * 1.5, 29), "DPI axes were incorrectly mixed.");
        Console.WriteLine("PASS: physical pixel preview geometry at 100/125/150/200% DPI (native size, exact frames, odd dimensions and global edge alignment)");
    }

    private static bool IsInteger(double value) => Near(value, Math.Round(value));
    private static bool Near(double left, double right) => Math.Abs(left - right) < 0.000001;
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
