using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using IconAdapt.App;
using IconAdapt.App.Controls;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;
using IconAdapt.Imaging;

internal static class DesktopPreviewChecks
{
    private static int assertions;

    public static void Run()
    {
        UiTestApplication.EnsureResources();
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        string output = Path.Combine(RepositoryRoot(), ".local", "desktop-preview");
        string data = Path.Combine(output, "synthetic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(data);
        var service = new PreviewService(data);
        var renderer = new PreviewRenderer();
        using var vm = new MainViewModel(service, renderer, Path.Combine(data, "演示快捷方式"), data);
        MainWindow? window = null;
        try
        {
            VerifyFallbackNotifications(service);
            Complete(vm.InitializeAsync());
            window = new MainWindow(vm, null)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false
            };
            Motion.SetIsEnabled(window, false);
            window.Show();
            Settle(window);
            UiTestApplication.VerifyIsolation(window);
            var surface = Named<DesktopPreview>(window, "DesktopPreviewSurface");
            var gallery = Named<ListBox>(window, "IconGallery");
            Check(gallery.IsVisible && !surface.IsVisible, "The initial workspace must show the icon library.");
            int initialCalls = renderer.Calls;
            Click(window, "DesktopModeButton");
            Settle(window);
            Check(surface.IsVisible && !gallery.IsVisible, "Desktop mode must replace the library inside the same workspace.");
            Check(ReferenceEquals(surface.ItemsSource, vm.Items) && ReferenceEquals(surface.SizeCommand, vm.PreviewSizeCommand),
                "Desktop preview must share the view model's icon collection and existing size action.");
            Check(surface.SelectedItems.Count == vm.SelectedCount, "Desktop preview must include precisely the checked shortcuts.");
            var list = Named<ListBox>(surface, "DesktopItems");

            foreach (var size in new[] { new Size(1220, 800), new Size(960, 680) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                Settle(window);
                VisibleWithin(surface, (FrameworkElement)window.Content);
                VisibleWithin(Named<FrameworkElement>(window, "Inspector"), (FrameworkElement)window.Content);
                foreach (int pixels in new[] { 32, 48, 64, 96 })
                {
                    var sizeButton = Descendants(surface).OfType<Button>().Single(x => x.CommandParameter?.ToString() == pixels.ToString());
                    Check(ReferenceEquals(sizeButton.Command, vm.PreviewSizeCommand), "The desktop size button must invoke the existing frame selection action.");
                    sizeButton.Command!.Execute(sizeButton.CommandParameter);
                    Settle(window);
                    Check(vm.PreviewPixelSize == pixels && surface.PixelSize == pixels,
                        $"Desktop and inspector must use the same {pixels}px output frame.");
                    Click(surface, "AdaptedModeButton");
                    VerifyItemImage(window, surface, list, vm.Items[0], pixels, true);
                    Click(surface, "OriginalModeButton");
                    Settle(window);
                    VerifyItemImage(window, surface, list, vm.Items[0], pixels, false);
                    if (pixels == 96) VerifySmallOriginalPixels(window, list, vm.Items[0]);
                    Click(surface, "AdaptedModeButton");
                    Click(surface, "LightBackgroundButton");
                    Settle(window);
                    Capture(window, Path.Combine(output, $"desktop-light-{size.Width:0}x{size.Height:0}-{pixels}px.png"));
                }
                VerifyLongName(window, list, vm.Items[1]);
                var light = CaptureBitmap(window);
                Click(surface, "DarkBackgroundButton");
                Settle(window);
                Check(surface.IsDark && !Pixels(light).SequenceEqual(Pixels(CaptureBitmap(window))), "The dark background control must change the rendered scene.");
                Capture(window, Path.Combine(output, $"desktop-dark-{size.Width:0}x{size.Height:0}.png"));
                Click(surface, "OriginalModeButton");
                Settle(window);
                Check(!surface.IsAdapted, "The original mode button must switch the desktop image presentation.");
                Capture(window, Path.Combine(output, $"desktop-original-{size.Width:0}x{size.Height:0}.png"));
                Click(surface, "AdaptedModeButton");
                Click(surface, "LightBackgroundButton");
                Settle(window);
            }
            Check(renderer.Calls == initialCalls, "View, background, source and size changes must reuse existing rendered frames.");

            vm.PreviewSizeCommand.Execute("96");
            Settle(window);
            var scroll = Descendants(list).OfType<ScrollViewer>().First();
            Check(scroll.ScrollableHeight > 0, "Many checked shortcuts must scroll vertically in the compact desktop preview.");
            scroll.ScrollToEnd();
            Settle(window);
            Check(scroll.VerticalOffset > 0 && scroll.HorizontalOffset == 0,
                "Desktop preview must expose the final rows without horizontal panning.");
            Capture(window, Path.Combine(output, "desktop-scroll-960x680.png"));
            scroll.ScrollToHome();
            Settle(window);

            list.SelectedItem = vm.Items[5];
            Settle(window);
            Check(ReferenceEquals(surface.SelectedItem, vm.Items[5]) && ReferenceEquals(vm.FocusedItem, vm.Items[5]),
                "Selecting a desktop icon must update the same inspector focus as the library.");
            vm.FocusedItem = vm.Items[2];
            Settle(window);
            Check(ReferenceEquals(list.SelectedItem, vm.Items[2]), "Inspector focus must flow back into the desktop selection.");
            var inspectorImage = Named<NativePixelImage>(window, "AdaptedPreview");
            Check(ReferenceEquals(inspectorImage.Source, vm.Items[2].Preview), "The inspector must show the focused desktop icon's rendered frame.");

            var focused = vm.FocusedItem!;
            focused.IsSelected = false;
            Settle(window);
            Check(ReferenceEquals(vm.FocusedItem, focused) && !surface.SelectedItems.Contains(focused) && list.SelectedItem == null,
                "Unchecking the focused desktop icon must preserve the inspector's independent focus.");
            focused.IsSelected = true;
            Settle(window);
            Check(ReferenceEquals(list.SelectedItem, focused), "Rechecking the focused icon must restore its visible desktop selection.");

            var removed = vm.Items[3];
            removed.IsSelected = false;
            Settle(window);
            Check(!surface.SelectedItems.Contains(removed) && surface.SelectedItems.Count == vm.SelectedCount,
                "Unchecking an icon must immediately remove it from the desktop preview.");
            vm.SelectNoneCommand.Execute(null);
            Settle(window);
            Check(surface.SelectedItems.Count == 0 && list.Items.Count == 0,
                "Selecting none must empty the desktop scene without changing the icon library.");
            Check(Named<FrameworkElement>(surface, "EmptyPreview").IsVisible && vm.Items.Count > 0,
                "An empty selection must show helpful preview guidance while retaining scanned icons.");
            Capture(window, Path.Combine(output, "desktop-none-960x680.png"));
            Click(surface, "ReturnToLibraryButton");
            Settle(window);
            Check(gallery.IsVisible && !surface.IsVisible && vm.SelectedCount == 0,
                "The empty preview's Return to Library action must reveal the library without changing the selection.");
            Click(window, "DesktopModeButton");
            Settle(window);
            vm.SelectAllCommand.Execute(null);
            Settle(window);
            Check(surface.SelectedItems.Count == vm.Items.Count(x => x.CanSelect), "Select all must repopulate every supported desktop icon.");
            Click(window, "GalleryModeButton");
            Settle(window);
            Check(gallery.IsVisible && !surface.IsVisible, "Users must be able to return to the library.");
            Click(window, "DesktopModeButton");
            Settle(window);
            Check(renderer.Calls == initialCalls, "Selection and focus changes must not regenerate ICO files.");

            var oldItems = vm.Items.ToArray();
            service.Generation++;
            vm.ScanCommand.Execute(null);
            Complete(vm.WaitForIdleAsync());
            Settle(window);
            Check(surface.SelectedItems.Count == vm.SelectedCount && !surface.SelectedItems.Any(oldItems.Contains),
                "Rescanning must replace desktop items and detach stale shortcut references.");
            Check(surface.SelectedItems.All(item => vm.Items.Contains(item)), "The desktop scene must contain only the current scan's items.");
            Check(ReferenceEquals(surface.SelectedItem, vm.FocusedItem), "The current scan's focus must remain synchronized with the desktop scene.");

            renderer.Fail = true;
            vm.CornerRadius = .31;
            Complete(vm.WaitForIdleAsync());
            Settle(window);
            var failed = vm.Items.First(x => x.CanSelect);
            Check(failed.Preview == null && ReferenceEquals(failed.DesktopImage, failed.Original),
                "A failed output must fall back to its original image instead of showing a blank desktop icon.");
            Check(failed.DesktopStatus.Length > 0 && failed.Status == "预览失败",
                "Original-image fallback must retain an explicit failure explanation.");
            VerifyItemImage(window, surface, list, failed, vm.PreviewPixelSize, true);
            Capture(window, Path.Combine(output, "desktop-render-fallback-960x680.png"));

            service.Empty = true;
            vm.ScanCommand.Execute(null);
            Complete(vm.WaitForIdleAsync());
            Settle(window);
            Check(surface.SelectedItems.Count == 0 && vm.Items.Count == 0,
                "An empty rescan must clear the old desktop scene.");
            Check(Named<FrameworkElement>(surface, "EmptyPreview").IsVisible, "An empty scan must preserve the desktop preview's empty guidance.");
            Check(service.WriteAttempts == 0, "Desktop preview interactions must never apply or restore a shortcut.");
            Capture(window, Path.Combine(output, "desktop-empty-scan-960x680.png"));
            CaptureShowcase(window, vm, service, renderer, output);
        }
        finally
        {
            window?.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        Console.WriteLine($"PASS: {assertions} desktop preview checks; real WPF workspace, physical sizes, shared frames, selection/focus, scrolling, rescan, original fallback and zero shortcut writes. Screenshots: {output}");
    }

    private static void CaptureShowcase(MainWindow window, MainViewModel vm, PreviewService service, PreviewRenderer renderer, string output)
    {
        // Keep the stress/low-resolution scenarios above intact. Documentation
        // uses six healthy synthetic icons rather than repeating those fixtures.
        service.Empty = false;
        service.Showcase = true;
        renderer.Fail = false;
        vm.CornerRadius = .23;
        vm.ScanCommand.Execute(null);
        Complete(vm.WaitForIdleAsync());
        Complete(WaitForShowcaseAsync(vm));
        Check(vm.Items.Count == 6 && vm.Items.All(x => x.Preview != null),
            "Documentation scenes must show completed adapted outputs, not temporary original fallbacks.");
        window.Width = 1220;
        window.Height = 800;
        vm.PreviewSizeCommand.Execute("48");
        var surface = Named<DesktopPreview>(window, "DesktopPreviewSurface");
        Click(surface, "AdaptedModeButton");
        Click(surface, "LightBackgroundButton");
        Settle(window);
        Capture(window, Path.Combine(output, "desktop-showcase-light.png"));
        Click(surface, "DarkBackgroundButton");
        Settle(window);
        Capture(window, Path.Combine(output, "desktop-showcase-dark.png"));
        Check(service.WriteAttempts == 0, "Capturing documentation scenes must not write desktop shortcuts.");
    }

    private static async Task WaitForShowcaseAsync(MainViewModel vm)
    {
        // The scan command starts its render after the scan operation completes.
        // Wait for those new outputs, rather than the cancelled pre-scan preview task.
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (vm.IsBusy || vm.IsRendering || vm.Items.Count != 6 || vm.Items.Any(x => x.Preview == null))
        {
            if (DateTime.UtcNow >= deadline) throw new TimeoutException("Showcase output did not finish rendering.");
            await Task.Delay(10);
        }
    }

    private static void VerifyItemImage(Window window, DesktopPreview surface, ListBox list, ShortcutItem item, int pixels, bool adapted)
    {
        list.ScrollIntoView(item);
        Settle(window);
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement
            ?? throw new InvalidOperationException("Desktop icon container was not realized.");
        var image = Descendants(container).OfType<NativePixelImage>().Single(x => x.IsVisible);
        var dpi = VisualTreeHelper.GetDpi(image);
        Check(image.PixelSize == pixels && Math.Abs(image.ActualWidth * dpi.DpiScaleX - pixels) < 1 && Math.Abs(image.ActualHeight * dpi.DpiScaleY - pixels) < 1,
            $"The desktop image must reserve exactly {pixels} physical pixels at the current DPI.");
        Check(ReferenceEquals(image.Source, adapted ? item.DesktopImage : item.Original),
            "The desktop icon must reuse the current native output/original bitmap without making a new preview image.");
        Check(!image.AnimateSourceChanges, "Desktop icon rows must not add source fades or image scaling animations.");
        if (!adapted || item.Preview == null)
            Check(!image.AllowUpscale, "Original images and fallback images must preserve native detail without enlarging small sources.");
    }

    private static void VerifyFallbackNotifications(PreviewService service)
    {
        var info = service.ScanAsync().GetAwaiter().GetResult().Items[0];
        var item = new ShortcutItem(info);
        Check(ReferenceEquals(item.DesktopImage, item.Original) && item.DesktopStatus.Length > 0,
            "An output that has not been generated must use an explained original-image fallback.");
        var notifications = new List<string?>();
        item.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        var frames = new Dictionary<int, BitmapSource> { [32] = item.Original!, [96] = item.Original! };
        item.SetRendered(new RenderResult([], []), frames);
        Check(ReferenceEquals(item.DesktopImage, item.Preview) && item.DesktopStatus.Length == 0,
            "A generated output must replace fallback and remove the fallback message.");
        Check(notifications.Contains(nameof(ShortcutItem.DesktopImage)) && notifications.Contains(nameof(ShortcutItem.DesktopStatus)),
            "Output completion must notify the desktop source and status bindings.");
        notifications.Clear();
        item.SetPreviewSize(96);
        Check(notifications.Contains(nameof(ShortcutItem.DesktopImage)), "Changing physical preview size must notify the desktop image binding.");
        item.SetRendered(null);
        Check(ReferenceEquals(item.DesktopImage, item.Original) && item.DesktopStatus.Length > 0,
            "Clearing a failed output must restore its original image and fallback message.");
    }

    private static void VerifySmallOriginalPixels(Window window, ListBox list, ShortcutItem item)
    {
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement
            ?? throw new InvalidOperationException("Small-source desktop container was not realized.");
        var image = Descendants(container).OfType<NativePixelImage>().Single(x => x.IsVisible);
        Check(image.Source?.PixelWidth == 32 && !image.AllowUpscale,
            "The tiny-source fixture must remain a native 32px source inside the 96px scene slot.");
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(image);
        Point position = image.TransformToAncestor(root).Transform(new Point());
        var crop = new Int32Rect((int)Math.Round(position.X * dpi.DpiScaleX), (int)Math.Round(position.Y * dpi.DpiScaleY), 96, 96);
        byte[] visible = Pixels(new CroppedBitmap(CaptureBitmap(window), crop));
        image.Opacity = 0;
        byte[] background = Pixels(new CroppedBitmap(CaptureBitmap(window), crop));
        image.Opacity = 1;
        int occupiedWidth = Enumerable.Range(0, 96).Count(x => Enumerable.Range(0, 96).Any(y =>
            Enumerable.Range(0, 4).Any(channel => visible[(y * 96 + x) * 4 + channel] != background[(y * 96 + x) * 4 + channel])));
        Check(occupiedWidth is > 20 and <= 32,
            $"The actual original's painted width must remain within 32 physical pixels in a 96px slot (painted {occupiedWidth}px).");
    }

    private static void VerifyLongName(Window window, ListBox list, ShortcutItem item)
    {
        list.ScrollIntoView(item);
        Settle(window);
        var container = list.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement
            ?? throw new InvalidOperationException("Long-name desktop container was not realized.");
        var label = Descendants(container).OfType<TextBlock>().First(x => x.Text == item.Name);
        Check(label.TextTrimming != TextTrimming.None || label.TextWrapping != TextWrapping.NoWrap,
            "Long desktop labels must wrap or trim within their icon cell.");
        Check(label.ActualWidth <= container.ActualWidth + 1, "Long application names must not widen a desktop icon cell.");
    }

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing preview element: {name}.");
    private static void Click(FrameworkElement root, string name) => Named<Button>(root, name).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    private static void VisibleWithin(FrameworkElement element, FrameworkElement root)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));
        Check(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0 && bounds.Left >= -1 && bounds.Top >= -1 && bounds.Right <= root.ActualWidth + 1 && bounds.Bottom <= root.ActualHeight + 1,
            $"{element.Name} must remain visible within the compact workspace.");
    }
    private static void Settle(Window window)
    {
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
    }
    private static void Complete(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            using var registration = timeout.Token.Register(() => dispatcher.BeginInvoke(() => frame.Continue = false));
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            if (!task.IsCompleted) throw new TimeoutException("Desktop preview fixture did not become idle.");
        }
        task.GetAwaiter().GetResult();
    }
    private static void Capture(Window window, string path)
    {
        var bitmap = CaptureBitmap(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
    private static BitmapSource CaptureBitmap(Window window)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        return bitmap;
    }
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[source.PixelWidth * source.PixelHeight * 4];
        converted.CopyPixels(bytes, source.PixelWidth * 4, 0);
        return bytes;
    }
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IconAdapt.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class PreviewRenderer : IIconRenderer
    {
        private readonly Dictionary<(string, IconStyle), RenderResult> cache = [];
        public int Calls;
        public bool Fail { get; set; }
        public RenderResult Render(byte[] source, IconStyle style) => Render(source, style, []);
        public RenderResult Render(byte[] source, IconStyle style, IReadOnlyList<IconFrame> nativeFrames)
        {
            Interlocked.Increment(ref Calls);
            if (Fail) throw new InvalidOperationException("Synthetic renderer failure for original fallback verification.");
            var key = (Convert.ToBase64String(source), style);
            if (!cache.TryGetValue(key, out var result)) cache[key] = result = new IconRenderer().Render(source, style, nativeFrames);
            return result;
        }
    }

    private sealed class PreviewService(string root) : IShortcutService
    {
        private readonly byte[][] images = Enumerable.Range(0, 6).Select(i => DrawIcon(i, i == 0 ? 32 : 128)).ToArray();
        public int Generation { get; set; }
        public bool Empty { get; set; }
        public bool Showcase { get; set; }
        public int WriteAttempts { get; private set; }
        public void Dispose() { }
        public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default)
        {
            string[] names = ["终端", "一个非常长的应用名称用于检查桌面图标文字换行及省略功能 Desktop Sample Application", "文件", "相机", "笔记", "阅读"];
            byte[][] sourceImages = images;
            if (Showcase)
            {
                names = ["终端", "设计", "文件", "相机", "笔记", "阅读"];
                sourceImages = Enumerable.Range(0, 6).Select(i => DrawIcon(i, 128)).ToArray();
            }
            var items = Enumerable.Range(0, Showcase ? 6 : 42).Select(i => new ShortcutInfo(
                Path.Combine(root, $"{Generation:00}-{i:00}.lnk"), names[i % names.Length], "", "", 0, "", sourceImages[i % sourceImages.Length], i != 41,
                i == 41 ? "Synthetic unsupported shortcut." : null)
            { SourcePixelWidth = !Showcase && i % images.Length == 0 ? 32 : 128, SourcePixelHeight = !Showcase && i % images.Length == 0 ? 32 : 128,
              SourceFrames = [new IconFrame(!Showcase && i % images.Length == 0 ? 32 : 128, !Showcase && i % images.Length == 0 ? 32 : 128, sourceImages[i % sourceImages.Length])] }).ToArray();
            return Task.FromResult(new ScanResult(Empty ? [] : items, []));
        }
        public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OperationBatch>>([]);
        public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Desktop preview tests cannot write shortcuts."); }
        public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Desktop preview tests cannot restore shortcuts."); }
        public Task<RestoreDefaultsResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Desktop preview tests cannot restore defaults."); }
        private static byte[] DrawIcon(int index, int size)
        {
            Color[] colors = [Color.FromRgb(52, 113, 226), Color.FromRgb(135, 91, 212), Color.FromRgb(43, 157, 122), Color.FromRgb(223, 146, 55), Color.FromRgb(210, 85, 119), Color.FromRgb(69, 138, 174)];
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 128d, size / 128d));
                var fill = new SolidColorBrush(colors[index]);
                dc.DrawRoundedRectangle(fill, null, new Rect(6, 6, 116, 116), 25, 25);
                var pen = new Pen(Brushes.White, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                if (index == 0) { dc.DrawLine(pen, new Point(34, 44), new Point(52, 62)); dc.DrawLine(pen, new Point(52, 62), new Point(34, 80)); dc.DrawLine(pen, new Point(67, 81), new Point(92, 81)); }
                else if (index == 1) { dc.DrawLine(pen, new Point(39, 91), new Point(64, 36)); dc.DrawLine(pen, new Point(64, 36), new Point(89, 91)); dc.DrawLine(pen, new Point(48, 73), new Point(80, 73)); }
                else if (index == 2) { dc.DrawRoundedRectangle(Brushes.White, null, new Rect(30, 44, 68, 48), 6, 6); dc.DrawRoundedRectangle(Brushes.White, null, new Rect(30, 35, 31, 24), 6, 6); }
                else if (index == 3) { dc.DrawRoundedRectangle(null, pen, new Rect(31, 42, 66, 49), 8, 8); dc.DrawEllipse(null, pen, new Point(64, 66), 14, 14); }
                else if (index == 4) { for (int y = 41; y <= 85; y += 22) dc.DrawLine(pen, new Point(37, y), new Point(91, y)); }
                else { dc.DrawRoundedRectangle(null, pen, new Rect(35, 31, 58, 67), 5, 5); dc.DrawLine(pen, new Point(48, 49), new Point(80, 49)); dc.DrawLine(pen, new Point(48, 65), new Point(80, 65)); }
                dc.Pop();
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
    }
}
