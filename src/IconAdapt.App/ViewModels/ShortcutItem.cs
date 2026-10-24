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
        originalFallback = Decode(info.OriginalIconPng);
        foreach (var frame in info.SourceFrames)
        {
            var image = Decode(frame.Png);
            if (image != null) originalFrames[Math.Max(image.PixelWidth, image.PixelHeight)] = image;
        }
        selected = info.IsSupported && Original != null;
        status = info.IsSupported ? "等待预览" : "已跳过";
        detail = info.Error ?? "";
    }

    public ShortcutInfo Info { get; set; }
    public string Name => Info.Name;
    public string Path => Info.Path;
    public bool CanSelect => Info.IsSupported && Original != null;
    private readonly BitmapSource? originalFallback;
    private readonly Dictionary<int, BitmapSource> originalFrames = [];
    public BitmapSource? Original => OriginalFrame(previewPixelSize);
    private IReadOnlyDictionary<int, BitmapSource> outputFrames = new Dictionary<int, BitmapSource>();
    private int previewPixelSize = 48;
    public BitmapSource? Preview => Frame(previewPixelSize);
    public BitmapSource? DisplayImage => Frame(48) ?? OriginalFrame(48);
    public bool HasExactPreviewFrame => outputFrames.ContainsKey(previewPixelSize);
    public string PreviewHint => Rendered == null ? "请选择图标，等待实际尺寸预览。" : HasExactPreviewFrame
        ? $"输出的 {previewPixelSize}px 图标，按屏幕像素 1:1 显示；原图不会放大。"
        : $"输出暂缺 {previewPixelSize}px 版本，当前为缩放预览。";
    public int SourceWidth => Info.SourcePixelWidth > 0 ? Info.SourcePixelWidth : originalFallback?.PixelWidth ?? 0;
    public int SourceHeight => Info.SourcePixelHeight > 0 ? Info.SourcePixelHeight : originalFallback?.PixelHeight ?? 0;
    public string SourceSizeLabel => Original != null ? $"原图 {Original.PixelWidth}×{Original.PixelHeight}" : "原图";
    public bool HasSmallSource => SourceWidth > 0 && Math.Min(SourceWidth, SourceHeight) < 64;
    public string SourceQualityNotice => !string.IsNullOrWhiteSpace(Info.QualityWarning) ? Info.QualityWarning :
        HasSmallSource ? "原图较小，放大无法补回细节。建议先以 32 或 48px 检查效果。" : "";
    public bool HasRenderNotes => Rendered?.Notes.Count > 0;
    public string RenderNotes => Rendered == null ? "" : string.Join("\n", Rendered.Notes.Distinct());
    public RenderResult? Rendered { get; private set; }

    public void SetRendered(RenderResult? result, IReadOnlyDictionary<int, BitmapSource>? frames = null)
    {
        Rendered = result;
        outputFrames = frames ?? new Dictionary<int, BitmapSource>();
        Changed(nameof(Preview)); Changed(nameof(DisplayImage)); Changed(nameof(HasExactPreviewFrame)); Changed(nameof(PreviewHint));
        Changed(nameof(SourceQualityNotice));
        Changed(nameof(HasRenderNotes)); Changed(nameof(RenderNotes));
    }
    public void SetPreviewSize(int pixels)
    {
        if (previewPixelSize == pixels) return;
        previewPixelSize = pixels;
        Changed(nameof(Preview)); Changed(nameof(HasExactPreviewFrame)); Changed(nameof(PreviewHint));
        Changed(nameof(Original)); Changed(nameof(SourceSizeLabel));
    }
    private BitmapSource? Frame(int pixels) => outputFrames.TryGetValue(pixels, out var exact) ? exact :
        outputFrames.OrderBy(x => x.Key >= pixels ? x.Key - pixels : 10000 + pixels - x.Key).FirstOrDefault().Value;
    private BitmapSource? OriginalFrame(int pixels) => originalFrames.TryGetValue(pixels, out var exact) ? exact :
        originalFrames.OrderBy(x => x.Key >= pixels ? x.Key - pixels : 10000 + pixels - x.Key).FirstOrDefault().Value ?? originalFallback;
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
