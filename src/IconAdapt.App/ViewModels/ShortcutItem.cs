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
    public BitmapSource? DesktopImage => Preview ?? Original;
    public string DesktopStatus => Preview != null ? "" : "未生成，显示原图";
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
    // Quality limitations are informational; successful and ready items must not look like errors.
    public string QualitySummary => HasSmallSource ? "原图清晰度有限，适合小尺寸" :
        !string.IsNullOrWhiteSpace(Info.QualityWarning) ? "原图来源有提示，可展开查看" :
        Rendered?.Notes.Any(x => x.Contains("裁切", StringComparison.Ordinal)) == true ? "图案边缘可能被裁切" :
        HasRenderNotes ? "有画质提示，可展开查看" : "";
    public RenderResult? Rendered { get; private set; }

    public void SetRendered(RenderResult? result, IReadOnlyDictionary<int, BitmapSource>? frames = null)
    {
        Rendered = result;
        outputFrames = frames ?? new Dictionary<int, BitmapSource>();
        Changed(nameof(Preview)); Changed(nameof(DisplayImage)); Changed(nameof(HasExactPreviewFrame)); Changed(nameof(PreviewHint));
        Changed(nameof(DesktopImage)); Changed(nameof(DesktopStatus));
        Changed(nameof(SourceQualityNotice));
        Changed(nameof(HasRenderNotes)); Changed(nameof(RenderNotes));
        Changed(nameof(QualitySummary));
    }
    public void SetPreviewSize(int pixels)
    {
        if (previewPixelSize == pixels) return;
        previewPixelSize = pixels;
        Changed(nameof(Preview)); Changed(nameof(HasExactPreviewFrame)); Changed(nameof(PreviewHint));
        Changed(nameof(Original)); Changed(nameof(SourceSizeLabel));
        Changed(nameof(DesktopImage)); Changed(nameof(DesktopStatus));
    }
    private BitmapSource? Frame(int pixels) => outputFrames.TryGetValue(pixels, out var exact) ? exact :
        outputFrames.OrderBy(x => x.Key >= pixels ? x.Key - pixels : 10000 + pixels - x.Key).FirstOrDefault().Value;
    private BitmapSource? OriginalFrame(int pixels) => originalFrames.TryGetValue(pixels, out var exact) ? exact :
        originalFrames.OrderBy(x => x.Key >= pixels ? x.Key - pixels : 10000 + pixels - x.Key).FirstOrDefault().Value ?? originalFallback;
    private bool selected;
    public bool IsSelected { get => selected; set => Set(ref selected, value && CanSelect); }
    private string status;
    public string Status { get => status; set { if (Set(ref status, value)) RefreshIssue(); } }
    private string detail;
    public string Detail { get => detail; set { if (Set(ref detail, value)) RefreshIssue(); } }
    public string Diagnostic => string.IsNullOrWhiteSpace(Detail) ? "" : OperationFeedback.ItemAdvice(Detail, Path);
    public bool HasIssue => !CanSelect || Status is "预览失败" or "应用失败" or "恢复失败" or "待核对 / 可恢复" or "文件已变化";
    public string IssueSummary
    {
        get
        {
            if (!HasIssue) return "";
            if (OperationFeedback.IsAccessDenied(Detail)) return "权限不足，请检查访问权限";
            if (OperationFeedback.IsProtectionAbort(Detail)) return "属性校验未通过，请先核对";
            if (Status == "预览失败") return "预览生成失败，请查看详情";
            if (Status == "待核对 / 可恢复") return "状态待核对，可从记录恢复";
            if (Status == "文件已变化") return "文件已变化，当前内容已保留";
            if (Status is "应用失败" or "恢复失败") return "操作未完成，请查看详情";
            if (Detail.Contains("目标", StringComparison.Ordinal) &&
                (Detail.Contains("不存在", StringComparison.Ordinal) || Detail.Contains("不可访问", StringComparison.Ordinal)))
                return "目标程序暂时不可用";
            return "暂时无法适配此快捷方式";
        }
    }
    private void RefreshIssue()
    {
        Changed(nameof(Diagnostic)); Changed(nameof(HasIssue)); Changed(nameof(IssueSummary));
    }

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
