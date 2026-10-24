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
    private static int Main()
    {
        try { Run(); return 0; }
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
        Console.WriteLine("PASS: WPF exported-pixel equality, native-source size, frame switching, source propagation and explicit clarity preset.");
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
