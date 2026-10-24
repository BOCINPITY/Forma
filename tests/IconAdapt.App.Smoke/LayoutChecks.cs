using System.IO;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using IconAdapt.App;
using IconAdapt.App.Controls;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;
using IconAdapt.Imaging;

internal static class LayoutChecks
{
    private static int assertions;

    public static void Run()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IconAdapt.slnx"))) directory = directory.Parent;
        string output = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."), ".local", "ui-redesign");
        Directory.CreateDirectory(output);
        string data = Path.Combine(output, Guid.NewGuid().ToString("N"));
        var service = new LayoutService(data);
        using var vm = new MainViewModel(service, new LayoutRenderer(), Path.Combine(data, "演示快捷方式"), data);
        Complete(vm.InitializeAsync());
        vm.Items[1].Status = "应用失败";
        vm.Items[1].Detail = "拒绝访问。测试文件不可写。";
        vm.Items[2].Status = "待核对 / 可恢复";
        vm.Items[2].Detail = "测试操作中断，备份及记录已保留。";
        vm.FocusedItem = vm.Items[0];

        // Load the actual application's resources without its OnStartup (which scans the real desktop).
        if (Application.Current == null)
        {
            var app = new IconAdapt.App.App();
            app.InitializeComponent();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        }
        var window = new MainWindow(vm, null)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000,
            Top = -20000,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        // A normal Application.Run installs this context. This console host pumps the
        // dispatcher itself, so install it explicitly before any bound async commands.
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
        try
        {
            window.Show();
            Settle(window);
            VerifyChrome(window);
            VerifyControls(window, vm);
            VerifySwitch(window, vm);
            VerifyIdentity(window);
            vm.FocusedItem = vm.Items[4];
            vm.PreviewSizeCommand.Execute("48");
            Settle(window);
            VerifyWorkspace(window, new Size(1220, 800));
            Capture(window, Path.Combine(output, "workspace-default.png"));
            VerifySelectionIndependence(window, vm, output);
            vm.FocusedItem = vm.Items[0];
            foreach (var size in new[] { new Size(960, 680), new Size(1046, 794), new Size(1280, 820) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                vm.PreviewSizeCommand.Execute("96");
                var details = Named<Expander>(window, "DetailExpander");
                details.IsExpanded = false;
                Settle(window);
                VerifyWorkspace(window, size);
                Capture(window, Path.Combine(output, $"workspace-{size.Width:0}x{size.Height:0}.png"));
                details.IsExpanded = true;
                Settle(window);
                VerifyWorkspace(window, size);
                Capture(window, Path.Combine(output, $"details-{size.Width:0}x{size.Height:0}.png"));
                if (size.Width == 960)
                {
                    vm.BackdropIndex = (int)BackdropStyle.Gradient;
                    Complete(vm.WaitForIdleAsync());
                    Settle(window);
                    VerifyWorkspace(window, size);
                    VisibleWithin(Named<TextBox>(window, "ColorInput"), (FrameworkElement)window.Content);
                    Capture(window, Path.Combine(output, "manual-color-960x680.png"));
                    vm.MacPresetCommand.Execute(null);
                    Complete(vm.WaitForIdleAsync());
                    Settle(window);
                }
            }
            window.Width = 960;
            window.Height = 680;
            vm.FocusedItem = vm.Items[1];
            Settle(window);
            VerifyWorkspace(window, new Size(960, 680));
            Capture(window, Path.Combine(output, "issue-960x680.png"));
            VerifyLongRestoreNotice(window, vm, service, output);

            service.HasRestorableBatch = false;
            service.Warnings = [];
            vm.ScanCommand.Execute(null);
            Complete(vm.WaitForIdleAsync());
            Settle(window);
            Check(!vm.IsEmpty && !vm.CanRestoreDefaults && !Named<Button>(window, "RestoreDefaultsButton").IsEnabled,
                "A populated workspace without an active restore candidate must disable Restore Defaults.");
            Capture(window, Path.Combine(output, "no-restore-candidates-960x680.png"));

            service.Empty = true;
            vm.ScanCommand.Execute(null);
            Complete(vm.WaitForIdleAsync());
            Settle(window);
            Check(vm.IsEmpty, "The empty fixture must show the empty workspace.");
            Check(Descendants((FrameworkElement)window.Content).OfType<TextBlock>().Any(x => x.IsVisible && x.Text == "选择一个图标开始预览"),
                "Empty workspace must display an explicit preview placeholder, including an unset nested binding.");
            VisibleWithin(Named<Button>(window, "ApplyButton"), (FrameworkElement)window.Content);
            Check(!Named<Button>(window, "ApplyButton").IsEnabled, "Empty workspace must disable Apply.");
            Check(!vm.CanRestoreDefaults && !Named<Button>(window, "RestoreDefaultsButton").IsEnabled,
                "A workspace without stored active operations must disable Restore Defaults.");
            Capture(window, Path.Combine(output, "empty-960x680.png"));
        }
        finally
        {
            window.Close();
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
        Check(service.WriteAttempts == 0, "Layout tests must never apply or restore shortcuts.");
        Console.WriteLine($"PASS: {assertions} layout checks; 31 synthetic icons, three window sizes, 96px previews, details, restore availability/results and empty states. Screenshots: {output}");
    }

    private static void VerifyWorkspace(MainWindow window, Size requested)
    {
        var root = (FrameworkElement)window.Content;
        Check(Math.Abs(window.ActualWidth - requested.Width) < 2 && Math.Abs(window.ActualHeight - requested.Height) < 2,
            $"Window must support {requested.Width}×{requested.Height} without minimum-size coercion.");
        foreach (string name in new[] { "IconGallery", "Inspector", "PreviewPanel", "StyleControls", "ApplyButton", "RestoreDefaultsButton" })
            VisibleWithin(Named<FrameworkElement>(window, name), root);
        var vm = (MainViewModel)window.DataContext;
        var restore = Named<Button>(window, "RestoreDefaultsButton");
        Check(ReferenceEquals(restore.Command, vm.RestoreDefaultsCommand), "Restore Defaults must remain bound to its recovery action.");
        Check(vm.CanRestoreDefaults && restore.IsEnabled, "Stored active fixture operations must enable Restore Defaults at every window size.");
        var settings = Named<FrameworkElement>(window, "StyleControls");
        foreach (var control in Descendants(settings).OfType<Control>().Where(x => x.IsVisible && (x is ComboBox or Slider or TextBox or CheckBox)))
            VisibleWithin(control, root);
        var gallery = Named<ListBox>(window, "IconGallery");
        var boxes = Enumerable.Range(0, gallery.Items.Count).Select(i => gallery.ItemContainerGenerator.ContainerFromIndex(i)).OfType<ListBoxItem>().ToArray();
        Check(boxes.Length >= 4, "Gallery must materialize a visible row.");
        double top = boxes[0].TransformToAncestor(gallery).Transform(new Point()).Y;
        int columns = boxes.Count(x => Math.Abs(x.TransformToAncestor(gallery).Transform(new Point()).Y - top) < 1);
        Check(columns >= 4, $"Gallery at {requested.Width}px must show at least four columns; actual {columns}.");
        var preview = Named<FrameworkElement>(window, "PreviewPanel");
        foreach (var image in Descendants(preview).OfType<NativePixelImage>()) VisibleWithin(image, preview);
        foreach (var section in new[] { settings, preview, gallery })
            VerifySingleLineText(section);
        var actionBounds = Bounds(Named<Button>(window, "ApplyButton"), root);
        Check(actionBounds.Top >= Bounds(settings, root).Bottom, "Apply action must stay below the basic settings.");
        var scrolling = Descendants(gallery).OfType<ScrollViewer>().First();
        if (scrolling.ScrollableHeight > 0)
        {
            var scrollbar = Descendants(scrolling).OfType<ScrollBar>().First(x => x.Orientation == Orientation.Vertical);
            Check(scrollbar.IsVisible && scrollbar.Template.FindName("PART_Track", scrollbar) is Track { Thumb: not null },
                "Overflowing gallery must have a visible vertical scrollbar with a working track/thumb template.");
        }
    }

    private static void VerifyChrome(MainWindow window)
    {
        var chrome = WindowChrome.GetWindowChrome(window);
        Check(chrome != null && chrome.CaptionHeight >= 30 && chrome.CaptionHeight <= 64, "Custom chrome must preserve a usable caption drag region.");
        Check(chrome!.ResizeBorderThickness.Left >= 4 && chrome.ResizeBorderThickness.Bottom >= 4, "Custom chrome must preserve a usable resize border.");
        var buttons = Descendants((FrameworkElement)window.Content).OfType<Button>().ToArray();
        foreach (string action in new[] { "最小化", "最大化", "关闭" })
        {
            var button = buttons.FirstOrDefault(x => AutomationProperties.GetName(x).Contains(action, StringComparison.Ordinal));
            Check(button != null, $"Window must expose an accessible {action} button.");
            Check(WindowChrome.GetIsHitTestVisibleInChrome(button!), $"{action} button must remain clickable in the caption.");
        }
        Check(window.FindName("HistoryPanel") == null && buttons.All(x => x.Content is not string label || label is not ("图标工作台" or "恢复记录")),
            "The single workspace must not retain the former page navigation or history panel.");
        var restore = Named<Button>(window, "RestoreDefaultsButton");
        VisibleWithin(restore, (FrameworkElement)window.Content);
        if (Bounds(restore, (FrameworkElement)window.Content).Top < chrome!.CaptionHeight)
            Check(WindowChrome.GetIsHitTestVisibleInChrome(restore), "Restore Defaults must be clickable when placed inside the caption.");
    }

    private static void VerifyLongRestoreNotice(MainWindow window, MainViewModel vm, LayoutService service, string output)
    {
        // Inject synthetic display results through a read-only scan warning. Recovery
        // transactions are tested separately; this host must never execute a write action.
        string result = "恢复默认结果：18 项已恢复，2 项失败，1 项文件冲突。\n" +
            string.Join("\n", Enumerable.Range(1, 18).Select(i => $"测试项目 {i:00}：文件已变化或访问被拒绝，原始备份仍保留，请核对后重试。")) +
            "\n最后一项结果：冲突文件未被覆盖，恢复信息已完整保留。";
        service.Warnings = [result];
        vm.ScanCommand.Execute(null);
        Complete(vm.WaitForIdleAsync());
        var notice = Named<Expander>(window, "NoticeExpander");
        notice.IsExpanded = true;
        Settle(window);
        VerifyWorkspace(window, new Size(960, 680));
        VisibleWithin(notice, (FrameworkElement)window.Content);
        var message = Descendants(notice).OfType<TextBlock>().FirstOrDefault(x => x.Text == result);
        Check(message != null && message.IsVisible && message.TextWrapping == TextWrapping.Wrap && message.TextTrimming == TextTrimming.None,
            "Expanded restore results must retain the entire readable, wrapped message.");
        var scroll = Descendants(notice).OfType<ScrollViewer>().FirstOrDefault(x => x.ScrollableHeight > 0);
        Check(scroll != null, "A long restore result must provide its own scrolling area.");
        Check(scroll!.ViewportHeight >= 48, "Expanded restore results must leave enough space to read several lines at once.");
        scroll!.ScrollToEnd();
        Settle(window);
        Check(Math.Abs(scroll.VerticalOffset - scroll.ScrollableHeight) < 1.5,
            "The final restore result must remain reachable by scrolling.");
        Capture(window, Path.Combine(output, "restore-results-960x680.png"));
        notice.IsExpanded = false;
        Settle(window);
    }

    private static void VerifyControls(MainWindow window, MainViewModel vm)
    {
        var combo = Named<ComboBox>(window, "BackdropSelector");
        Check(combo.Items.Count == 4 && combo.Items.Cast<ComboBoxItem>().All(x => x.Content is string { Length: > 0 }), "Backdrop choices must retain their readable labels, including the macOS style.");
        Check(combo.Template.FindName("PART_Popup", combo) is Popup { Child: not null }, "Backdrop combo must have an actual dropdown popup.");
        string currentChoice = (string)((ComboBoxItem)combo.SelectedItem).Content;
        Check(Descendants(combo).OfType<TextBlock>().Any(x => x.Text == currentChoice), "Collapsed backdrop selector must display its current choice.");
        foreach (string name in new[] { "ScaleSlider", "RadiusSlider" })
        {
            var slider = Named<Slider>(window, name);
            Check(slider.Template.FindName("PART_Track", slider) is Track { Thumb: not null }, $"{name} must retain its draggable track/thumb.");
        }
        vm.BackdropIndex = (int)BackdropStyle.MacOS;
        Complete(vm.WaitForIdleAsync());
        Settle(window);
        var color = Named<TextBox>(window, "ColorInput");
        Check(!color.IsVisible && Descendants(Named<FrameworkElement>(window, "StyleControls")).OfType<TextBlock>()
            .Any(x => x.IsVisible && x.Text == "跟随应用配色"), "Automatic palette mode must explain its color choice and hide the manual hex input.");
        vm.BackdropIndex = (int)BackdropStyle.Gradient;
        Complete(vm.WaitForIdleAsync());
        Settle(window);
        VisibleWithin(color, (FrameworkElement)window.Content);
        var host = (ScrollViewer)color.Template.FindName("PART_ContentHost", color);
        var textSurface = Descendants(host).OfType<FrameworkElement>().First(x => x.GetType().Name == "TextBoxView");
        Check(textSurface.ActualHeight >= color.FontSize, "Color input must leave enough vertical space for its text, without duplicate padding.");
        VisibleWithin(textSurface, color);
        vm.MacPresetCommand.Execute(null);
        Complete(vm.WaitForIdleAsync());
        Settle(window);
    }

    private static void VerifyIdentity(MainWindow window)
    {
        Check(window.Title.StartsWith("Forma", StringComparison.Ordinal), "Window title must identify the renamed product.");
        var root = (FrameworkElement)window.Content;
        var mark = Descendants(root).OfType<FrameworkElement>().FirstOrDefault(x => x.GetType().Name == "BrandMark");
        Check(mark != null, "The title bar must use the code-drawn brand mark.");
        VisibleWithin(mark!, root);
        Check(Descendants(root).OfType<TextBlock>().Any(x => x.IsVisible && x.Text == "Forma"), "The visible title bar must show the new product name.");

        string assembly = typeof(MainWindow).Assembly.GetName().Name!;
        var resource = Application.GetResourceStream(new Uri($"pack://application:,,,/{assembly};component/Assets/IconAdapt.ico"))
            ?? throw new InvalidOperationException("Application icon resource is missing.");
        using var stream = resource.Stream;
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        foreach (int size in new[] { 16, 20, 24, 32, 48, 64, 128, 256 })
            Check(decoder.Frames.Any(x => x.PixelWidth == size && x.PixelHeight == size), $"Application icon must carry its own {size}px frame.");
    }

    private static void VerifySwitch(MainWindow window, MainViewModel vm)
    {
        var toggle = Named<CheckBox>(window, "CropCheckBox");
        Check(toggle.Focusable && toggle.IsTabStop, "The crop switch must remain reachable by keyboard.");
        Check(!string.IsNullOrWhiteSpace(AutomationProperties.GetName(toggle)) || toggle.Content is string { Length: > 0 },
            "The crop switch must expose a readable accessible label.");
        var track = toggle.Template.FindName("SwitchTrack", toggle) as FrameworkElement;
        var knob = toggle.Template.FindName("SwitchKnob", toggle) as FrameworkElement;
        Check(track != null && knob != null, "The switch must render both its track and thumb.");
        VisibleWithin(track!, toggle);
        VisibleWithin(knob!, toggle);
        bool original = vm.CropTransparent;
        var initial = Bounds(knob!, toggle);
        var peer = UIElementAutomationPeer.CreatePeerForElement(toggle);
        var provider = peer?.GetPattern(PatternInterface.Toggle) as IToggleProvider;
        Check(provider != null, "The styled switch must preserve the accessible toggle pattern.");
        provider!.Toggle();
        Settle(window);
        Check(vm.CropTransparent != original && toggle.IsChecked == vm.CropTransparent,
            "Toggling through accessibility must update the actual crop setting.");
        var changed = Bounds(knob!, toggle);
        Check(Math.Abs(initial.Left - changed.Left) > 3, "The switch thumb must visibly distinguish on and off states.");
        provider.Toggle();
        Complete(vm.WaitForIdleAsync());
        Settle(window);
        Check(vm.CropTransparent == original, "The switch check must restore the fixture's original setting.");
    }

    private static void VerifySelectionIndependence(MainWindow window, MainViewModel vm, string output)
    {
        var gallery = Named<ListBox>(window, "IconGallery");
        var focused = vm.FocusedItem!;
        bool original = focused.IsSelected;
        focused.IsSelected = false;
        Settle(window);
        Check(ReferenceEquals(gallery.SelectedItem, focused) && ReferenceEquals(vm.FocusedItem, focused),
            "Unchecking a focused icon must keep its preview focus independent of batch selection.");
        Capture(window, Path.Combine(output, "selection-focus-default.png"));
        focused.IsSelected = original;
        Settle(window);
    }

    private static void VerifySingleLineText(FrameworkElement section)
    {
        foreach (var text in Descendants(section).OfType<TextBlock>().Where(x => x.IsVisible &&
            !string.IsNullOrWhiteSpace(x.Text) && x.TextWrapping == TextWrapping.NoWrap))
        {
            var sample = new FormattedText(text.Text, CultureInfo.CurrentUICulture, text.FlowDirection,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize,
                text.Foreground, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Check(text.ActualHeight + 1 >= sample.Height,
                $"Text '{text.Text}' must have room for its full line at {text.FontSize}px: actual {text.ActualHeight:0.##}, required {sample.Height:0.##}.");
        }
    }

    private static T Named<T>(FrameworkElement root, string name) where T : FrameworkElement =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing layout element: {name} ({typeof(T).Name}).");

    private static void VisibleWithin(FrameworkElement element, FrameworkElement root)
    {
        Check(element.IsVisible && element.ActualWidth > 0 && element.ActualHeight > 0, $"{element.Name}/{element.GetType().Name} must be visible.");
        var rect = Bounds(element, root);
        const double tolerance = 1.5;
        Check(rect.Left >= -tolerance && rect.Top >= -tolerance && rect.Right <= root.ActualWidth + tolerance && rect.Bottom <= root.ActualHeight + tolerance,
            $"{element.Name}/{element.GetType().Name} is clipped: {rect}, available {root.ActualWidth}×{root.ActualHeight}.");
        for (DependencyObject? parent = VisualTreeHelper.GetParent(element); parent != null && parent != root; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is not FrameworkElement clip || (!clip.ClipToBounds && clip is not ScrollContentPresenter)) continue;
            var relative = Bounds(element, clip);
            Check(relative.Left >= -tolerance && relative.Top >= -tolerance && relative.Right <= clip.ActualWidth + tolerance && relative.Bottom <= clip.ActualHeight + tolerance,
                $"{element.Name}/{element.GetType().Name} is outside clipping ancestor {clip.GetType().Name}.");
        }
    }

    private static Rect Bounds(FrameworkElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(new Point(), element.RenderSize));

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var item in Descendants(child)) yield return item;
        }
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using var registration = timeout.Token.Register(() => dispatcher.BeginInvoke(() => frame.Continue = false));
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            if (!task.IsCompleted) throw new TimeoutException("Fixture view model did not become idle.");
        }
        task.GetAwaiter().GetResult();
    }

    private static void Capture(Window window, string path)
    {
        var root = (FrameworkElement)window.Content;
        var dpi = VisualTreeHelper.GetDpi(root);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class LayoutRenderer : IIconRenderer
    {
        private readonly Dictionary<(string Png, IconStyle Style), RenderResult> cache = [];
        public RenderResult Render(byte[] png, IconStyle style)
        {
            var key = (Convert.ToBase64String(png), style);
            if (!cache.TryGetValue(key, out var value)) cache[key] = value = new IconRenderer().Render(png, style);
            return value;
        }
    }

    private sealed class LayoutService : IShortcutService
    {
        private readonly IReadOnlyList<ShortcutInfo> fixtures;
        private readonly OperationBatch batch;
        public bool Empty { get; set; }
        public bool HasRestorableBatch { get; set; } = true;
        public IReadOnlyList<string> Warnings { get; set; } = [];
        public int WriteAttempts { get; private set; }
        public LayoutService(string root)
        {
            string[] names = ["Windows Terminal", "备忘录", "相机", "音乐", "文件管理器", "Visual Studio Code", "阅读器", "日历", "邮件", "PyCharm 2026.2", "计算器", "一个很长的应用名称用于检查文字省略与紧凑布局"];
            fixtures = Enumerable.Range(0, 31).Select(i =>
            {
                int size = i == 0 ? 32 : 128;
                byte[] png = Icon(i % 6, size);
                return new ShortcutInfo(Path.Combine(root, $"{i:00}-{names[i % names.Length]}.lnk"), names[i % names.Length], "", "", 0, "", png, i != 7, i == 7 ? "目标程序不存在，测试项目无法适配。" : null)
                { SourcePixelWidth = size, SourcePixelHeight = size, SourceFrames = [new IconFrame(size, size, png)] };
            }).ToArray();
            batch = new OperationBatch
            {
                Id = "synthetic-layout-batch", CreatedAt = new DateTimeOffset(2026, 10, 24, 12, 30, 0, TimeSpan.FromHours(8)),
                Items = fixtures.Take(12).Select((item, i) => new OperationItem
                {
                    ShortcutPath = item.Path,
                    State = i == 1 ? "failed" : i == 2 ? "pending" : "applied",
                    Error = i == 1 ? "拒绝访问。该测试位置没有写入权限。" : i == 2 ? "测试操作中断，请先核对记录。" : null
                }).ToList()
            };
        }
        public void Dispose() { }
        public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default) => Task.FromResult(new ScanResult(Empty ? [] : fixtures, Warnings));
        public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OperationBatch>>(Empty || !HasRestorableBatch ? [] : [batch]);
        public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Layout tests cannot write shortcuts."); }
        public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Layout tests cannot restore shortcuts."); }
        public Task<RestoreDefaultsResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default) { WriteAttempts++; throw new InvalidOperationException("Layout tests cannot restore default icons."); }
        private static byte[] Icon(int index, int size)
        {
            Color[] colors = [Color.FromRgb(44, 61, 80), Color.FromRgb(221, 161, 58), Color.FromRgb(95, 143, 183), Color.FromRgb(210, 96, 116), Color.FromRgb(82, 158, 139), Color.FromRgb(134, 111, 195)];
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushTransform(new ScaleTransform(size / 128d, size / 128d));
                dc.DrawRoundedRectangle(new SolidColorBrush(colors[index]), null, new Rect(8, 8, 112, 112), 24, 24);
                var pen = new Pen(Brushes.White, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                if (index == 0) { dc.DrawLine(pen, new Point(36, 42), new Point(54, 60)); dc.DrawLine(pen, new Point(54, 60), new Point(36, 78)); dc.DrawLine(pen, new Point(65, 81), new Point(91, 81)); }
                else if (index == 1) { for (int y = 42; y <= 86; y += 22) dc.DrawLine(pen, new Point(36, y), new Point(92, y)); }
                else if (index == 2) { dc.DrawRoundedRectangle(null, pen, new Rect(30, 39, 68, 52), 9, 9); dc.DrawEllipse(null, pen, new Point(64, 65), 15, 15); }
                else if (index == 3) { dc.DrawLine(pen, new Point(76, 35), new Point(76, 84)); dc.DrawLine(pen, new Point(76, 35), new Point(96, 42)); dc.DrawEllipse(Brushes.White, null, new Point(62, 85), 17, 11); }
                else if (index == 4) { dc.DrawRoundedRectangle(Brushes.White, null, new Rect(31, 42, 66, 49), 5, 5); dc.DrawRoundedRectangle(Brushes.White, null, new Rect(31, 33, 30, 25), 5, 5); }
                else { dc.DrawLine(pen, new Point(38, 91), new Point(64, 35)); dc.DrawLine(pen, new Point(64, 35), new Point(91, 91)); dc.DrawLine(pen, new Point(48, 73), new Point(80, 73)); }
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
