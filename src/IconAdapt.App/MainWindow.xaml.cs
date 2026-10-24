using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Shell;
using IconAdapt.App.ViewModels;

namespace IconAdapt.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel viewModel;
    private readonly string? smokeOutput;
    private bool allowClose;
    private bool closing;

    public MainWindow(MainViewModel viewModel, string? smokeOutput)
    {
        InitializeComponent();
        this.viewModel = viewModel;
        this.smokeOutput = smokeOutput;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        SourceInitialized += (_, _) => TryEnableRoundedCorners();
        StateChanged += (_, _) =>
        {
            // WindowChrome preserves native resizing; keep content inside the maximized frame.
            OuterFrame.Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
            MaximizeButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
    }

    private void TryEnableRoundedCorners()
    {
        try
        {
            // Windows 11 owns the real outer corners; older Windows versions simply ignore this hint.
            int preference = 2;
            _ = DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, 33, ref preference, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);

    private void MinimizeWindow(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);
    private void ToggleMaximizeWindow(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this);
        else SystemCommands.MaximizeWindow(this);
    }
    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    public void ShowDesktopPreview(bool show)
    {
        GalleryContent.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        DesktopPreviewSurface.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        GalleryModeButton.Background = show ? Brushes.Transparent : Brushes.White;
        DesktopModeButton.Background = show ? Brushes.White : Brushes.Transparent;
        WorkspaceHint.Text = show ? "点击图标查看右侧对比 · 在图标库调整勾选" : "点击图标查看对比 · 勾选后批量应用";
    }

    private void ShowGalleryMode(object sender, RoutedEventArgs e) => ShowDesktopPreview(false);
    private void ShowDesktopMode(object sender, RoutedEventArgs e) => ShowDesktopPreview(true);
    private void ReturnToGallery(object sender, RoutedEventArgs e)
    {
        ShowDesktopPreview(false);
        GalleryModeButton.Focus();
    }

    private void ShowNoticeDetails(object sender, RoutedEventArgs e)
    {
        NoticeExpander.IsExpanded = true;
        NoticeExpander.UpdateLayout();
        NoticeScroll.ScrollToTop();
        NoticeScroll.Focus();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.InitializeAsync();
        if (smokeOutput == null) return;
        try
        {
            var ready = viewModel.Items.Count(item => item.Rendered != null);
            if (viewModel.Items.Any(item => item.CanSelect && item.Rendered == null))
                throw new InvalidOperationException("A supported shortcut failed to render in the packaged app.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            UpdateLayout();
            var content = (FrameworkElement)Content;
            var dpi = VisualTreeHelper.GetDpi(content);
            var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(content.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            image.Render(content);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            Directory.CreateDirectory(Path.GetDirectoryName(smokeOutput)!);
            using (var output = File.Create(smokeOutput)) encoder.Save(output);
            File.WriteAllText(smokeOutput + ".json", System.Text.Json.JsonSerializer.Serialize(new
            {
                Version = viewModel.Version, Scanned = viewModel.Items.Count, Ready = ready,
                BundledRuntime = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory()
                    .StartsWith(AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase)
            }));
            allowClose = true;
            Close();
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(smokeOutput)!);
            File.WriteAllText(smokeOutput + ".error.txt", ex.ToString());
            Application.Current.Shutdown(1);
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (allowClose) return;
        if (!viewModel.IsBusy && !viewModel.IsRendering) { allowClose = true; return; }
        e.Cancel = true;
        if (closing) return;
        if (viewModel.IsBusy && MessageBox.Show("操作仍在进行。停止并退出？\n\n程序会等待当前文件写入及操作记录保存完成，再关闭窗口。", "退出 Forma", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        closing = true;
        viewModel.Cancel();
        await viewModel.WaitForIdleAsync();
        allowClose = true;
        Close();
    }
}
