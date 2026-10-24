using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Windows;
using IconAdapt.Core;
using IconAdapt.App.Diagnostics;
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
        DesktopCommand = new(_ => { this.directory = null; Changed(nameof(SourceLabel)); _ = ScanAsync(); }, _ => !IsBusy);
        SelectAllCommand = new(_ => SetSelection(true), _ => !IsBusy && Items.Count > 0);
        SelectNoneCommand = new(_ => SetSelection(false), _ => !IsBusy && Items.Count > 0);
        ApplyCommand = new(_ => _ = ApplyAsync(), _ => !IsBusy && !IsRendering && StyleError.Length == 0 && Items.Any(x => x.IsSelected) && Items.Where(x => x.IsSelected).All(x => x.Rendered != null));
        RestoreCommand = new(_ => _ = RestoreAsync(), _ => !IsBusy && SelectedBatch?.CanRestore == true);
        CancelCommand = new(_ => { operationCancellation?.Cancel(); CancelCommand?.Refresh(); }, _ => IsBusy && operationCancellation?.IsCancellationRequested == false);
        WorkspaceCommand = new(_ => ShowHistory = false);
        HistoryCommand = new(_ => { ShowHistory = true; _ = RefreshHistorySafelyAsync(); }, _ => !IsBusy);
        ColorCommand = new(value => { if (value is string hex) BackgroundColor = hex; }, _ => !IsBusy);
    }

    public ObservableCollection<ShortcutItem> Items { get; } = [];
    public ObservableCollection<BatchViewModel> History { get; } = [];
    public string Version => typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "开发版";
    public string SourceLabel => directory == null ? "当前用户桌面 + 公共桌面" : directory;
    public string SelectionLabel => $"已选择 {Items.Count(x => x.IsSelected)} / {Items.Count(x => x.CanSelect)} 项";
    public bool IsEmpty => Items.Count == 0;
    public bool HasHistory => History.Count > 0;
    public bool CanEdit => !IsBusy;
    public int SelectedCount => Items.Count(x => x.IsSelected);
    private bool isBusy;
    public bool IsBusy { get => isBusy; private set { if (Set(ref isBusy, value)) { Changed(nameof(CanEdit)); RefreshCommands(); } } }
    private bool isRendering;
    public bool IsRendering { get => isRendering; private set { if (Set(ref isRendering, value)) RefreshCommands(); } }
    private string status = "准备扫描桌面快捷方式";
    public string Status { get => status; private set => Set(ref status, value); }
    private string notice = "预览只在本地生成；点击应用后才会修改快捷方式。";
    public string Notice { get => notice; private set => Set(ref notice, value); }
    private bool showHistory;
    public bool ShowHistory { get => showHistory; set { if (Set(ref showHistory, value)) Changed(nameof(ShowWorkspace)); } }
    public bool ShowWorkspace => !ShowHistory;
    private ShortcutItem? focusedItem;
    public ShortcutItem? FocusedItem { get => focusedItem; set => Set(ref focusedItem, value); }
    private BatchViewModel? selectedBatch;
    public BatchViewModel? SelectedBatch { get => selectedBatch; set { if (Set(ref selectedBatch, value)) RefreshCommands(); } }
    private int backdropIndex;
    public int BackdropIndex { get => backdropIndex; set { if (Set(ref backdropIndex, value)) SchedulePreview(); } }
    private string backgroundColor = "#7772EE";
    public string BackgroundColor { get => backgroundColor; set { if (Set(ref backgroundColor, value)) SchedulePreview(); } }
    private double contentScale = 0.68;
    public double ContentScale { get => contentScale; set { if (Set(ref contentScale, value)) { Changed(nameof(ScaleLabel)); SchedulePreview(); } } }
    public string ScaleLabel => $"{ContentScale:P0}";
    private double cornerRadius = 0.23;
    public double CornerRadius { get => cornerRadius; set { if (Set(ref cornerRadius, value)) { Changed(nameof(RadiusLabel)); SchedulePreview(); } } }
    public string RadiusLabel => $"{CornerRadius:P0}";
    private bool cropTransparent = true;
    public bool CropTransparent { get => cropTransparent; set { if (Set(ref cropTransparent, value)) SchedulePreview(); } }
    private string styleError = "";
    public string StyleError { get => styleError; private set { if (Set(ref styleError, value)) RefreshCommands(); } }

    public RelayCommand ScanCommand { get; }
    public RelayCommand ChooseFolderCommand { get; }
    public RelayCommand DesktopCommand { get; }
    public RelayCommand SelectAllCommand { get; }
    public RelayCommand SelectNoneCommand { get; }
    public RelayCommand ApplyCommand { get; }
    public RelayCommand RestoreCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand WorkspaceCommand { get; }
    public RelayCommand HistoryCommand { get; }
    public RelayCommand ColorCommand { get; }

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
        Changed(nameof(SourceLabel));
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
                Items.Add(item);
            }
            FocusedItem = Items.FirstOrDefault(x => x.CanSelect) ?? Items.FirstOrDefault();
            Changed(nameof(IsEmpty));
            UpdateSelection();
            Notice = result.Warnings.Count > 0 ? string.Join("\n", result.Warnings) : "选中卡片上的复选框参与批量处理；点击卡片查看前后对比。";
            Status = $"扫描完成 · {Items.Count} 个快捷方式，{Items.Count(x => !x.CanSelect)} 个已跳过";
            await RefreshHistoryAsync(token);
        });
        SchedulePreview();
        await previewTask;
    }

    private void ItemChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(ShortcutItem.IsSelected)) UpdateSelection();
    }
    private void UpdateSelection() { Changed(nameof(SelectionLabel)); Changed(nameof(SelectedCount)); RefreshCommands(); }
    private void SetSelection(bool value) { foreach (var item in Items) item.IsSelected = value; }
    private IconStyle CurrentStyle() => new()
    {
        Backdrop = (BackdropStyle)Math.Clamp(BackdropIndex, 0, 2), BackgroundColor = BackgroundColor.Trim(),
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
            await Task.Delay(180, token);
            var style = CurrentStyle();
            if (style.BackgroundColor.Length != 7 || style.BackgroundColor[0] != '#' || !uint.TryParse(style.BackgroundColor.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out _))
            {
                StyleError = "颜色请输入 #RRGGBB，例如 #7772EE。";
                return;
            }
            StyleError = "";
            var snapshot = Items.Where(x => x.CanSelect).ToArray();
            foreach (var item in snapshot)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var result = await Task.Run(() => renderer.Render(item.Info.OriginalIconPng!, style), token);
                    token.ThrowIfCancellationRequested();
                    item.Rendered = result;
                    item.Preview = ShortcutItem.Decode(result.PreviewPng);
                    if (item.Status is "等待预览" or "预览失败" or "可应用")
                    {
                        item.Status = "可应用";
                        item.Detail = item.Info.Error ?? "";
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { item.Rendered = null; item.Status = "预览失败"; item.Detail = ex.Message; }
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
        if (selection.Any(x => x.Rendered == null)) { Notice = "选中的项目包含预览失败项，请先取消选择或调整风格。"; return; }
        if (MessageBox.Show($"将修改 {selection.Length} 个快捷方式的图标。\n\n程序会先保存原始快捷方式备份和操作记录。应用后可在“恢复记录”中恢复。\n\n生成的图标会保留在本机，请勿手动删除数据目录。", "应用图标适配", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
        var changes = selection.Select(x => new IconChange(x.Path, x.Info.FileHash, x.Rendered!.IconFile)).ToArray();
        await RunOperationAsync($"正在备份并应用 {changes.Length} 项…", async token =>
        {
            var batch = await service.ApplyAsync(changes, token);
            foreach (var result in batch.Items)
            {
                var item = Items.FirstOrDefault(x => string.Equals(x.Path, result.ShortcutPath, StringComparison.OrdinalIgnoreCase));
                if (item == null) continue;
                item.Status = BatchItemViewModel.DisplayState(result.State);
                item.Detail = result.Error ?? OperationFeedback.ItemAdvice(null, result.ShortcutPath, result.State);
                if (result.AppliedHash != null) item.Info = item.Info with { FileHash = result.AppliedHash };
            }
            Status = OperationFeedback.ApplyStatus(batch);
            Notice = OperationFeedback.ApplyNotice(batch);
            await RefreshHistoryAsync(CancellationToken.None);
            SelectedBatch = History.FirstOrDefault(x => x.Id == batch.Id) ?? SelectedBatch;
        });
    }

    private async Task RestoreAsync()
    {
        if (SelectedBatch is not { } batch || IsBusy) return;
        if (MessageBox.Show($"恢复 {batch.Title} 这批快捷方式的原始图标？\n\n程序会检查文件是否被其他程序修改；冲突会保留并显示原因。", "恢复原始图标", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
        await RunOperationAsync("正在恢复原始图标…", async token =>
        {
            var result = await service.RestoreAsync(batch.Id, token);
            Status = $"恢复结束 · {result.Items.Count(x => x.State == "restored")} 项已恢复 / {result.Items.Count} 项";
            Notice = "请查看本批次各项结果。恢复失败或文件冲突时，备份及操作记录仍保留。返回图标工作台后可重新扫描。";
            await RefreshHistoryAsync(CancellationToken.None);
        });
        // Refresh file fingerprints and original sources after restoration before another apply.
        var restoreStatus = Status;
        var restoreNotice = Notice;
        await ScanAsync();
        var scanFailed = Status == "操作未能完成";
        var scanNotice = Notice;
        Status = restoreStatus;
        Notice = restoreNotice + (scanFailed ? $"\n重新扫描未完成：{scanNotice}" : "");
    }

    private async Task RefreshHistorySafelyAsync()
    {
        await RunOperationAsync("正在读取恢复记录…", async token => { await RefreshHistoryAsync(token); Status = $"共 {History.Count} 个操作批次"; });
    }
    private async Task RefreshHistoryAsync(CancellationToken token)
    {
        var id = SelectedBatch?.Id;
        var history = await service.GetHistoryAsync(token);
        History.Clear();
        foreach (var batch in history.OrderByDescending(x => x.CreatedAt)) History.Add(new(batch));
        SelectedBatch = History.FirstOrDefault(x => x.Id == id) ?? History.FirstOrDefault();
        Changed(nameof(HasHistory));
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
        catch (OperationCanceledException) { Status = "操作已停止"; Notice = "已完成的操作会保留。涉及修改时，请在恢复记录中查看实际结果。"; await RecoverHistoryAsync(); }
        catch (Exception ex) { Status = "操作未能完成"; Notice = FriendlyError(ex); await RecoverHistoryAsync(); }
        finally { IsBusy = false; operationFinished.TrySetResult(); }
    }
    private async Task RecoverHistoryAsync()
    {
        try { await RefreshHistoryAsync(CancellationToken.None); }
        catch (Exception ex) { Notice += $"\n恢复记录暂时无法读取：{ex.Message}"; }
    }
    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => $"没有权限访问该位置，请选择可读写的文件夹或跳过受保护的快捷方式。\n{ex.Message}",
        IOException => $"文件读写失败，请检查文件是否存在、被占用或磁盘空间不足。\n{ex.Message}",
        _ => $"{ex.Message}\n操作记录和备份会保留，请在恢复记录中核对结果。"
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
        ApplyCommand?.Refresh(); RestoreCommand?.Refresh(); CancelCommand?.Refresh(); HistoryCommand?.Refresh(); ColorCommand?.Refresh();
    }
    private void LoadSettings()
    {
        try
        {
            if (!File.Exists(settingsPath)) return;
            var style = System.Text.Json.JsonSerializer.Deserialize<IconStyle>(File.ReadAllText(settingsPath));
            if (style == null) return;
            backdropIndex = Math.Clamp((int)style.Backdrop, 0, 2); backgroundColor = style.BackgroundColor;
            contentScale = Math.Clamp(style.ContentScale, .3, .9); cornerRadius = Math.Clamp(style.CornerRadius, 0, .5); cropTransparent = style.CropTransparent;
        }
        catch { notice = "上次风格设置无法读取，已使用默认风格；快捷方式备份不受影响。"; }
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
        catch (Exception ex) { Notice = $"预览已生成，但风格设置无法保存：{ex.Message}"; }
    }
    public void Dispose()
    {
        disposed = true;
        Cancel();
        operationCancellation?.Dispose(); previewCancellation?.Dispose();
        foreach (var item in Items) item.PropertyChanged -= ItemChanged;
    }
}
