using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
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
    }

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
            var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), (int)Math.Ceiling(content.ActualHeight), 96, 96, PixelFormats.Pbgra32);
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
        if (viewModel.IsBusy && MessageBox.Show("操作仍在进行。停止并退出？\n\n程序会等待当前文件写入及操作记录保存完成，再关闭窗口。", "退出 IconAdapt", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        closing = true;
        viewModel.Cancel();
        await viewModel.WaitForIdleAsync();
        allowClose = true;
        Close();
    }
}
