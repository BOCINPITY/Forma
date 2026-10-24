using IconAdapt.Tests;
using System.Security.Cryptography;
using System.Text.Json;
using IconAdapt.Core;
using IconAdapt.Windows;

internal static class RestoreDefaultsChecks
{
    public static async Task Run(string testRoot, byte[] ico, Action<bool, string> check)
    {
        string root = Path.Combine(testRoot, "restore-defaults");
        string links = Path.Combine(root, "links");
        string storage = Path.Combine(root, "storage");
        Directory.CreateDirectory(links);
        string otherLinks = Path.Combine(root, "other-links");
        Directory.CreateDirectory(otherLinks);
        string first = Path.Combine(links, "First.lnk"), second = Path.Combine(otherLinks, "Second.lnk");
        CreateLink(first); CreateLink(second);
        var originalFirst = ReadLink(first);
        var originalSecond = ReadLink(second);
        OperationBatch firstLayer, secondLayer;
        using (var service = new ShortcutService(storage))
        {
            check((await service.RestoreDefaultsAsync()).Items.Count == 0, "restore defaults on empty history is a no-op");
            firstLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            secondLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            var thirdLayer = await service.ApplyAsync([new(first, Hash(first), ico)]);
            check(firstLayer.Items.Concat(secondLayer.Items).Concat(thirdLayer.Items).All(x => x.State == "applied"),
                "restore-defaults fixture has three genuine COM icon layers");
        }
        using (var restarted = new ShortcutService(storage))
        {
            await restarted.ScanAsync(links); // The other folder must not disappear from restore scope.
            var result = await restarted.RestoreDefaultsAsync();
            check(result.Items.Count == 2 && result.Items.All(x => x.Restored && x.Error is null) && !result.Cancelled,
                "one restore reaches original defaults across all persisted layers, with paths deduplicated");
            check(ReadLink(first) == originalFirst && ReadLink(second) == originalSecond,
                "restore defaults preserves all launch properties and reaches original icon references");
            var history = await restarted.GetHistoryAsync();
            check(history.SelectMany(x => x.Items).All(x => x.State == "restored"), "every active layer is durably resolved");
            check(history.SelectMany(x => x.Items).All(x => File.Exists(x.BackupPath) && File.Exists(x.GeneratedIconPath)),
                "one-click restore retains original backups and generated icons");
            string before = Hash(first);
            check((await restarted.RestoreDefaultsAsync()).Items.Count == 0 && Hash(first) == before,
                "repeated restore defaults is idempotent and does not touch resolved shortcuts");

            // Old non-active outcomes must not be presented as icons waiting to be restored.
            var ignored = await restarted.ApplyAsync(Enumerable.Range(0, 3).Select(i =>
                new IconChange(Path.Combine(links, $"never-written-{i}.lnk"), Hash(first), ico)).ToArray());
            ignored.Items[1].State = "queued";
            ignored.Items[2].State = "cancelled";
            Save(storage, ignored);
            check((await restarted.RestoreDefaultsAsync()).Items.Count == 0,
                "failed, queued and cancelled history entries are not restore candidates");
        }

        // A problem with one path blocks every older layer of that path, not unrelated paths.
        using (var service = new ShortcutService(storage))
        {
            firstLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            secondLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            byte[] beforeEdit = File.ReadAllBytes(first);
            EditArguments(first, "/c echo external-change");
            string editedHash = Hash(first);
            var result = await service.RestoreDefaultsAsync();
            check(result.Items.Count == 2 && !result.Items.Single(x => x.ShortcutPath == first).Restored &&
                result.Items.Single(x => x.ShortcutPath == second).Restored,
                "a conflicting path does not prevent unrelated paths reaching defaults");
            check(Hash(first) == editedHash && ReadLink(second) == originalSecond,
                "external shortcut changes are preserved while independent restoration succeeds");
            var history = await service.GetHistoryAsync();
            check(history.Single(x => x.Id == firstLayer.Id).Items.Single(x => x.ShortcutPath == first).State == "applied" &&
                history.Single(x => x.Id == secondLayer.Id).Items.Single(x => x.ShortcutPath == first).State == "conflict",
                "newer conflict prevents all older layers of the same path from being attempted");
            check(result.Items.Single(x => x.ShortcutPath == first).Error?.Contains("参数") == true,
                "restore-defaults result retains the actionable per-path conflict reason");
            File.WriteAllBytes(first, beforeEdit); // Explicitly undo only our synthetic external edit.
        }
        using (var service = new ShortcutService(storage))
        {
            var retried = await service.RestoreDefaultsAsync();
            check(retried.Items.Count == 1 && retried.Items[0].Restored && ReadLink(first) == originalFirst,
                "restart and retry resumes a previously conflicted path through every layer");
        }

        // A crash before apply and a crash during restore are reconciled through the same safety path.
        using (var service = new ShortcutService(storage))
        {
            firstLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            secondLayer = await service.ApplyAsync([new(first, Hash(first), ico), new(second, Hash(second), ico)]);
            File.Copy(secondLayer.Items[0].BackupPath, first, true);
            secondLayer.Items[0].State = "pending";
            secondLayer.Items[0].AppliedHash = null;
            // Simulate the other shortcut already restored on disk before the final journal save.
            File.Copy(secondLayer.Items[1].BackupPath, second, true);
            secondLayer.Items[1].State = "restoring";
            Save(storage, secondLayer);
            var recovered = await service.RestoreDefaultsAsync();
            check(recovered.Items.Count == 2 && recovered.Items.All(x => x.Restored) &&
                ReadLink(first) == originalFirst && ReadLink(second) == originalSecond,
                "one-click restore reconciles pending and interrupted-restore journals before older layers");
        }

        // Multiple layers ensure a restored newest layer alone is never reported as restored to default.
        string cancelLinks = Path.Combine(root, "cancel-links"), cancelStorage = Path.Combine(root, "cancel-storage");
        Directory.CreateDirectory(cancelLinks);
        string[] paths = Enumerable.Range(0, 48).Select(i => Path.Combine(cancelLinks, $"{i:D2}.lnk")).ToArray();
        foreach (string path in paths) File.Copy(first, path);
        using (var service = new ShortcutService(cancelStorage))
        {
            firstLayer = await service.ApplyAsync(paths.Select(p => new IconChange(p, Hash(p), ico)).ToArray());
            secondLayer = await service.ApplyAsync(paths.Select(p => new IconChange(p, Hash(p), ico)).ToArray());
            check(firstLayer.Items.Concat(secondLayer.Items).All(x => x.State == "applied"), "cancel fixture has two complete layers");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                string hashBefore = Hash(paths[0]);
                var result = await service.RestoreDefaultsAsync(cancelled.Token);
                check(result.Cancelled && result.Items.Count == paths.Length && result.Items.All(x => !x.Restored) && Hash(paths[0]) == hashBefore,
                    "pre-cancelled restore returns active paths without shortcut writes");
            }
            using var cts = new CancellationTokenSource();
            var restoring = service.RestoreDefaultsAsync(cts.Token);
            bool observedRestoredLayer = false;
            while (!restoring.IsCompleted)
            {
                try
                {
                    var observed = JsonSerializer.Deserialize<OperationBatch>(File.ReadAllText(
                        Path.Combine(cancelStorage, "operations", secondLayer.Id + ".json")));
                    if (observed?.Items.Any(x => x.State == "restored") == true)
                    {
                        observedRestoredLayer = true;
                        cts.Cancel();
                        break;
                    }
                }
                catch (IOException) { }
                await Task.Delay(1);
            }
            var partial = await restoring;
            check(observedRestoredLayer && partial.Cancelled && partial.Items.Count == paths.Length && partial.Items.Any(x => !x.Restored),
                "mid-restore cancellation returns the full path snapshot and incomplete outcomes");
            var history = await service.GetHistoryAsync();
            check(partial.Items.All(result => result.Restored == history.SelectMany(b => b.Items)
                    .Where(x => x.ShortcutPath == result.ShortcutPath).All(x => x.State == "restored")),
                "partial restore success requires every layer to be durably restored");
            check(history.SelectMany(x => x.Items).Any(x => x.State == "restored") &&
                history.SelectMany(x => x.Items).Any(x => x.State == "applied"),
                "cancellation preserves both completed and remaining journal entries");
        }
        using (var restarted = new ShortcutService(cancelStorage))
        {
            var result = await restarted.RestoreDefaultsAsync();
            check(result.Items.Count > 0 && result.Items.All(x => x.Restored) && paths.All(p => ReadLink(p) == originalFirst),
                "after restart, cancelled restoration resumes to original defaults for every path");
            check((await restarted.RestoreDefaultsAsync()).Items.Count == 0, "completed retry leaves no active paths");
        }
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Save(string storage, OperationBatch batch) => File.WriteAllText(
        Path.Combine(storage, "operations", batch.Id + ".json"), JsonSerializer.Serialize(batch));
    private static void CreateLink(string path)
    {
        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        ShortcutFixture.CreateLink(path, target, "/c echo defaults-fixture", Path.GetDirectoryName(path)!,
            "Restore defaults fixture", target + ",0");
    }
    private static (string Target, string Args, string Working, string Description, string Icon) ReadLink(string path)
        => ShortcutFixture.ReadLink(path);
    private static void EditArguments(string path, string args) => ShortcutFixture.EditArguments(path, args);
}
