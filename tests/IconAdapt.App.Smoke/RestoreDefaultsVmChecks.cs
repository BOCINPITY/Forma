using System.IO;
using System.Reflection;
using IconAdapt.App.ViewModels;
using IconAdapt.Core;

internal static class RestoreDefaultsVmChecks
{
    internal static void Run(byte[] png, IIconRenderer renderer, string root)
    {
        var service = new RestoreFixture(png, root);
        using var vm = new MainViewModel(service, renderer, root, Path.Combine(root, "restore-defaults-vm"));
        vm.InitializeAsync().GetAwaiter().GetResult();
        Check(vm.RestoreDefaultsCommand.CanExecute(null), "Active stored changes must enable default restoration.");
        vm.SelectNoneCommand.Execute(null);
        service.Result = new([new(Path.Combine(root, "other-folder", "done.lnk"), true, null),
            new(Path.Combine(root, "locked.lnk"), false, "文件被占用，已保留当前文件。")], false);
        service.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var task = ExecuteWithoutConfirmation(vm);
        Check(vm.IsBusy && !vm.RestoreDefaultsCommand.CanExecute(null) && !vm.ApplyCommand.CanExecute(null),
            "Default restoration must prevent concurrent apply and restore actions.");
        service.Gate.SetResult();
        task.GetAwaiter().GetResult();
        Check(service.Calls == 1 && service.Scans == 2 && vm.Items[0].Info.FileHash == "scan-2",
            "Restoration must refresh source fingerprints before another apply.");
        Check(vm.Status.Contains("已恢复 1") && vm.Status.Contains("未完成 1") && vm.NoticeHasIssue &&
            vm.Notice.Contains("locked.lnk") && vm.Notice.Contains("文件被占用") && vm.CanRestoreDefaults,
            "Partial restoration must retain actionable per-file failures and remain retryable after rescan.");
        Check(!vm.Notice.Contains("恢复记录"), "Restoration advice must not point to the removed history page.");

        service.Gate = null;
        service.Active = false;
        service.Result = new([new(Path.Combine(root, "locked.lnk"), true, null)], false);
        ExecuteWithoutConfirmation(vm).GetAwaiter().GetResult();
        Check(vm.Status.Contains("已恢复 1") && !vm.NoticeHasIssue && !vm.RestoreDefaultsCommand.CanExecute(null),
            "A complete restoration must show success and disable the action after refreshed stored state.");

        service.Active = true;
        service.Result = new([new(Path.Combine(root, "locked.lnk"), false, "操作已停止，尚未恢复到默认图标。")], true);
        ExecuteWithoutConfirmation(vm).GetAwaiter().GetResult();
        Check(vm.Status.StartsWith("恢复已停止") && vm.NoticeHasIssue && vm.CanRestoreDefaults,
            "Cancellation must keep unfinished work visible and allow retry.");
        service.Error = new IOException("测试备份暂时无法读取。");
        ExecuteWithoutConfirmation(vm).GetAwaiter().GetResult();
        Check(vm.Status == "操作未能完成" && vm.Notice.Contains("备份暂时无法读取") && vm.NoticeHasIssue && !vm.IsBusy,
            "A restore error must survive the subsequent scan and release the busy state.");
        Console.WriteLine("PASS: default-restore VM outcomes, busy state, retry, cancellation and post-restore fingerprints.");
    }

    // Bypass only the native confirmation dialog. The command's full operation and
    // rescan path runs against an in-memory service, without writing any shortcuts.
    private static Task ExecuteWithoutConfirmation(MainViewModel vm) =>
        (Task)(typeof(MainViewModel).GetMethod("RestoreDefaultsCoreAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(vm, null) ?? throw new InvalidOperationException("Restore operation missing."));

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class RestoreFixture(byte[] png, string root) : IShortcutService
    {
        public bool Active = true;
        public int Calls, Scans;
        public TaskCompletionSource? Gate;
        public Exception? Error;
        public RestoreDefaultsResult Result = new([], false);
        public void Dispose() { }
        public Task<ScanResult> ScanAsync(string? directory = null, CancellationToken cancellationToken = default)
        {
            Scans++;
            return Task.FromResult(new ScanResult([new(Path.Combine(root, "fixture.lnk"), "Fixture", "", "", 0,
                $"scan-{Scans}", png, true, null)], []));
        }
        public Task<IReadOnlyList<OperationBatch>> GetHistoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OperationBatch>>(Active ? [new() { Items = [new() { State = "applied" }] }] : []);
        public async Task<RestoreDefaultsResult> RestoreDefaultsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Gate != null) await Gate.Task;
            if (Error != null) throw Error;
            return Result;
        }
        public Task<OperationBatch> ApplyAsync(IReadOnlyList<IconChange> changes, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<OperationBatch> RestoreAsync(string batchId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
