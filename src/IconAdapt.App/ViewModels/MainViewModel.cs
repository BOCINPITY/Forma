using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using IconAdapt.Core;
using IconAdapt.App.Diagnostics;
using IconAdapt.App.Imaging;
using Microsoft.Win32;

namespace IconAdapt.App.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IShortcutService service;
    private readonly IIconRenderer renderer;
    private readonly string settingsPath;
    private CancellationTokenSource? operationCancellation;
    private CancellationTokenSource? previewCancellation;
    private Task previewTask = Task.CompletedTask;
    private TaskCompletionSource? operationFinished;
    private bool initialized;
    private bool disposed;
    private string? directory;

    public MainViewModel(IShortcutService service, IIconRenderer renderer, string? directory, string storageRoot)
    {
        this.service = service;
        this.renderer = renderer;
        this.directory = directory;
        settingsPath = System.IO.Path.Combine(storageRoot, "settings.json");
        LoadSettings();
        ScanCommand = new(_ => _ = ScanAsync(), _ => !IsBusy);
        ChooseFolderCommand = new(_ => ChooseFolder(), _ => !IsBusy);
        DesktopCommand = new(_ => { this.directory = null; SourceChanged(); _ = ScanAsync(); }, _ => !IsBusy);
        SelectAllCommand = new(_ => SetSelection(true), _ => !IsBusy && Items.Count > 0);
        SelectNoneCommand = new(_ => SetSelection(false), _ => !IsBusy && Items.Count > 0);
        ApplyCommand = new(_ => _ = ApplyAsync(), _ => !IsBusy && !IsRendering && StyleError.Length == 0 && Items.Any(x => x.IsSelected) && Items.Where(x => x.IsSelected).All(x => x.Rendered != null));
        RestoreDefaultsCommand = new(_ => _ = RestoreDefaultsAsync(), _ => !IsBusy && CanRestoreDefaults);
        CancelCommand = new(_ => { operationCancellation?.Cancel(); CancelCommand?.Refresh(); }, _ => IsBusy && operationCancellation?.IsCancellationRequested == false);
        ColorCommand = new(value => { if (value is string hex) BackgroundColor = hex; }, _ => CanChooseColor);
        PreviewSizeCommand = new(value =>
        {
            if (int.TryParse(value?.ToString(), out var pixels) && pixels is 32 or 48 or 64 or 96) PreviewPixelSize = pixels;
        });
        ClarityPresetCommand = new(_ => { CropTransparent = true; ContentScale = 0.80; }, _ => !IsBusy);
        MacPresetCommand = new(_ => { BackdropIndex = (int)BackdropStyle.MacOS; CropTransparent = true; ContentScale = 0.80; CornerRadius = 0.23; }, _ => !IsBusy);
    }

    public ObservableCollection<ShortcutItem> Items { get; } = [];
    public string Version => typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "开发版";
    public string SourceLabel => directory == null ? "当前用户桌面 + 公共桌面" : directory;
    public string SourceShortLabel => directory is null ? "桌面快捷方式" :
        System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(directory)) is { Length: > 0 } name ? name : directory;
    public string SourceTooltip => SourceLabel;
    public string SelectionLabel => $"已选择 {Items.Count(x => x.IsSelected)} / {Items.Count(x => x.CanSelect)} 项";
    public bool IsEmpty => Items.Count == 0;
    private bool canRestoreDefaults;
    public bool CanRestoreDefaults { get => canRestoreDefaults; private set { if (Set(ref canRestoreDefaults, value)) RestoreDefaultsCommand?.Refresh(); } }
    public bool CanEdit => !IsBusy;
    public bool IsMacStyle => BackdropIndex == (int)BackdropStyle.MacOS;
    public bool CanChooseColor => CanEdit && !IsMacStyle;
    public int SelectedCount => Items.Count(x => x.IsSelected);
    public int TotalCount => Items.Count;
    private int previewPixelSize = 48;
    public int PreviewPixelSize
    {
        get => previewPixelSize;
        private set
        {
            if (!Set(ref previewPixelSize, value)) return;
            Changed(nameof(PreviewSizeLabel));
            foreach (var item in Items) item.SetPreviewSize(value);
        }
    }
    public string PreviewSizeLabel => $"{PreviewPixelSize}px · 实际尺寸";
    private bool isBusy;
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) { Changed(nameof(CanEdit)); Changed(nameof(CanChooseColor)); Changed(nameof(CompactStatus)); RefreshCommands(); } } }
    private bool isRendering;
    public bool IsRendering { get => isRendering; private set { if (Set(ref isRendering, value)) { Changed(nameof(CompactStatus)); RefreshCommands(); } } }
    private string status = "准备扫描桌面快捷方式";
    public string Status { get => status; private set { if (Set(ref status, value)) { Changed(nameof(CompactStatus)); Changed(nameof(StatusDetail)); Changed(nameof(NoticeSummary)); } } }
    public string StatusDetail => Status;
    public string CompactStatus
    {
        get
        {
            if (IsBusy)
                return Status.Contains("读取", StringComparison.Ordinal) ? "正在读取记录…" :
                    Status.Contains("扫描", StringComparison.Ordinal) ? "正在扫描图标…" :
                    Status.Contains("恢复", StringComparison.Ordinal) ? "正在恢复图标…" : "正在应用图标…";
            if (IsRendering) return "正在生成预览…";
            if (Status.StartsWith("扫描完成", StringComparison.Ordinal)) return $"已载入 {TotalCount} 个图标";
            if (Status.StartsWith("应用结果", StringComparison.Ordinal))
            {
                var outcomes = Status.Split('·').Skip(1).Select(x => x.Trim()).Where(x => !x.EndsWith(" 0", StringComparison.Ordinal)).ToArray();
                return outcomes.Length > 0 ? string.Join(" · ", outcomes) : "应用结束";
            }
            return Status == "准备扫描桌面快捷方式" ? "准备就绪" : Status;
        }
    }
    private string notice = "预览只在本地生成；点击应用后才会修改快捷方式。";
    private bool noticeNeedsAttention;
    public string Notice { get => notice; private set { if (Set(ref notice, value)) { Changed(nameof(NoticeSummary)); Changed(nameof(NoticeDetail)); Changed(nameof(NoticeHasIssue)); } } }
    public string NoticeDetail => Notice;
    // Severity comes from actual outcomes, not words such as "失败" in explanatory guidance.
    public bool NoticeHasIssue => noticeNeedsAttention || Items.Any(x => x.HasIssue);
    public string NoticeSummary => NoticeHasIssue ? "有项目或提示需要核对，展开查看详情" :
        Status == "操作已停止" || Notice.Contains("已取消", StringComparison.Ordinal) ? "操作已停止，可展开查看结果" :
        Status.StartsWith("恢复结束", StringComparison.Ordinal) || Status.StartsWith("恢复已停止", StringComparison.Ordinal) ? "恢复结果已保存，可展开查看" :
        "本地预览 · 应用时自动备份";
    private void SetNotice(string message, bool needsAttention = false)
    {
        noticeNeedsAttention = needsAttention;
        Notice = message;
        Changed(nameof(NoticeHasIssue)); Changed(nameof(NoticeSummary));
    }
    private ShortcutItem? focusedItem;
    public ShortcutItem? FocusedItem { get => focusedItem; set => Set(ref focusedItem, value); }
    private int backdropIndex = (int)BackdropStyle.MacOS;
    public int BackdropIndex { get => backdropIndex; set { if (Set(ref backdropIndex, value)) { Changed(nameof(IsMacStyle)); Changed(nameof(CanChooseColor)); ColorCommand?.Refresh(); SchedulePreview(); } } }
    private string backgroundColor = "#7772EE";
    public string BackgroundColor { get => backgroundColor; set { if (Set(ref backgroundColor, value)) SchedulePreview(); } }
    private double contentScale = 0.80;
    public double ContentScale { get => contentScale; set { if (Set(ref contentScale, value)) { Changed(nameof(ScaleLabel)); Changed(nameof(DetailLimitNotice)); SchedulePreview(); } } }
    public string ScaleLabel => $"{ContentScale:P0}";
    private double cornerRadius = 0.23;
    public double CornerRadius { get => cornerRadius; set { if (Set(ref cornerRadius, value)) { Changed(nameof(RadiusLabel)); SchedulePreview(); } } }
    public string RadiusLabel => $"{CornerRadius:P0}";
    private bool cropTransparent = true;
    public bool CropTransparent { get => cropTransparent; set { if (Set(ref cropTransparent, value)) { Changed(nameof(DetailLimitNotice)); SchedulePreview(); } } }
    public string DetailLimitNotice => ContentScale < 0.66 ? "图案占比较小，小尺寸下细节会受限。可试用“清晰优先”，再比较 32 / 48px 效果。" :
        !CropTransparent ? "保留透明留白可能让图案显得更小。可试用“清晰优先”，再检查小尺寸效果。" : "";
    private string styleError = "";
    public string StyleError { get => styleError; private set { if (Set(ref styleError, value)) RefreshCommands(); } }

    public RelayCommand ScanCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand DesktopCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand RestoreDefaultsCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand ColorCommand { get; }
    public RelayCommand PreviewSizeCommand { get; }
    public RelayCommand ClarityPresetCommand { get; }
    public RelayCommand MacPresetCommand { get; }

    public async Task InitializeAsync()
    {
        if (initialized) return;
        initialized = true;
        await ScanAsync();
    }

    private void ChooseFolder()
    {
        var dialog = new OpenFolderDialog { Title = "选择包含 .lnk 快捷方式的文件夹", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        directory = dialog.FolderName;
        SourceChanged();
        _ = ScanAsync();
    }

    private async Task ScanAsync()
    {
        await RunOperationAsync("正在扫描快捷方式…", async token =>
        {
            previewCancellation?.Cancel();
            await previewTask;
            var result = await service.ScanAsync(directory, token);
            foreach (var old in Items) old.PropertyChanged -= ItemChanged;
            Items.Clear();
            foreach (var info in result.Items)
            {
                ShortcutItem item;
                try { item = new(info); }
                catch (Exception ex) { item = new(info with { OriginalIconPng = null, IsSupported = false, Error = $"图标无法读取：{ex.Message}" }); }
                item.PropertyChanged += ItemChanged;
                item.SetPreviewSize(PreviewPixelSize);
                Items.Add(item);
            }
            FocusedItem = Items.FirstOrDefault(x => x.CanSelect) ?? Items.FirstOrDefault();
            Changed(nameof(IsEmpty));
            UpdateSelection();
            SetNotice(result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : "选中卡片上的复选框参与批量处理；点击卡片查看前后对比。", result.Warnings.Count > 0);
            Status = $"扫描完成 · {Items.Count} 个快捷方式，{Items.Count(x => !x.CanSelect)} 个已跳过";
            await RefreshHistoryAsync(token);
        });
        SchedulePreview();
        await previewTask;
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShortcutItem.IsSelected)) UpdateSelection();
        if (args.PropertyName == nameof(ShortcutItem.HasIssue)) { Changed(nameof(NoticeHasIssue)); Changed(nameof(NoticeSummary)); }
    }
    private void UpdateSelection()
    {
        Changed(nameof(SelectionLabel)); Changed(nameof(SelectedCount)); Changed(nameof(TotalCount));
        Changed(nameof(CompactStatus)); Changed(nameof(NoticeHasIssue)); Changed(nameof(NoticeSummary)); RefreshCommands();
    }
    private void SourceChanged() { Changed(nameof(SourceLabel)); Changed(nameof(SourceShortLabel)); Changed(nameof(SourceTooltip)); }
    private void SetSelection(bool value) { foreach (var item in Items) item.IsSelected = value; }
    private IconStyle CurrentStyle() => new()
    {
        Backdrop = (BackdropStyle)Math.Clamp(BackdropIndex, 0, 3), BackgroundColor = IsMacStyle ? "#F4F6FA" : BackgroundColor.Trim(),
        ContentScale = ContentScale, CornerRadius = CornerRadius, CropTransparent = CropTransparent
    };

    private void SchedulePreview()
    {
        if (!initialized || disposed) return;
        previewCancellation?.Cancel();
        previewCancellation?.Dispose();
        previewCancellation = new();
        previewTask = RenderPreviewAsync(previewCancellation.Token);
    }

    private async Task RenderPreviewAsync(CancellationToken token)
    {
        IsRendering = true;
        try
        {
            await Task.Delay(250, token);
            var style = CurrentStyle();
            if (style.BackgroundColor.Length != 7 || style.BackgroundColor[0] != '#' || !uint.TryParse(style.BackgroundColor.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
            {
                StyleError = "颜色请输入 #RRGGBB，例如 #7772EE。";
                return;
            }
            StyleError = "";
            var snapshot = Items.Where(x => x.CanSelect).OrderByDescending(x => ReferenceEquals(x, FocusedItem)).ToArray();
            foreach (var item in snapshot)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var rendered = await Task.Run(() =>
                    {
                        var result = renderer.Render(item.Info.OriginalIconPng!, style, item.Info.SourceFrames);
                        return (Result: result, Frames: OutputIconFrames.Decode(result.IconFile));
                    }, token);
                    token.ThrowIfCancellationRequested();
                    item.SetRendered(rendered.Result, rendered.Frames);
                    if (item.Status is "等待预览" or "预览失败" or "可应用")
                    {
                        item.Status = "可应用";
                        item.Detail = item.Info.Error ?? "";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { item.SetRendered(null); item.Status = "预览失败"; item.Detail = ex.Message; }
            }
            SaveSettings(style);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StyleError = $"无法生成预览：{ex.Message}"; }
        finally { if (previewCancellation?.Token == token) IsRendering = false; }
    }

    private async Task ApplyAsync()
    {
        if (!ApplyCommand.CanExecute(null)) return;
        var selection = Items.Where(x => x.IsSelected).ToArray();
        if (selection.Any(x => x.Rendered == null)) { SetNotice("选中的项目包含预览失败项，请先取消选择或调整风格。", true); return; }
        if (MessageBox.Show($"将修改 {selection.Length} 个快捷方式的图标。\n\n程序会先自动备份。以后可点击“恢复默认图标”还原。\n\n生成的图标会保留在本机，请勿手动删除数据目录。", "应用图标适配", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        var changes = selection.Select(x => new IconChange(x.Path, x.Info.FileHash, x.Rendered!.IconFile)).ToArray();
        await RunOperationAsync($"正在备份并应用 {changes.Length} 项…", async token =>
        {
            var batch = await service.ApplyAsync(changes, token);
            foreach (var result in batch.Items)
            {
                var item = Items.FirstOrDefault(x => string.Equals(x.Path, result.ShortcutPath, StringComparison.OrdinalIgnoreCase));
                if (item == null) continue;
                item.Status = OperationFeedback.DisplayState(result.State);
                item.Detail = result.Error ?? OperationFeedback.ItemAdvice(null, result.ShortcutPath, result.State);
                if (result.AppliedHash != null) item.Info = item.Info with { FileHash = result.AppliedHash };
            }
            Status = OperationFeedback.ApplyStatus(batch);
            SetNotice(OperationFeedback.ApplyNotice(batch), BatchNeedsAttention(batch));
            await RefreshHistoryAsync(CancellationToken.None);
        });
    }

    private async Task RestoreDefaultsAsync()
    {
        if (!RestoreDefaultsCommand.CanExecute(null)) return;
        if (MessageBox.Show("将恢复此账户下所有由 Forma 适配过的快捷方式，包括其他文件夹中的项目。\n\n连续多次适配会一次还原到首次适配前的图标，不受当前勾选范围限制。程序会保留外部修改过的文件并显示原因。", "恢复默认图标", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RestoreDefaultsCoreAsync();
    }

    private async Task RestoreDefaultsCoreAsync()
    {
        await RunOperationAsync("正在恢复默认图标…", async token =>
        {
            var result = await service.RestoreDefaultsAsync(token);
            int restored = result.Items.Count(x => x.Restored);
            int remaining = result.Items.Count - restored;
            Status = result.Cancelled ? $"恢复已停止 · 已恢复 {restored} 项 · 未完成 {remaining} 项" :
                $"恢复结束 · 已恢复 {restored} 项 · 未完成 {remaining} 项";
            var details = new List<string> { result.Items.Count == 0 ? "没有需要恢复的图标。" :
                $"{restored} 个快捷方式已恢复到首次适配前的图标。" };
            foreach (var item in result.Items.Where(x => !x.Restored))
                details.Add($"{item.ShortcutPath}\n{OperationFeedback.RestoreAdvice(item.Error)}");
            if (remaining > 0) details.Add("未完成项目的备份仍保留。解决权限、占用或冲突后，可再次点击“恢复默认图标”。");
            SetNotice(string.Join("\n\n", details), remaining > 0);
            await RefreshHistoryAsync(CancellationToken.None);
        });
        // Refresh file fingerprints and original sources after restoration before another apply.
        var restoreStatus = Status;
        var restoreNotice = Notice;
        var restoreNeedsAttention = noticeNeedsAttention;
        await ScanAsync();
        var scanFailed = Status == "操作未能完成";
        var scanNotice = Notice;
        Status = restoreStatus;
        SetNotice(restoreNotice + (scanFailed ? $"\n重新扫描未完成：{scanNotice}" : ""), restoreNeedsAttention || scanFailed);
    }

    private async Task RefreshHistoryAsync(CancellationToken token)
    {
        var history = await service.GetHistoryAsync(token);
        CanRestoreDefaults = history.Any(batch => batch.Items.Any(x => x.State is "applied" or "pending" or "restoring" or "conflict"));
    }

    private async Task RunOperationAsync(string message, Func<CancellationToken, Task> action)
    {
        if (IsBusy) return;
        operationCancellation?.Dispose();
        operationCancellation = new();
        operationFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        IsBusy = true;
        Status = message;
        try { await action(operationCancellation.Token); }
        catch (OperationCanceledException) { Status = "操作已停止"; SetNotice("已完成的操作会保留。可查看图标上的状态，或点击“恢复默认图标”还原。"); await RecoverHistoryAsync(); }
        catch (Exception ex) { Status = "操作未能完成"; SetNotice(FriendlyError(ex), true); await RecoverHistoryAsync(); }
        finally { IsBusy = false; operationFinished.TrySetResult(); }
    }
    private async Task RecoverHistoryAsync()
    {
        try { await RefreshHistoryAsync(CancellationToken.None); }
        catch (Exception ex) { SetNotice(Notice + $"\n备份状态暂时无法读取，请重新扫描后重试：{ex.Message}", true); }
    }
    private static bool BatchNeedsAttention(OperationBatch batch) => batch.Items.Any(x =>
        x.State is "failed" or "restore_failed" or "pending" or "restoring" or "conflict" or "queued");
    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => $"没有权限访问该位置，请选择可读写的文件夹或跳过受保护的快捷方式。\n{ex.Message}",
        IOException => $"文件读写失败，请检查文件是否存在、被占用或磁盘空间不足。\n{ex.Message}",
        _ => $"{ex.Message}\n备份会保留，可展开查看结果并重试，或点击“恢复默认图标”。"
    };

    public void Cancel() { operationCancellation?.Cancel(); previewCancellation?.Cancel(); CancelCommand.Refresh(); }
    public async Task WaitForIdleAsync()
    {
        if (operationFinished != null) await operationFinished.Task;
        await previewTask;
    }
    private void RefreshCommands()
    {
        ScanCommand?.Refresh(); ChooseFolderCommand?.Refresh(); DesktopCommand?.Refresh(); SelectAllCommand?.Refresh(); SelectNoneCommand?.Refresh();
        ApplyCommand?.Refresh(); RestoreDefaultsCommand?.Refresh(); CancelCommand?.Refresh(); ColorCommand?.Refresh(); ClarityPresetCommand?.Refresh(); MacPresetCommand?.Refresh();
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            var style = System.Text.Json.JsonSerializer.Deserialize<IconStyle>(File.ReadAllText(settingsPath));
            if (style == null) return;
            backdropIndex = Math.Clamp((int)style.Backdrop, 0, 3); backgroundColor = style.BackgroundColor;
            contentScale = Math.Clamp(style.ContentScale, .3, .9); cornerRadius = Math.Clamp(style.CornerRadius, 0, .5); cropTransparent = style.CropTransparent;
        }
        catch { notice = "上次风格设置无法读取，已使用默认风格；快捷方式备份不受影响。"; noticeNeedsAttention = true; }
    }
    private void SaveSettings(IconStyle style)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(settingsPath)!);
            var temp = settingsPath + ".tmp";
            File.WriteAllText(temp, System.Text.Json.JsonSerializer.Serialize(style, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, settingsPath, true);
        }
        catch (Exception ex) { SetNotice($"预览已生成，但风格设置无法保存：{ex.Message}", true); }
    }
    public void Dispose()
    {
        disposed = true;
        Cancel();
        operationCancellation?.Dispose(); previewCancellation?.Dispose();
        foreach (var item in Items) item.PropertyChanged -= ItemChanged;
    }
}
