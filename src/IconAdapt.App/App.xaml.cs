using System.IO;
using System.Windows;
using System.Windows.Threading;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;
using IconAdapt.Imaging;
using IconAdapt.Windows;

namespace IconAdapt.App;

public partial class App : Application
{
    private IShortcutService? service;
    private MainViewModel? viewModel;
    private string? smokeOutput;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;
        try
        {
            string? scanFolder = null;
            string? dataRoot = null;
            for (var index = 0; index < e.Args.Length; index++)
            {
                var arg = e.Args[index];
                if (arg is not ("--scan-folder" or "--data-root" or "--smoke-ui")) throw new ArgumentException($"未知启动选项：{arg}");
                if (++index >= e.Args.Length) throw new ArgumentException($"{arg} 后需要提供路径。");
                var path = Path.GetFullPath(e.Args[index]);
                switch (arg)
                {
                    case "--scan-folder": scanFolder = path; break;
                    case "--data-root": dataRoot = path; break;
                    case "--smoke-ui": smokeOutput = path; break;
                }
            }
            dataRoot ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconAdapt");
            service = new ShortcutService(dataRoot);
            viewModel = new MainViewModel(service, new IconRenderer(), scanFolder, dataRoot);
            var window = new MainWindow(viewModel, smokeOutput);
            MainWindow = window;
            window.Show();
        }
        catch (Exception ex) { ReportFatal(ex); }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        if (smokeOutput != null) { ReportFatal(e.Exception); return; }
        MessageBox.Show($"操作出现异常：{e.Exception.Message}\n\n请核对恢复记录。原始快捷方式备份会保留在本机。", "IconAdapt", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void ReportFatal(Exception ex)
    {
        if (smokeOutput != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(smokeOutput)!);
            File.WriteAllText(smokeOutput + ".error.txt", ex.ToString());
        }
        else MessageBox.Show($"IconAdapt 无法启动：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown(1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        viewModel?.Dispose();
        service?.Dispose();
        base.OnExit(e);
    }
}
