using System.IO;
using System.Windows.Media.Imaging;
using IconAdapt.Core;
using IconAdapt.App.Diagnostics;

namespace IconAdapt.App.ViewModels;

public sealed class ShortcutItem : ObservableObject
{
    public ShortcutItem(ShortcutInfo info)
    {
        Info = info;
        Original = Decode(info.OriginalIconPng);
        selected = info.IsSupported && Original != null;
        status = info.IsSupported ? "等待预览" : "已跳过";
        detail = info.Error ?? "";
    }

    public ShortcutInfo Info { get; set; }
    public string Name => Info.Name;
    public string Path => Info.Path;
    public bool CanSelect => Info.IsSupported && Original != null;
    public BitmapSource? Original { get; }
    private BitmapSource? preview;
    public BitmapSource? Preview { get => preview; set { if (Set(ref preview, value)) Changed(nameof(DisplayImage)); } }
    public BitmapSource? DisplayImage => Preview ?? Original;
    public RenderResult? Rendered { get; set; }
    private bool selected;
    public bool IsSelected { get => selected; set => Set(ref selected, value && CanSelect); }
    private string status;
    public string Status { get => status; set { if (Set(ref status, value)) Changed(nameof(Diagnostic)); } }
    private string detail;
    public string Detail { get => detail; set { if (Set(ref detail, value)) Changed(nameof(Diagnostic)); } }
    public string Diagnostic => string.IsNullOrWhiteSpace(Detail) ? "" : OperationFeedback.ItemAdvice(Detail, Path);

    public static BitmapSource? Decode(byte[]? png)
    {
        if (png is not { Length: > 0 }) return null;
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

public sealed class BatchViewModel(OperationBatch batch)
{
    public OperationBatch Batch { get; } = batch;
    public string Id => Batch.Id;
    public string Title => Batch.CreatedAt.ToLocalTime().ToString("MM月dd日  HH:mm:ss");
    public string Summary => OperationFeedback.BatchSummary(Batch);
    public string SummaryForeground => Batch.Items.Any(x => x.State is "failed" or "pending" or "restoring" or "conflict") ? "#B46C5A" : "#7A8194";
    public IEnumerable<BatchItemViewModel> Items => Batch.Items.Select(x => new BatchItemViewModel(x));
    public bool CanRestore => Batch.Items.Any(x => x.State is "applied" or "pending" or "restoring" or "conflict");
}

public sealed class BatchItemViewModel(OperationItem item)
{
    public string Name => System.IO.Path.GetFileNameWithoutExtension(item.ShortcutPath);
    public string Detail => item.Error ?? item.ShortcutPath;
    public string Diagnostic => OperationFeedback.ItemAdvice(item.Error, item.ShortcutPath, item.State);
    public string Status => DisplayState(item.State);
    public static string DisplayState(string state) => state switch
    {
        "applied" => "已应用", "restored" => "已恢复", "failed" => "应用失败", "pending" => "待核对 / 可恢复",
        "restoring" => "待核对 / 可恢复", "queued" => "尚未开始", "cancelled" => "已取消",
        "restore_failed" => "恢复失败", "conflict" => "文件已变化", _ => state
    };
}
