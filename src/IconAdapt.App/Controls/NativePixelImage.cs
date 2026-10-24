using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace IconAdapt.App.Controls;

/// <summary>Displays a chosen icon size in physical screen pixels, including on scaled monitors.</summary>
public sealed class NativePixelImage : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(
        nameof(Source), typeof(BitmapSource), typeof(NativePixelImage), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty PixelSizeProperty = DependencyProperty.Register(
        nameof(PixelSize), typeof(int), typeof(NativePixelImage), new FrameworkPropertyMetadata(48, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender),
        value => value is int size && size is > 0 and <= 1024);
    public static readonly DependencyProperty AllowUpscaleProperty = DependencyProperty.Register(
        nameof(AllowUpscale), typeof(bool), typeof(NativePixelImage), new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public NativePixelImage()
    {
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    public BitmapSource? Source { get => (BitmapSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public int PixelSize { get => (int)GetValue(PixelSizeProperty); set => SetValue(PixelSizeProperty, value); }
    public bool AllowUpscale { get => (bool)GetValue(AllowUpscaleProperty); set => SetValue(AllowUpscaleProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        return new Size(PixelSize / dpi.DpiScaleX, PixelSize / dpi.DpiScaleY);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (Source == null) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var origin = new Point(0, 0);
        if (PresentationSource.FromVisual(this)?.RootVisual is Visual root && !ReferenceEquals(root, this))
            origin = TransformToAncestor(root).Transform(new Point());
        var bounds = PixelImageGeometry.Calculate(PixelSize, Source.PixelWidth, Source.PixelHeight, AllowUpscale,
            dpi.DpiScaleX, dpi.DpiScaleY, origin.X, origin.Y);
        drawingContext.DrawImage(Source, new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height));
    }
}
