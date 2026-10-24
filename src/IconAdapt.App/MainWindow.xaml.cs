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
    private void ShowNoticeDetails(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, viewModel.StatusDetail + "\n\n" + viewModel.NoticeDetail,
            "操作详情", MessageBoxButton.OK, MessageBoxImage.Information);

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await viewModel.InitializeAsync();
        if (smokeOutput == null) return;
        try
        {
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
