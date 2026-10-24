using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IconAdapt.App;
using IconAdapt.App.Controls;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;
using IconAdapt.Imaging;

internal static class MotionChecks
{
    private static int assertions;

    public static void Run()
    {
        UiTestApplication.EnsureResources();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        try
        {
            VerifyMotionAndPixels();
            CaptureActualControls();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previousContext); }
        Console.WriteLine($"PASS: {assertions} motion checks; interruption, reduced motion, reveal layout, source pixels and actual WPF control frames.");
    }

    private static void VerifyMotionAndPixels()
    {
        var opacity = new Border { Width = 32, Height = 32, Background = Brushes.CornflowerBlue };
        var offset = new Border { Width = 20, Height = 20, Background = Brushes.White, RenderTransform = new TranslateTransform() };
        var angle = new Border { Width = 20, Height = 20, Background = Brushes.SlateGray, RenderTransform = new RotateTransform(), RenderTransformOrigin = new Point(.5, .5) };
        var content = new Border { Height = 64, Background = Brushes.AliceBlue };
        var reveal = new Border { Width = 180, Child = content };
        var first = Pixels(48, false);
        var second = Pixels(48, true);
        var animated = new NativePixelImage { Source = first, PixelSize = 48, AnimateSourceChanges = true };
        var raw = new NativePixelImage { Source = first, PixelSize = 48 };
        var canvas = new Canvas();
        Add(canvas, opacity, 0, 0);
        Add(canvas, offset, 64, 0);
        Add(canvas, angle, 128, 0);
        Add(canvas, reveal, 0, 64);
        Add(canvas, animated, 0, 160);
        Add(canvas, raw, 64, 160);
        var window = Offscreen(new Window { Width = 320, Height = 260, WindowStyle = WindowStyle.None, Content = canvas });
        Check(!Motion.CanAnimate(opacity), "Unloaded elements must not start a motion clock.");
        Motion.SetOpacity(opacity, .4);
        Check(Near(opacity.Opacity, .4) && !opacity.HasAnimatedProperties, "Unloaded opacity changes must immediately reach the target.");
        Motion.SetOpacity(opacity, 1);
        try
        {
            window.Show();
            Pump(50);
            Settle(window);
            UiTestApplication.VerifyIsolation(window);
            bool animate = Motion.CanAnimate(opacity);
            Check(animate == SystemParameters.ClientAreaAnimation, "Loaded motion must respect the system's client-area animation setting.");
            if (animate)
            {
                Motion.SetOpacity(opacity, 0);
                WaitFor(() => opacity.Opacity is > .01 and < .99, "Opacity must expose an intermediate frame.");
                double interrupted = opacity.Opacity;
                Motion.SetOpacity(opacity, 1);
                Check(Math.Abs(opacity.Opacity - interrupted) < .1, $"Reversing opacity must continue from the currently displayed value ({interrupted:F3} to {opacity.Opacity:F3}).");
                Pump(300);
                Check(Near(opacity.Opacity, 1) && !opacity.HasAnimatedProperties, "Reversed opacity must finish exactly and release its clock.");

                Motion.SetOffsetX(offset, 18);
                var translation = (TranslateTransform)offset.RenderTransform;
                WaitFor(() => translation.X is > .1 and < 17.9, "Switch translation must expose an intermediate frame.");
                interrupted = translation.X;
                Motion.SetOffsetX(offset, 0);
                Check(Math.Abs(translation.X - interrupted) < .5, "A reversing switch must continue from its current position.");
                Pump(300);
                Check(Near(translation.X, 0) && !translation.HasAnimatedProperties, "Switch reversal must not retain a transform clock.");

                Motion.SetAngle(angle, 90);
                var rotation = (RotateTransform)angle.RenderTransform;
                WaitFor(() => rotation.Angle is > 1 and < 89, "Disclosure rotation must expose an intermediate frame.");
                interrupted = rotation.Angle;
                Motion.SetAngle(angle, 0);
                Check(Math.Abs(rotation.Angle - interrupted) < 1, "Disclosure reversal must not jump to an endpoint.");
                Pump(300);
                Check(Near(rotation.Angle, 0) && !rotation.HasAnimatedProperties, "Disclosure rotation must release its clock.");

                Motion.SetReveal(reveal, false);
                WaitFor(() => reveal.ActualHeight is > 1 and < 63, "Closing details must expose a partial layout height.");
                content.Height = 100;
                Motion.SetReveal(reveal, true);
                Pump(350);
                Settle(window);
                Check(reveal.Visibility == Visibility.Visible && double.IsNaN(reveal.Height) && Near(reveal.ActualHeight, 100) && !reveal.HasAnimatedProperties,
                    "A reversed reveal must reach the updated natural content height and release its clock.");
                content.Height = 112;
                Settle(window);
                Check(Near(reveal.ActualHeight, 112), "Expanded details must return to automatic layout when their content changes.");

                Motion.SetOpacity(opacity, 0);
                Motion.SetOffsetX(offset, 18);
                Motion.SetAngle(angle, 90);
                Motion.SetReveal(reveal, false);
                Pump(35);
            }
            else Console.WriteLine("System animations are disabled: temporal intermediate-frame checks were skipped; immediate-target checks still run.");

            Motion.SetIsEnabled(window, false);
            Check(!Motion.CanAnimate(opacity), "A disabled motion setting must inherit from the test window.");
            Check(!opacity.HasAnimatedProperties && !((TranslateTransform)offset.RenderTransform).HasAnimatedProperties && !((RotateTransform)angle.RenderTransform).HasAnimatedProperties && !reveal.HasAnimatedProperties,
                "Disabling motion during a transition must immediately remove every active clock.");
            Motion.SetOpacity(opacity, .25);
            Motion.SetOffsetX(offset, 9);
            Motion.SetAngle(angle, 45);
            Motion.SetReveal(reveal, false);
            Settle(window);
            Check(Near(opacity.Opacity, .25) && Near(((TranslateTransform)offset.RenderTransform).X, 9) && Near(((RotateTransform)angle.RenderTransform).Angle, 45),
                "Reduced motion must immediately apply each requested visual target.");
            Check(reveal.Visibility == Visibility.Collapsed && Near(reveal.Height, 0), "Reduced-motion closing must immediately collapse the layout.");
            Motion.SetReveal(reveal, true);
            Settle(window);
            Check(reveal.Visibility == Visibility.Visible && double.IsNaN(reveal.Height) && Near(reveal.ActualHeight, 112),
                "Reduced-motion opening must immediately restore natural layout.");

            Motion.SetIsEnabled(window, true);
            animated.Source = second;
            raw.Source = second;
            if (Motion.CanAnimate(animated))
                Check(animated.Opacity < 1 || animated.HasAnimatedProperties, "Opted-in preview source changes must start their brief fade.");
            Check(!raw.AnimateSourceChanges && Near(raw.Opacity, 1) && !raw.HasAnimatedProperties,
                "Raw pixel previews must not acquire animation implicitly.");
            Pump(350);
            Check(Near(animated.Opacity, 1) && !animated.HasAnimatedProperties, "A source fade must settle at full opacity without a retained clock.");
            var animatedPixels = Draw(animated);
            var rawPixels = Draw(raw);
            var expectedPixels = BitmapPixels(second);
            Check(animatedPixels.SequenceEqual(expectedPixels), $"An animated preview's final image must exactly equal its native source pixels (animated bytes {animatedPixels.Zip(expectedPixels).Count(x => x.First != x.Second)}, raw bytes {rawPixels.Zip(expectedPixels).Count(x => x.First != x.Second)} differ; dpi {VisualTreeHelper.GetDpi(animated).DpiScaleX:F2}; bounds {animated.ActualWidth:F2}x{animated.ActualHeight:F2}).");
            Check(rawPixels.SequenceEqual(expectedPixels), "An unanimated preview must remain an exact pixel rendering.");

            Motion.SetOpacity(opacity, .75);
            window.Close();
            Pump(20);
            Check(!opacity.IsLoaded && Near(opacity.Opacity, .75) && !opacity.HasAnimatedProperties,
                "Unloading a transitioning element must finish its target and clear its clock.");
        }
        finally { if (window.IsVisible) window.Close(); }
    }

    private static void CaptureActualControls()
    {
        string root = RepositoryRoot();
        string output = Path.Combine(root, ".local", "motion-preview");
        string framesPath = Path.Combine(output, "frames");
        Directory.CreateDirectory(framesPath);
        string data = Path.Combine(output, "data-" + Guid.NewGuid().ToString("N"));
        var service = new DemoService(data);
        using var vm = new MainViewModel(service, new IconRenderer(), Path.Combine(data, "动画演示"), data);
        Complete(vm.InitializeAsync());
        var window = Offscreen(new MainWindow(vm, null));
        Motion.SetIsEnabled(window, false);
        var captured = new List<(int Index, long Milliseconds, string Action, BitmapSource Bitmap)>();
        var telemetry = new List<object>();
        try
        {
            window.Show();
            Settle(window);
            UiTestApplication.VerifyIsolation(window);
            var toggle = (CheckBox)window.FindName("CropCheckBox");
            var notice = (Expander)window.FindName("NoticeExpander");
            var details = (Expander)window.FindName("DetailExpander");
            var selector = (ComboBox)window.FindName("BackdropSelector");
            selector.ApplyTemplate();
            var popup = (Popup)selector.Template.FindName("PART_Popup", selector);
            Check(!Motion.GetIsEnabled(popup) && popup.PopupAnimation == PopupAnimation.None,
                "The actual combo popup must inherit disabled motion and suppress its native animation.");
            bool? initialToggle = toggle.IsChecked;
            toggle.IsChecked = false;
            Settle(window);
            toggle.ApplyTemplate();
            var knob = (FrameworkElement)toggle.Template.FindName("SwitchKnob", toggle);
            Check(Near(knob.RenderTransform.Value.OffsetX, 0),
                "The actual unchecked switch must place its thumb at zero.");
            notice.IsExpanded = false;
            details.IsExpanded = false;
            Settle(window);
            Motion.SetIsEnabled(window, true);
            Settle(window);
            Check(popup.PopupAnimation == (SystemParameters.ClientAreaAnimation ? PopupAnimation.Fade : PopupAnimation.None),
                "The actual combo popup must restore fade only when system motion is enabled.");
            toggle.IsChecked = true;
            if (Motion.CanAnimate(knob))
                WaitFor(() => knob.RenderTransform is TranslateTransform transform && transform.X is > .1 and < 13.9,
                    "The actual switch template must display an intermediate thumb position.");
            Pump(300);
            Check(knob.RenderTransform is TranslateTransform on && Near(on.X, 14) && !on.HasAnimatedProperties,
                $"The actual checked switch must settle at fourteen without retaining its clock (X={knob.RenderTransform.Value.OffsetX:F3}; target={Motion.GetOffsetX(knob):F3}; checked={toggle.IsChecked}; animated={(knob.RenderTransform as TranslateTransform)?.HasAnimatedProperties}).");
            toggle.IsChecked = initialToggle;
            Complete(vm.WaitForIdleAsync());
            Pump(250);
            var elapsed = Stopwatch.StartNew();
            for (int frame = 0; frame < 76; frame++)
            {
                string action = "";
                switch (frame)
                {
                    case 5: toggle.IsChecked = false; vm.FocusedItem = vm.Items[1]; notice.IsExpanded = true; action = "switch off; select second icon; expand results"; break;
                    case 20: toggle.IsChecked = true; notice.IsExpanded = false; action = "switch on; close results"; break;
                    case 35: vm.FocusedItem = vm.Items[2]; details.IsExpanded = true; action = "select third icon; expand icon details"; break;
                    case 50: toggle.IsChecked = false; details.IsExpanded = false; notice.IsExpanded = true; action = "switch off; close details; expand results"; break;
                    case 65: toggle.IsChecked = true; notice.IsExpanded = false; vm.FocusedItem = vm.Items[0]; action = "switch on; close results; select first icon"; break;
                }
                Settle(window);
                long milliseconds = elapsed.ElapsedMilliseconds;
                var originalPreview = (NativePixelImage)window.FindName("OriginalPreview");
                telemetry.Add(new
                {
                    Index = frame, Milliseconds = milliseconds, Action = action,
                    SwitchOffset = knob.RenderTransform.Value.OffsetX,
                    NoticeHeight = ((FrameworkElement)notice.Template.FindName("Details", notice)).ActualHeight,
                    DetailsHeight = ((FrameworkElement)details.Template.FindName("Details", details)).ActualHeight,
                    PreviewOpacity = originalPreview.Opacity
                });
                var bitmap = Capture((FrameworkElement)window.Content);
                Save(bitmap, Path.Combine(framesPath, $"frame-{frame:000}.png"));
                captured.Add((frame, milliseconds, action, bitmap));
                Pump(40);
            }
            Complete(vm.WaitForIdleAsync());
            Pump(250);
            Check(service.WriteAttempts == 0, "Recording actual control animations must never apply or restore a shortcut.");
            File.WriteAllText(Path.Combine(output, "frames.json"), JsonSerializer.Serialize(new
            {
                Source = "Actual MainWindow, WPF control templates and NativePixelImage; synthetic icons only.",
                SystemAnimations = SystemParameters.ClientAreaAnimation,
                Frames = telemetry
            }, new JsonSerializerOptions { WriteIndented = true }));
            Save(ContactSheet(captured), Path.Combine(output, "motion-stages.png"));
            Console.WriteLine($"Actual WPF animation frames: {framesPath}; contact sheet: {Path.Combine(output, "motion-stages.png")}");
        }
        finally { window.Close(); }
    }

    private static BitmapSource ContactSheet(IReadOnlyList<(int Index, long Milliseconds, string Action, BitmapSource Bitmap)> frames)
    {
        int[] samples = [0, 6, 9, 21, 36, 51];
        const int cellWidth = 610, cellHeight = 428;
        var drawing = new DrawingVisual();
        using (var dc = drawing.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, cellWidth * 2, cellHeight * 3));
            for (int i = 0; i < samples.Length; i++)
            {
                var sample = frames[samples[i]];
                double x = i % 2 * cellWidth, y = i / 2 * cellHeight;
                dc.DrawText(new FormattedText($"frame {sample.Index:00} · {sample.Milliseconds} ms", CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 13, Brushes.SlateGray, 1), new Point(x + 8, y + 5));
                dc.DrawImage(sample.Bitmap, new Rect(x, y + 25, cellWidth, 400));
            }
        }
        var bitmap = new RenderTargetBitmap(cellWidth * 2, cellHeight * 3, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(drawing);
        return bitmap;
    }

    private static T Offscreen<T>(T window) where T : Window
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000; window.Top = -20000;
        window.ShowActivated = false; window.ShowInTaskbar = false;
        window.UseLayoutRounding = true;
        return window;
    }
    private static void Add(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x); Canvas.SetTop(element, y); canvas.Children.Add(element);
    }
    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }
    private static void WaitFor(Func<bool> predicate, string message)
    {
        var timeout = Stopwatch.StartNew();
        while (!predicate() && timeout.ElapsedMilliseconds < 250) Pump(10);
        Check(predicate(), message);
    }
    private static void Settle(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static void Complete(Task task)
    {
        var timeout = Stopwatch.StartNew();
        while (!task.IsCompleted && timeout.Elapsed < TimeSpan.FromSeconds(30)) Pump(10);
        if (!task.IsCompleted) throw new TimeoutException("Motion fixture did not become idle.");
        task.GetAwaiter().GetResult();
    }
    private static BitmapSource Capture(FrameworkElement element)
    {
        var dpi = VisualTreeHelper.GetDpi(element);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(element);
        return bitmap;
    }
    private static byte[] Draw(NativePixelImage image)
    {
        var dpi = VisualTreeHelper.GetDpi(image);
        var root = (FrameworkElement)Window.GetWindow(image).Content;
        Point position = image.TransformToAncestor(root).Transform(new Point());
        // Render the real visual tree, then crop its physical pixels. Rendering a
        // positioned Canvas child alone retains its offset and yields an empty crop.
        var bitmap = Capture(root);
        return BitmapPixels(new CroppedBitmap(bitmap, new Int32Rect(
            (int)Math.Round(position.X * dpi.DpiScaleX),
            (int)Math.Round(position.Y * dpi.DpiScaleY), 48, 48)));
    }
    private static byte[] BitmapPixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var pixels = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(pixels, source.PixelWidth * 4, 0);
        return pixels;
    }
    private static BitmapSource Pixels(int size, bool alternate)
    {
        var pixels = Enumerable.Range(0, size * size).SelectMany(i => new byte[] { (byte)((i % size / 4 + i / size / 4) % 2 == 0 ? 32 : 220), alternate ? (byte)180 : (byte)80, alternate ? (byte)50 : (byte)210, 255 }).ToArray();
        var bitmap = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, pixels, size * 4);
        bitmap.Freeze();
        return bitmap;
    }
    private static void Save(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IconAdapt.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
    private static bool Near(double first, double second) => Math.Abs(first - second) < .01;
    private static void Check(bool value, string message)
    {
        assertions++;
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class DemoService(string root) : IShortcutService
    {
        public int WriteAttempts { get; private set; }
        public void Dispose() { }
        public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default)
        {
            string[] names = ["终端", "图形工具", "文件"];
            var items = names.Select((name, index) =>
            {
                var source = DemoIcon(index);
                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source)); encoder.Save(stream);
                byte[] png = stream.ToArray();
                return new ShortcutInfo(Path.Combine(root, name + ".lnk"), name, "", "", 0, "", png, true, null)
                { SourcePixelWidth = 128, SourcePixelHeight = 128, SourceFrames = [new IconFrame(128, 128, png)] };
            }).ToArray();
            return Task.FromResult(new ScanResult(items, []));
        }
        public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OperationBatch>>([]);
        public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Motion fixtures cannot write shortcuts."); }
        public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Motion fixtures cannot restore shortcuts."); }
        public Task<RestoreDefaultsResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Motion fixtures cannot restore defaults."); }
    }

    private static BitmapSource DemoIcon(int index)
    {
        Color[] colors = [Color.FromRgb(56, 118, 235), Color.FromRgb(137, 92, 224), Color.FromRgb(39, 160, 129)];
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            Color color = colors[index];
            var gradient = new LinearGradientBrush(Color.FromRgb((byte)Math.Min(255, color.R + 35), (byte)Math.Min(255, color.G + 35), (byte)Math.Min(255, color.B + 20)), color, 90);
            dc.DrawRoundedRectangle(gradient, null, new Rect(6, 6, 116, 116), 26, 26);
            var stroke = new Pen(Brushes.White, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            if (index == 0)
            {
                dc.DrawLine(stroke, new Point(36, 45), new Point(53, 62));
                dc.DrawLine(stroke, new Point(53, 62), new Point(36, 79));
                dc.DrawLine(stroke, new Point(67, 80), new Point(90, 80));
            }
            else if (index == 1)
            {
                dc.PushTransform(new RotateTransform(42, 64, 64));
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(55, 32, 18, 54), 4, 4);
                dc.DrawGeometry(Brushes.White, null, Geometry.Parse("M55,91 L73,91 L64,103 Z"));
                dc.DrawLine(new Pen(gradient, 3), new Point(55, 44), new Point(73, 44));
                dc.Pop();
            }
            else
            {
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), null,
                    Geometry.Parse("M30,43 Q30,36 37,36 L54,36 L64,46 L91,46 Q98,46 98,53 L98,57 L30,57 Z"));
                dc.DrawRoundedRectangle(Brushes.White, null, new Rect(30, 52, 68, 41), 7, 7);
            }
        }
        var bitmap = new RenderTargetBitmap(128, 128, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
