using IconAdapt.Tests;
using System.Security.Cryptography;
using IconAdapt.Core;
using IconAdapt.Imaging;
using IconAdapt.Windows;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            Run();
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"INTEGRATION FAIL: {error}");
            return 1;
        }
    }

    private static void Run()
    {
        FeedbackChecks.Run();
        PixelGeometryChecks.Run();
        var root = FindRepositoryRoot();
        var fixtureRoot = Path.Combine(root, ".local", "integration-smoke", Guid.NewGuid().ToString("N"));
        var shortcutRoot = Path.Combine(fixtureRoot, "中文 测试 shortcuts");
        var dataRoot = Path.Combine(fixtureRoot, "data");
        Directory.CreateDirectory(shortcutRoot);
        var shortcut = Path.Combine(shortcutRoot, "记事本 测试.lnk");
        var untouchedShortcut = Path.Combine(shortcutRoot, "保持不变.lnk");
        var target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
        CreateShortcut(shortcut, target, shortcutRoot);
        CreateShortcut(untouchedShortcut, target, shortcutRoot);
        var before = ReadProperties(shortcut);
        var originalHash = Hash(shortcut);
        var untouchedHash = Hash(untouchedShortcut);
        string batchId;

        using (var service = new ShortcutService(dataRoot))
        {
            var scan = service.ScanAsync(shortcutRoot).GetAwaiter().GetResult();
            Require(scan.Items.Count == 2, "Both fixture shortcuts must be discovered.");
            var item = scan.Items.Single(item => item.Path.Equals(shortcut, StringComparison.OrdinalIgnoreCase));
            Require(item.IsSupported && item.OriginalIconPng is { Length: > 0 }, "Fixture icon must be readable.");
            Require(Hash(shortcut) == originalHash, "Read-only scan must preserve shortcut bytes.");

            Require(item.SourceFrames.Count > 0, "Native source frames must be retained for the renderer.");
            var rendered = new IconRenderer().Render(item.OriginalIconPng!, new IconStyle(), item.SourceFrames);
            Require(rendered.PreviewPng.Length > 0 && rendered.IconFile.Length > 0, "Renderer must produce preview and ICO.");
            File.WriteAllBytes(Path.Combine(fixtureRoot, "preview.png"), rendered.PreviewPng);
            var batch = service.ApplyAsync([new IconChange(item.Path, item.FileHash, rendered.IconFile)]).GetAwaiter().GetResult();
            Require(batch.Items.Count == 1 && batch.Items[0].State == "applied", "Rendered icon must apply successfully.");
            batchId = batch.Id;
            var after = ReadProperties(shortcut);
            Require(after.Target == before.Target && after.Arguments == before.Arguments && after.WorkingDirectory == before.WorkingDirectory && after.Description == before.Description,
                "Applying icon must preserve launch properties.");
            Require(after.Icon != before.Icon && File.Exists(batch.Items[0].GeneratedIconPath), "Shortcut must reference a persistent generated icon.");
            Require(File.Exists(batch.Items[0].BackupPath), "An original backup must exist.");
            Require(Hash(untouchedShortcut) == untouchedHash, "Unselected shortcut must remain byte-identical.");
            var rescan = service.ScanAsync(shortcutRoot).GetAwaiter().GetResult();
            var managedItem = rescan.Items.Single(candidate => candidate.Path.Equals(shortcut, StringComparison.OrdinalIgnoreCase));
            Require(managedItem.OriginalIconPng!.SequenceEqual(item.OriginalIconPng!), "Repeated scan must return the original artwork, avoiding nested frames.");
        }

        using (var restartedService = new ShortcutService(dataRoot))
        {
            var history = restartedService.GetHistoryAsync().GetAwaiter().GetResult();
            Require(history.Any(batch => batch.Id == batchId), "History must survive service restart.");
            var result = restartedService.RestoreAsync(batchId).GetAwaiter().GetResult();
            Require(result.Items.Single().State == "restored", "A restarted service must restore the batch.");
            Require(ReadProperties(shortcut) == before, "Restore must recover original icon and all launch properties.");
            var repeated = restartedService.RestoreAsync(batchId).GetAwaiter().GetResult();
            Require(repeated.Items.Single().State == "restored", "Restore must be idempotent.");
            Require(Hash(untouchedShortcut) == untouchedHash, "Restoring must not touch an unselected shortcut.");

            var desktopScan = restartedService.ScanAsync().GetAwaiter().GetResult();
            Console.WriteLine($"Read-only real desktop scan: {desktopScan.Items.Count} shortcuts, {desktopScan.Warnings.Count} warnings. No desktop files modified.");
        }

        Console.WriteLine("INTEGRATION PASS: source extraction -> render -> apply -> persistent history -> restart -> restore; launch properties and unselected file preserved.");
    }

    private static void CreateShortcut(string path, string target, string workingDirectory)
        => ShortcutFixture.CreateLink(path, target, "\"测试 文件.txt\"", workingDirectory,
            "IconAdapt integration fixture", "");

    private static ShortcutProperties ReadProperties(string path)
    {
        var link = ShortcutFixture.ReadLink(path);
        return new(link.Target, link.Arguments, link.WorkingDirectory, link.Description, link.Icon);
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "IconAdapt.slnx"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed record ShortcutProperties(string Target, string Arguments, string WorkingDirectory, string Description, string Icon);
}
