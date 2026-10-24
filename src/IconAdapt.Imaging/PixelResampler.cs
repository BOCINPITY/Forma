using System.Runtime.InteropServices;
using SkiaSharp;

namespace IconAdapt.Imaging;

/// <summary>
/// Positive, separable filtering in premultiplied RGBA. Reduction integrates the complete
/// output-pixel footprint; magnification uses bilinear reconstruction and finite-domain coverage.
/// Native 1:1 aligned pixels are copied exactly. No sharpening, ringing or shared mutable state.
/// </summary>
internal sealed class PixelResampler
{
    private readonly byte[] pixels;
    private readonly int stride;
    private readonly SKRectI bounds;

    internal PixelResampler(SKBitmap bitmap, SKRectI bounds)
    {
        this.bounds = bounds;
        stride = bitmap.RowBytes;
        pixels = new byte[stride * bitmap.Height];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
    }

    internal SKBitmap Render(int size, double contentScale)
    {
        double factor = size * contentScale / Math.Max(bounds.Width, bounds.Height);
        double left = (size - bounds.Width * factor) / 2;
        double top = (size - bounds.Height * factor) / 2;
        var horizontal = CreateKernels(size, bounds.Width, factor, left);
        var vertical = CreateKernels(size, bounds.Height, factor, top);
        var intermediate = new float[size * bounds.Height * 4];
        for (int y = 0; y < bounds.Height; y++)
        for (int x = 0; x < size; x++)
        {
            int destination = (y * size + x) * 4;
            foreach (var tap in horizontal[x])
            {
                int source = (y + bounds.Top) * stride + (tap.Index + bounds.Left) * 4;
                for (int channel = 0; channel < 4; channel++)
                    intermediate[destination + channel] += pixels[source + channel] * tap.Weight;
            }
        }

        var output = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int destination = (y * size + x) * 4;
            // Accumulate all channels before rounding. Rounding the horizontal pass loses
            // subpixel coverage twice and can erase fine transparent strokes.
            for (int channel = 0; channel < 4; channel++)
            {
                float sum = 0;
                foreach (var tap in vertical[y])
                    sum += intermediate[(tap.Index * size + x) * 4 + channel] * tap.Weight;
                output[destination + channel] = (byte)Math.Clamp((int)MathF.Round(sum), 0, 255);
            }
        }
        var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
        Marshal.Copy(output, 0, bitmap.GetPixels(), output.Length);
        return bitmap;
    }

    private static Tap[][] CreateKernels(int size, int sourceLength, double factor, double offset)
    {
        var kernels = new Tap[size][];
        for (int position = 0; position < size; position++)
        {
            var taps = new List<Tap>();
            if (factor < 1)
            {
                double begin = (position - offset) / factor;
                double end = (position + 1 - offset) / factor;
                int first = Math.Max(0, (int)Math.Floor(begin));
                int last = Math.Min(sourceLength - 1, (int)Math.Ceiling(end) - 1);
                for (int source = first; source <= last; source++)
                {
                    double overlap = Math.Min(source + 1, end) - Math.Max(source, begin);
                    if (overlap > 0) taps.Add(new(source, (float)(overlap * factor)));
                }
            }
            else
            {
                double begin = Math.Max(0, (position - offset) / factor);
                double end = Math.Min(sourceLength, (position + 1 - offset) / factor);
                if (end <= begin) { kernels[position] = []; continue; }
                float coverage = (float)((end - begin) * factor);
                double center = Math.Clamp((begin + end) / 2 - 0.5, 0, sourceLength - 1);
                int first = (int)Math.Floor(center);
                for (int source = first; source <= first + 1; source++)
                {
                    if (source < 0 || source >= sourceLength) continue;
                    double weight = 1 - Math.Abs(source - center);
                    if (weight > 0) taps.Add(new(source, (float)weight * coverage));
                }
            }
            // Boundary pixels retain fractional domain coverage. They are neither normalized
            // to opaque full pixels nor allowed to bleed outside the intended image rectangle.
            kernels[position] = taps.ToArray();
        }
        return kernels;
    }

    private readonly record struct Tap(int Index, float Weight);
}
