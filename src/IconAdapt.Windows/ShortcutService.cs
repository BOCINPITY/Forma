using System.Collections.Concurrent;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using IconAdapt.Core;

namespace IconAdapt.Windows;

/// <summary>Serial STA Shell operations with recoverable, local write-ahead journals.</summary>
public sealed class ShortcutService : IShortcutService
{
    private const int MaxItems = 10000;
    private readonly string root;
    private readonly BlockingCollection<Action> queue = new();
    private readonly Thread worker;
    private readonly JsonSerializerOptions json = new() { WriteIndented = true, MaxDepth = 16 };
    private bool disposed;

    public ShortcutService(string? storageRoot = null)
    {
        root = SafeAbsolute(storageRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IconAdapt"));
        EnsureSafe(root);
        worker = new Thread(() => { foreach (Action action in queue.GetConsumingEnumerable()) action(); })
        { IsBackground = true, Name = "IconAdapt Shell STA" };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default)
        => Run(() => Scan(directory, cancellationToken), cancellationToken);

    public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count is 0 or > MaxItems) throw new ArgumentException("请选择 1 至 10000 个快捷方式。", nameof(changes));
        // Copy caller-owned buffers before entering the background queue.
        var copy = changes.Select(c => new IconChange(ShortcutPath(c.ShortcutPath), ValidHash(c.ExpectedHash),
            c.IconFile is { Length: <= 16777216 } ? c.IconFile.ToArray() : throw new ArgumentException("ICO 文件过大或为空。"))).ToArray();
        if (copy.Select(c => c.ShortcutPath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != copy.Length)
            throw new ArgumentException("同一批次不能重复包含快捷方式。", nameof(changes));
        if (copy.Any(c => IsStoragePath(c.ShortcutPath)))
            throw new ArgumentException("不能修改 Forma 存储目录内的备份。", nameof(changes));
        return Run(() => Apply(copy, cancellationToken), cancellationToken);
    }

    public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default)
        => Run<IReadOnlyList<OperationBatch>>(() => { using var lease = Lease(); return ReadHistory(); }, cancellationToken);

    public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default)
    {
        ValidId(batchId);
        return Run(() => Restore(batchId, cancellationToken), cancellationToken);
    }

    private Task<T> Run<T>(Func<T> action, CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        token.ThrowIfCancellationRequested();
        var result = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        queue.Add(() =>
        {
            try { token.ThrowIfCancellationRequested(); result.SetResult(action()); }
            catch (OperationCanceledException) { result.SetCanceled(token); }
            catch (Exception error) { result.SetException(error); }
        });
        return result.Task;
    }

    private ScanResult Scan(string? directory, CancellationToken token)
    {
        var items = new List<ShortcutInfo>();
        var warnings = new List<string>();
        List<OperationBatch> history = [];
        try { using var lease = Lease(); history = ReadHistory(); }
        catch (Exception e) { warnings.Add("无法读取适配历史，已使用当前图标：" + e.Message); }
        string[] directories = directory is null
            ? new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory) }
            : new[] { SafeAbsolute(directory) };
        foreach (string folder in directories.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            string[] files;
            try
            {
                EnsureSafe(folder);
                files = Directory.GetFiles(folder, "*.lnk", SearchOption.TopDirectoryOnly);
            }
            catch (Exception e) { warnings.Add($"无法扫描 {folder}：{e.Message}"); continue; }
            foreach (string path in files.Order(StringComparer.CurrentCultureIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    EnsureSafe(path);
                    string hash = Hash(path);
                    var link = ShellInterop.Read(path);
                    string? unsupported = Unsupported(path, link);
                    byte[]? png = null;
                    int sourceWidth = 0, sourceHeight = 0;
                    IReadOnlyList<IconFrame> sourceFrames = [];
                    string? qualityWarning = null;
                    string? imageError = null;
                    try
                    {
                        if (unsupported is null)
                        {
                            string source = OriginalSource(path, link, history);
                            var image = ShellInterop.ExtractImage(source, source == path ? link : ShellInterop.Read(source), path);
                            png = image?.Png;
                            if (image is not null)
                            {
                                sourceWidth = image.Width;
                                sourceHeight = image.Height;
                                sourceFrames = image.Frames.Select(f => new IconFrame(f.Width, f.Height, f.Png)).ToArray();
                                qualityWarning = !image.IsNative
                                    ? "无法核实原生分辨率；原图清晰度有限，建议小尺寸使用。"
                                    : Math.Min(sourceWidth, sourceHeight) < 128
                                        ? $"原图仅有 {sourceWidth}×{sourceHeight} 像素，清晰度有限，建议小尺寸使用或保留原图。"
                                        : null;
                            }
                            if (png is null) imageError = "无法提取可用图标。";
                        }
                    }
                    catch (Exception e) { imageError = "图标提取失败：" + e.Message; }
                    // A scan is a snapshot. Never return an applicable hash for a concurrently changed link.
                    if (Hash(path) != hash) unsupported = "扫描期间快捷方式已变化，请重新扫描。";
                    items.Add(new(path, Path.GetFileNameWithoutExtension(path), link.Target, link.IconPath,
                        link.IconIndex, hash, png, unsupported is null && png is not null, unsupported ?? imageError)
                    { SourcePixelWidth = sourceWidth, SourcePixelHeight = sourceHeight, SourceFrames = sourceFrames, QualityWarning = qualityWarning });
                }
                catch (Exception e) { items.Add(new(path, Path.GetFileNameWithoutExtension(path), "", "", 0, "", null, false, e.Message)); }
            }
        }
        return new(items, warnings);
    }

    private string OriginalSource(string path, ShellInterop.LinkData current, List<OperationBatch> history)
    {
        string icon = current.IconPath;
        int iconIndex = current.IconIndex;
        string source = path;
        // Follow the icon chain, newest to oldest. A normal user icon may itself reside in our storage;
        // only a journal whose backup passes integrity checks is accepted as provenance.
        foreach (var batch in history)
        {
            foreach (var item in batch.Items)
            {
                if (!SamePath(item.ShortcutPath, path) || !SamePath(item.GeneratedIconPath, icon) || iconIndex != 0) continue;
                if (!File.Exists(item.BackupPath) || Hash(item.BackupPath) != item.OriginalHash) continue;
                var original = ShellInterop.Read(item.BackupPath);
                if (original.IconPath != item.OriginalIconPath || original.IconIndex != item.OriginalIconIndex || !current.SameLaunch(original)) continue;
                source = item.BackupPath;
                icon = original.IconPath;
                iconIndex = original.IconIndex;
            }
        }
        return source;
    }

    private OperationBatch Apply(IconChange[] changes, CancellationToken token)
    {
        using var lease = Lease();
        // A malformed existing journal must be resolved before creating new operations.
        var existing = ReadHistory();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var batch = new OperationBatch
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = existing.Count > 0 && existing[0].CreatedAt >= now ? existing[0].CreatedAt.AddTicks(1) : now
        };
        for (int i = 0; i < changes.Length; i++)
        {
            batch.Items.Add(new()
            {
                ShortcutPath = changes[i].ShortcutPath, OriginalHash = changes[i].ExpectedHash,
                BackupPath = BackupPath(batch.Id, i), GeneratedIconPath = IconPath(batch.Id, i), State = "queued"
            });
        }
        Save(batch);
        for (int i = 0; i < changes.Length; i++)
        {
            var item = batch.Items[i];
            if (token.IsCancellationRequested)
            {
                foreach (var remaining in batch.Items.Skip(i))
                {
                    remaining.State = "cancelled";
                    remaining.Error = "已取消，尚未修改。";
                }
                Save(batch);
                break;
            }
            bool prepared = false;
            try
            {
                EnsureSafe(item.ShortcutPath);
                if (Hash(item.ShortcutPath) != item.OriginalHash) throw new IOException("扫描后快捷方式已变化，请重新扫描。");
                var original = ShellInterop.Read(item.ShortcutPath);
                string? reason = Unsupported(item.ShortcutPath, original);
                if (reason is not null) throw new InvalidOperationException(reason);
                ValidateIcon(changes[i].IconFile);
                // Check write access before persisting a pending operation, without writing the source.
                using (var permission = new FileStream(item.ShortcutPath, FileMode.Open, FileAccess.ReadWrite, FileShare.Read)) { }
                Directory.CreateDirectory(Path.GetDirectoryName(item.BackupPath)!);
                EnsureSafe(item.BackupPath);
                File.Copy(item.ShortcutPath, item.BackupPath, false);
                using (var backup = new FileStream(item.BackupPath, FileMode.Open, FileAccess.Write, FileShare.Read,
                    4096, FileOptions.WriteThrough)) backup.Flush(true);
                if (Hash(item.BackupPath) != item.OriginalHash) throw new IOException("备份期间快捷方式已变化。");
                item.OriginalIconPath = original.IconPath;
                item.OriginalIconIndex = original.IconIndex;
                AtomicWrite(item.GeneratedIconPath, changes[i].IconFile);
                item.State = "pending";
                Save(batch); // Durable intent precedes the first desktop write.
                prepared = true;
                if (token.IsCancellationRequested)
                {
                    item.State = "cancelled";
                    item.Error = "已取消，备份已保存，快捷方式尚未修改。";
                    Save(batch);
                    continue;
                }
                ChangeIcon(item.ShortcutPath, item.GeneratedIconPath, 0, item.OriginalHash, original);
                item.AppliedHash = Hash(item.ShortcutPath);
                item.State = "applied";
                item.Error = null;
                Save(batch);
            }
            catch (Exception e)
            {
                // A failure after the write may leave a changed shortcut. Keep pending so recovery
                // inspects the generated icon reference; never mislabel an uncertain write as failed.
                item.State = prepared ? "pending" : "failed";
                item.Error = FormatItemError(e, item.ShortcutPath);
                Save(batch);
            }
        }
        return batch;
    }

    private OperationBatch Restore(string id, CancellationToken token)
    {
        using var lease = Lease();
        var history = ReadHistory();
        var batch = history.SingleOrDefault(b => b.Id == id) ?? throw new FileNotFoundException("找不到此批次记录。");
        foreach (var item in batch.Items.AsEnumerable().Reverse())
        {
            if (token.IsCancellationRequested) break; // Preserve already restored outcomes.
            if (item.State is "restored" or "failed" or "queued" or "cancelled") continue;
            bool recoveringRestore = item.State == "restoring";
            try
            {
                EnsureSafe(item.ShortcutPath);
                EnsureSafe(item.BackupPath);
                EnsureSafe(item.GeneratedIconPath);
                if (Hash(item.BackupPath) != item.OriginalHash) throw new IOException("原始备份校验失败，无法恢复。");
                var original = ShellInterop.Read(item.BackupPath);
                if (original.IconPath != item.OriginalIconPath || original.IconIndex != item.OriginalIconIndex)
                    throw new InvalidDataException("记录与原始备份的图标引用不匹配。");
                string currentHash = Hash(item.ShortcutPath);
                var current = ShellInterop.Read(item.ShortcutPath);
                var differences = current.DifferentLaunchFields(original);
                if (differences.Count > 0)
                    throw new IOException($"快捷方式的启动属性已被修改（{string.Join("、", differences)}），已保留当前文件。");
                // Recover the save/log crash window, including a restore that finished before Save.
                if (current.IconPath == item.OriginalIconPath && current.IconIndex == item.OriginalIconIndex)
                {
                    // Exact original bytes prove that no icon work remains, including a pending
                    // intent previously marked conflict by a transient file lock/access failure.
                    if (currentHash == item.OriginalHash || item.State == "restoring")
                    {
                        UpdateParent(history, batch, item, currentHash);
                        item.State = "restored";
                        item.Error = null;
                        Save(batch);
                        continue;
                    }
                    throw new IOException("图标引用已在程序外修改；请核对当前快捷方式。");
                }
                if (!SamePath(current.IconPath, item.GeneratedIconPath) || current.IconIndex != 0)
                    throw new IOException("存在后续适配或外部图标修改，请先恢复较新的批次。");
                if (item.AppliedHash is not null && currentHash != item.AppliedHash)
                    throw new IOException("快捷方式在适配后已变化，已保留当前文件。");
                // If a newer pending record exists it must be resolved first, even if its write
                // has not yet occurred. Otherwise its OriginalHash could describe the wrong layer.
                if (history.Any(b => b.CreatedAt > batch.CreatedAt && b.Items.Any(x => SamePath(x.ShortcutPath, item.ShortcutPath) && x.State is "pending" or "restoring")))
                    throw new IOException("此快捷方式有较新的未完成批次，请先恢复该批次。");
                item.State = "restoring";
                item.Error = null;
                Save(batch);
                recoveringRestore = true;
                ChangeIcon(item.ShortcutPath, item.OriginalIconPath, item.OriginalIconIndex, currentHash, current);
                string restoredHash = Hash(item.ShortcutPath);
                UpdateParent(history, batch, item, restoredHash);
                item.State = "restored";
                Save(batch);
            }
            catch (Exception e)
            {
                // Retain restoring if the filesystem changed before journal save; retry can reconcile it.
                item.State = recoveringRestore ? "restoring" : "conflict";
                item.Error = FormatItemError(e, item.ShortcutPath);
                Save(batch);
            }
        }
        return batch;
    }

    private void UpdateParent(List<OperationBatch> history, OperationBatch restored, OperationItem item, string hash)
    {
        // COM serializes the link again. Update only the immediate previous matching icon layer;
        // do not bless unrelated external edits or older layers which are still hidden.
        foreach (var parent in history.Where(b => b.CreatedAt < restored.CreatedAt))
        {
            var match = parent.Items.FirstOrDefault(x => SamePath(x.ShortcutPath, item.ShortcutPath) &&
                SamePath(x.GeneratedIconPath, item.OriginalIconPath) && item.OriginalIconIndex == 0 &&
                x.AppliedHash == item.OriginalHash && x.State is "applied" or "pending" or "conflict");
            if (match is null) continue;
            match.AppliedHash = hash;
            match.State = "applied";
            match.Error = null;
            Save(parent);
            break;
        }
    }

    private static void ChangeIcon(string path, string icon, int index, string expectedHash, ShellInterop.LinkData before)
    {
        EnsureSafe(path);
        string temporary = Path.Combine(Path.GetDirectoryName(path)!, ".iconadapt-" + Guid.NewGuid().ToString("N") + ".lnk");
        try
        {
            // Deny ordinary concurrent writes while preparing/replacing the file. FileShare.Delete
            // permits our atomic ReplaceFile operation; a final path hash still detects replacement.
            using var guard = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            File.Copy(path, temporary, false);
            if (Hash(temporary) != expectedHash) throw new IOException("修改前快捷方式已变化。");
            ShellInterop.WriteIcon(temporary, icon, index);
            using (var stable = new FileStream(temporary, FileMode.Open, FileAccess.Write, FileShare.Read,
                4096, FileOptions.WriteThrough)) stable.Flush(true);
            var after = ShellInterop.Read(temporary);
            var differences = before.DifferentLaunchFields(after);
            if (after.IconPath != icon) differences.Add("图标路径");
            if (after.IconIndex != index) differences.Add("图标索引");
            if (differences.Count > 0)
                throw new IOException($"Shell 未能保留快捷方式属性（{string.Join("、", differences)}），已中止修改。");
            if (Hash(path) != expectedHash) throw new IOException("保存前快捷方式已变化。");
            File.Replace(temporary, path, null); // Same-volume atomic replacement; preserve source ACL.
            ShellInterop.Notify(path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private FileStream Lease()
    {
        EnsureSafe(root);
        Directory.CreateDirectory(root);
        foreach (string folder in new[] { "operations", "icons", "backups" })
        {
            string path = Path.Combine(root, folder);
            EnsureSafe(path);
            Directory.CreateDirectory(path);
        }
        string lockPath = Path.Combine(root, "operations.lock");
        EnsureSafe(lockPath);
        try { return new(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException e) { throw new IOException("另一个 Forma 实例正在操作此记录目录，请稍后重试。", e); }
    }

    private List<OperationBatch> ReadHistory()
    {
        var result = new List<OperationBatch>();
        foreach (string file in Directory.GetFiles(Path.Combine(root, "operations"), "*.json"))
        {
            EnsureSafe(file);
            if (new FileInfo(file).Length > 32 * 1024 * 1024) throw new InvalidDataException("批次记录过大。");
            byte[] bytes = File.ReadAllBytes(file);
            ValidateJsonSchema(bytes);
            var batch = JsonSerializer.Deserialize<OperationBatch>(bytes, json) ?? throw new InvalidDataException("批次记录为空。");
            Validate(batch);
            if (!SamePath(file, BatchPath(batch.Id))) throw new InvalidDataException("批次 ID 与文件名不匹配。");
            result.Add(batch);
        }
        return result.OrderByDescending(b => b.CreatedAt).ThenByDescending(b => b.Id, StringComparer.Ordinal).ToList();
    }

    private void Save(OperationBatch batch)
    {
        Validate(batch);
        AtomicWrite(BatchPath(batch.Id), JsonSerializer.SerializeToUtf8Bytes(batch, json));
    }

    private static void ValidateJsonSchema(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
        var data = document.RootElement;
        Required(data, "SchemaVersion", "Id", "CreatedAt", "Items");
        if (!data.GetProperty("SchemaVersion").TryGetInt32(out int schema) || schema != 1 ||
            data.GetProperty("Items").ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("不支持的批次记录 schema。");
        foreach (var item in data.GetProperty("Items").EnumerateArray())
            Required(item, "ShortcutPath", "OriginalIconPath", "OriginalIconIndex", "OriginalHash", "BackupPath", "GeneratedIconPath", "State");

        static void Required(JsonElement element, params string[] names)
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("批次条目不是对象。");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
                if (!keys.Add(property.Name)) throw new InvalidDataException("批次记录含重复字段。");
            if (names.Any(name => !keys.Contains(name))) throw new InvalidDataException("批次记录缺少必需字段。");
        }
    }

    private void Validate(OperationBatch batch)
    {
        ValidId(batch.Id);
        if (batch.SchemaVersion != 1 || batch.Items is null || batch.Items.Count is 0 or > MaxItems || batch.CreatedAt == default)
            throw new InvalidDataException("不支持或损坏的批次记录。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < batch.Items.Count; i++)
        {
            var item = batch.Items[i] ?? throw new InvalidDataException("无效批次条目。");
            string shortcut = ShortcutPath(item.ShortcutPath);
            if (IsStoragePath(shortcut) || !paths.Add(shortcut) || !SamePath(item.BackupPath, BackupPath(batch.Id, i)) || !SamePath(item.GeneratedIconPath, IconPath(batch.Id, i)))
                throw new InvalidDataException("批次包含重复快捷方式或超出存储目录的路径。");
            EnsureSafe(item.BackupPath);
            EnsureSafe(item.GeneratedIconPath);
            ValidHash(item.OriginalHash);
            if (item.AppliedHash is not null) ValidHash(item.AppliedHash);
            if (item.State is not ("queued" or "pending" or "applied" or "failed" or "cancelled" or "restored" or "restoring" or "conflict"))
                throw new InvalidDataException("无效条目状态。");
            if (item.OriginalIconPath is null || item.OriginalIconPath.Length > 32767 || item.OriginalIconPath.Contains('\0'))
                throw new InvalidDataException("无效原始图标引用。");
        }
    }

    private string BatchPath(string id) => Path.Combine(root, "operations", id + ".json");
    private string BackupPath(string id, int index) => Path.Combine(root, "backups", id, index + ".lnk");
    private string IconPath(string id, int index) => Path.Combine(root, "icons", id + "-" + index + ".ico");
    private bool IsStoragePath(string path) => path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void AtomicWrite(string path, byte[] bytes)
    {
        EnsureSafe(path);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            { stream.Write(bytes); stream.Flush(true); }
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string? Unsupported(string path, ShellInterop.LinkData link)
    {
        if (ShellInterop.IsAdvertised(path)) return "暂不支持 Windows Installer advertised 快捷方式。";
        string expanded = Environment.ExpandEnvironmentVariables(link.Target);
        if (string.IsNullOrWhiteSpace(expanded) || !Path.IsPathFullyQualified(expanded)) return "暂不支持特殊 Shell 或商店应用入口。";
        if (!Path.GetExtension(expanded).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return "第一版仅支持目标为 .exe 的应用快捷方式。";
        if (!File.Exists(expanded)) return "目标程序不存在或当前不可访问。";
        return null;
    }

    private static void ValidateIcon(byte[] bytes)
    {
        if (bytes.Length < 22 || BitConverter.ToUInt16(bytes, 0) != 0 || BitConverter.ToUInt16(bytes, 2) != 1)
            throw new InvalidDataException("无效 ICO 文件。");
        int count = BitConverter.ToUInt16(bytes, 4);
        if (count is < 1 or > 256 || bytes.Length < 6 + 16 * count) throw new InvalidDataException("ICO 目录无效。");
        for (int i = 0; i < count; i++)
        {
            uint length = BitConverter.ToUInt32(bytes, 6 + i * 16 + 8);
            uint offset = BitConverter.ToUInt32(bytes, 6 + i * 16 + 12);
            if (length == 0 || offset < 6 + 16 * count || (ulong)offset + length > (ulong)bytes.Length)
                throw new InvalidDataException("ICO 帧超出文件范围。");
        }
        using var stream = new MemoryStream(bytes, false);
        using var icon = new Icon(stream);
        using var bitmap = icon.ToBitmap();
    }

    private static string SafeAbsolute(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.IndexOf('\0') >= 0)
            throw new ArgumentException("必须提供完整 Windows 路径。");
        string full = Path.GetFullPath(path);
        if (full.StartsWith(@"\\?\", StringComparison.Ordinal) || full.StartsWith(@"\\.\", StringComparison.Ordinal) || full.AsSpan(2).Contains(':'))
            throw new ArgumentException("不支持设备路径或备用数据流路径。");
        return Path.TrimEndingDirectorySeparator(full);
    }

    private static string ShortcutPath(string path)
    {
        string full = SafeAbsolute(path);
        if (!Path.GetExtension(full).Equals(".lnk", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("只允许 .lnk 快捷方式。");
        return full;
    }

    private static void EnsureSafe(string path)
    {
        string? current = SafeAbsolute(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0 &&
                !ShellInterop.IsCloudPlaceholder(current))
                throw new IOException("为避免修改重定向文件，暂不支持符号链接或目录联接路径。");
            current = Path.GetDirectoryName(current);
        }
    }

    private static bool SamePath(string? a, string? b) => !string.IsNullOrEmpty(a) && !string.IsNullOrEmpty(b) &&
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string FormatItemError(Exception error, string shortcutPath)
    {
        if (error is not UnauthorizedAccessException && !(error is COMException && error.HResult == unchecked((int)0x80070005)))
            return error.Message;
        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        bool isCommonDesktop = !string.IsNullOrWhiteSpace(commonDesktop) &&
            Path.GetFullPath(shortcutPath).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(commonDesktop)) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        string advice = isCommonDesktop
            ? "公共桌面快捷方式权限不足。请先关闭程序，右键以管理员身份运行，确认后重新扫描，仅勾选失败项重试。普通模式下请取消勾选这些受保护项。"
            : "快捷方式访问被拒绝。请检查文件的只读属性和当前账户的文件/所在目录写入权限（ACL），确认后重新扫描重试。";
        return advice + " 原始错误：" + error.Message;
    }
    private static string Hash(string path)
    {
        EnsureSafe(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (file.Length > 16 * 1024 * 1024) throw new InvalidDataException("快捷方式文件过大。");
        return Convert.ToHexString(SHA256.HashData(file));
    }
    private static string ValidHash(string hash)
    {
        if (hash is null || hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("无效 SHA-256 指纹。");
        return hash.ToUpperInvariant();
    }
    private static void ValidId(string id)
    {
        if (id is null || !Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("无效批次 ID。");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        queue.CompleteAdding();
        if (Thread.CurrentThread != worker) worker.Join();
        queue.Dispose();
    }
}
