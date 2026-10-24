namespace IconAdapt.Core;

public sealed record ShortcutInfo(
    string Path,
    string Name,
    string TargetPath,
    string IconPath,
    int IconIndex,
    string FileHash,
    byte[]? OriginalIconPng,
    bool IsSupported,
    string? Error)
{
    public int SourcePixelWidth { get; init; }
    public int SourcePixelHeight { get; init; }
    public string? QualityWarning { get; init; }
    public IReadOnlyList<IconFrame> SourceFrames { get; init; } = [];
}

public sealed record IconFrame(int PixelWidth, int PixelHeight, byte[] Png);

public sealed record ScanResult(IReadOnlyList<ShortcutInfo> Items, IReadOnlyList<string> Warnings);

public sealed record IconChange(string ShortcutPath, string ExpectedHash, byte[] IconFile);

public sealed class OperationItem
{
    public string ShortcutPath { get; set; } = "";
    public string OriginalIconPath { get; set; } = "";
    public int OriginalIconIndex { get; set; }
    public string OriginalHash { get; set; } = "";
    public string? AppliedHash { get; set; }
    public string BackupPath { get; set; } = "";
    public string GeneratedIconPath { get; set; } = "";
    public string State { get; set; } = "pending";
    public string? Error { get; set; }
}

public sealed class OperationBatch
{
    public int SchemaVersion { get; set; } = 1;
    public string Id { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<OperationItem> Items { get; set; } = [];
}

public interface IShortcutService : IDisposable
{
    Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default);
    Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default);
    Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default);
}

public enum BackdropStyle { Gradient, Solid, None }

public sealed record IconStyle
{
    public BackdropStyle Backdrop { get; init; } = BackdropStyle.Gradient;
    public string BackgroundColor { get; init; } = "#7772EE";
    public double ContentScale { get; init; } = 0.68;
    public double CornerRadius { get; init; } = 0.23;
    public bool CropTransparent { get; init; } = true;
}

public sealed record RenderResult(byte[] PreviewPng, byte[] IconFile)
{
    public IReadOnlyList<string> Notes { get; init; } = [];
}

public interface IIconRenderer
{
    RenderResult Render(byte[] sourcePng, IconStyle style);
    RenderResult Render(byte[] sourcePng, IconStyle style, IReadOnlyList<IconFrame> nativeFrames)
        => Render(sourcePng, style);
}
