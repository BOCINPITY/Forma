using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IconAdapt.App.Controls;
using IconAdapt.App.Imaging;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;
using IconAdapt.Imaging;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Contains("--motion", StringComparer.OrdinalIgnoreCase)) MotionChecks.Run();
            else Run();
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void Run()
    {
        var source = BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null,
            Enumerable.Range(0, 32 * 32).SelectMany(i => new byte[]
            { (byte)((i % 32 + i / 32) % 2 == 0 ? 0 : 255), 96, 192, 255 }).ToArray(), 32 * 4);
        source.Freeze();
        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(stream);
        byte[] png = stream.ToArray();
        var renderer = new CountingRenderer();
        var frames = OutputIconFrames.Decode(renderer.Render(png, new IconStyle()).IconFile);
        Check(new[] { 32, 48, 64, 96 }.All(frames.ContainsKey), "Actual exported preview sizes must exist.");

        var actual = Draw(new NativePixelImage { Source = frames[48], PixelSize = 48 }, 48);
        var expected = new FormatConvertedBitmap(frames[48], PixelFormats.Pbgra32, null, 0);
        var expectedPixels = new byte[48 * 48 * 4];
        expected.CopyPixels(expectedPixels, 48 * 4, 0);
        Check(actual.SequenceEqual(expectedPixels), "48px preview must display the exact exported pixels without another resampling pass.");
        var original = Draw(new NativePixelImage { Source = source, PixelSize = 48, AllowUpscale = false }, 48);
        int occupiedWidth = Enumerable.Range(0, 48).Count(x => Enumerable.Range(0, 48).Any(y => original[(y * 48 + x) * 4 + 3] > 0));
        Check(occupiedWidth == 32, "32px original must not be enlarged inside a 48px comparison frame.");

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "IconAdapt.slnx"))) directory = directory.Parent;
        var root = Path.Combine(directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found."),
            ".local", "app-smoke", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "settings.json"), System.Text.Json.JsonSerializer.Serialize(
            new IconStyle { ContentScale = .5722, CropTransparent = false }));
        using var vm = new MainViewModel(new FakeService(png, root), renderer, root, root);
        vm.InitializeAsync().GetAwaiter().GetResult();
        Check(vm.ContentScale == .5722 && !vm.CropTransparent && vm.DetailLimitNotice.Length > 0,
            "Existing settings must be preserved and their detail limitation explained.");
        Check(renderer.LastNativeCount == 1, "View model must pass native source frames to the renderer.");
        int calls = renderer.Calls;
        foreach (string size in new[] { "32", "48", "64", "96" })
        {
            vm.PreviewSizeCommand.Execute(size);
            Check(vm.PreviewPixelSize == int.Parse(size), "Size selector must update the physical pixel comparison.");
            Check(vm.FocusedItem!.Preview!.PixelWidth == int.Parse(size), "Size selector must select the exported frame rather than resize the large preview.");
        }
        Check(renderer.Calls == calls, "Changing preview size must not regenerate output icons.");
        Check(vm.FocusedItem!.HasSmallSource && vm.FocusedItem.SourceWidth == 32, "Small native sources must be identified.");
        vm.ClarityPresetCommand.Execute(null);
        vm.WaitForIdleAsync().GetAwaiter().GetResult();
        Check(vm.CropTransparent && vm.ContentScale == .8, "Only the explicit clarity action should change padding and content size.");
        var item = vm.FocusedItem!;
        foreach (var state in new[] { "可应用", "已应用", "已恢复", "已取消" })
        {
            item.Status = state;
            Check(!item.HasIssue && item.IssueSummary.Length == 0, "Successful outcomes and small source warnings must not appear as errors.");
        }
        var notifications = new List<string?>();
        vm.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        foreach (var state in new[] { "预览失败", "应用失败", "恢复失败", "待核对 / 可恢复", "文件已变化" })
        {
            item.Status = state;
            Check(item.HasIssue && item.IssueSummary.Length > 0 && vm.NoticeHasIssue,
                "Failed and uncertain outcomes must keep visible, actionable summaries.");
        }
        item.Detail = "Access is denied";
        Check(item.IssueSummary.Contains("权限"), "Access failures must retain permission guidance.");
        item.Status = "已恢复";
        item.Detail = "恢复失败或文件冲突时，备份及操作记录仍保留。";
        Check(!item.HasIssue && !vm.NoticeHasIssue && item.Diagnostic.Length > 0,
            "Conditional recovery guidance must stay available without making a successful item look like an error.");
        Check(notifications.Contains(nameof(vm.NoticeHasIssue)) && notifications.Contains(nameof(vm.NoticeSummary)),
            "Changing an item's outcome must update the global notice bindings.");
        Check(vm.BackdropIndex == (int)BackdropStyle.Gradient, "Existing saved styles must stay unchanged until a preset is explicitly chosen.");
        vm.BackgroundColor = "invalid";
        vm.MacPresetCommand.Execute(null);
        vm.WaitForIdleAsync().GetAwaiter().GetResult();
        Check(vm.IsMacStyle && !vm.CanChooseColor && vm.ContentScale == .8 && vm.CornerRadius == .23 && vm.CropTransparent,
            "The explicit macOS preset must select automatic color and readable material proportions.");
        Check(vm.StyleError.Length == 0 && vm.ApplyCommand.CanExecute(null),
            "An old invalid manual color must not trap the automatic-color preset in an error with a hidden editor.");
        using (var reloaded = new MainViewModel(new FakeService(png, root), renderer, root, root))
            Check(reloaded.IsMacStyle, "The macOS style must survive a settings reload without being clamped to the old enum range.");
        using (var fresh = new MainViewModel(new FakeService(png, root), renderer, root, Path.Combine(root, "fresh")))
            Check(fresh.IsMacStyle && fresh.ContentScale == .8, "A fresh installation must start with the new macOS style.");
        Console.WriteLine("PASS: WPF exported-pixel equality, native-source size, frame switching, source propagation and explicit clarity preset.");
        RestoreDefaultsVmChecks.Run(png, renderer, root);
        LayoutChecks.Run();
        MotionChecks.Run();
    }

    private static byte[] Draw(NativePixelImage control, int size)
    {
        control.Measure(new Size(size, size));
        control.Arrange(new Rect(0, 0, size, size));
        control.UpdateLayout();
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(control);
        var pixels = new byte[size * size * 4];
        bitmap.CopyPixels(pixels, size * 4, 0);
        return pixels;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}

internal sealed class CountingRenderer : IIconRenderer
{
    public int Calls;
    public int LastNativeCount;
    public RenderResult Render(byte[] png, IconStyle style) => Render(png, style, []);
    public RenderResult Render(byte[] png, IconStyle style, IReadOnlyList<IconFrame> frames)
    {
        LastNativeCount = frames.Count;
        Interlocked.Increment(ref Calls);
        return new IconRenderer().Render(png, style, frames);
    }
}

internal sealed class FakeService(byte[] png, string root) : IShortcutService
{
    public void Dispose() { }
    public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(new ScanResult([new ShortcutInfo(Path.Combine(root, "fixture.lnk"), "Small source fixture", "", "", 0, "", png, true, null)
        { SourcePixelWidth = 32, SourcePixelHeight = 32, SourceFrames = [new IconFrame(32, 32, png)] }], []));
    public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<OperationBatch>>([]);
    public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
