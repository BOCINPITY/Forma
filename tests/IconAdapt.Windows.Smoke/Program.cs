using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using IconAdapt.Core;
using IconAdapt.Windows;

// Never discovers or writes the real desktop. Fixtures live beneath .local and a uniquely named
// %TEMP% directory for the physical cross-volume regression; no user link is loaded by this test.
string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string testRoot = Path.Combine(repo, ".local", "windows-smoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
string links = Path.Combine(testRoot, "中文 快捷方式");
Directory.CreateDirectory(links);
string storage = Path.Combine(testRoot, "storage");
string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
string shortcut = Path.Combine(links, "中文 application.lnk");
CreateLink(shortcut, target, "/c echo 测试", testRoot, "Smoke 中文说明", target + ",0");
var initialLaunch = ReadLink(shortcut);
string baseline = Hash(shortcut);
using var service = new ShortcutService(storage);
int assertions = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception("FAILED: " + name);
    assertions++;
    Console.WriteLine("PASS: " + name);
}
using var iconStream = new MemoryStream();
SystemIcons.Application.Save(iconStream);
byte[] ico = iconStream.ToArray();
var initial = await service.ScanAsync(links);
Check(initial.Items.Count == 1 && initial.Items[0].IsSupported, "scan custom directory with Chinese/spaces");
Check(initial.Items[0].OriginalIconPng is { Length: > 0 }, "icon extracted");
using (var pngStream = new MemoryStream(initial.Items[0].OriginalIconPng!))
using (var bitmap = new Bitmap(pngStream))
    Check(bitmap.Width == 256 && bitmap.Height == 256, "high-resolution 256 px source extraction");
byte[] originalPng = initial.Items[0].OriginalIconPng!;
Check(Hash(shortcut) == baseline, "scan does not mutate source");
Check(initial.Items[0].SourcePixelWidth == 256 && initial.Items[0].SourcePixelHeight == 256, "scan reports verified native dimensions");

string qualityLinks = Path.Combine(testRoot, "quality-links");
Directory.CreateDirectory(qualityLinks);
byte[] sharp32 = CreatePng(32), sharp256 = CreatePng(256), sharp768 = CreatePng(768);
string lowIco = Path.Combine(testRoot, "native32.ico");
string modernIco = Path.Combine(testRoot, "native256.ico");
string oversizedIco = Path.Combine(testRoot, "native768.ico");
File.WriteAllBytes(lowIco, PngIco((32, sharp32)));
File.WriteAllBytes(modernIco, PngIco((32, sharp32), (256, sharp256)));
File.WriteAllBytes(oversizedIco, PngIco((256, sharp256), (768, sharp768)));
CreateLink(Path.Combine(qualityLinks, "Low.lnk"), target, "", testRoot, "low-native-size", lowIco + ",0");
CreateLink(Path.Combine(qualityLinks, "Modern.lnk"), target, "", testRoot, "original-png-alpha", modernIco + ",0");
CreateLink(Path.Combine(qualityLinks, "Oversized.lnk"), target, "", testRoot, "actual-PNG-dimensions", oversizedIco + ",0");
var quality = await service.ScanAsync(qualityLinks);
var low = quality.Items.Single(x => x.Name == "Low");
Check(low.OriginalIconPng!.AsSpan().SequenceEqual(sharp32) && low.SourcePixelWidth == 32 && low.SourcePixelHeight == 32,
    "32px original stays native; scanner never disguises it as upscaled 256px");
Check(low.QualityWarning?.Contains("32×32") == true, "limited-resolution source has explicit size warning");
var modern = quality.Items.Single(x => x.Name == "Modern");
Check(modern.OriginalIconPng!.AsSpan().SequenceEqual(sharp256), "largest PNG frame retains exact original bytes and straight alpha");
Check(modern.SourcePixelWidth == 256 && modern.QualityWarning is null, "256px native source metadata");
Check(modern.SourceFrames.Count == 2 && modern.SourceFrames.Single(x => x.PixelWidth == 32).Png.AsSpan().SequenceEqual(sharp32) &&
    modern.SourceFrames.Single(x => x.PixelWidth == 256).Png.AsSpan().SequenceEqual(sharp256),
    "native 32px and 256px designs are both preserved byte-for-byte for output size selection");
var oversized = quality.Items.Single(x => x.Name == "Oversized");
Check(oversized.OriginalIconPng!.AsSpan().SequenceEqual(sharp768) && oversized.SourcePixelWidth == 768,
    "resource selection reads actual PNG dimensions beyond ICO directory's 256px marker");
Check(oversized.SourceFrames.Select(x => (x.PixelWidth, x.PixelHeight)).Distinct().Count() == oversized.SourceFrames.Count,
    "native source frames are unique by actual dimensions");
byte[] badHeader = sharp768.ToArray();
badHeader[16] = 255; // A corrupt PNG IHDR must not prevent extraction of a valid companion frame.
string mixedIco = Path.Combine(testRoot, "mixed-frames.ico");
File.WriteAllBytes(mixedIco, PngIco((768, badHeader), (256, sharp256), (256, sharp256), (32, sharp32)));
CreateLink(Path.Combine(qualityLinks, "Mixed.lnk"), target, "", testRoot, "one-bad-frame", mixedIco + ",0");
var mixed = (await service.ScanAsync(qualityLinks)).Items.Single(x => x.Name == "Mixed");
Check(mixed.IsSupported && mixed.SourcePixelWidth == 256 && mixed.SourceFrames.Count == 2,
    "corrupt large PNG frame is skipped, valid frames remain available and duplicates collapse");
Check(mixed.SourceFrames.Count <= 64 && mixed.SourceFrames.Sum(x => (long)x.Png.Length) <= 32L * 1024 * 1024 &&
    mixed.SourceFrames.Sum(x => (long)x.PixelWidth * x.PixelHeight) <= 32L * 1024 * 1024,
    "native frame collection obeys count, byte and pixel budgets");
using (var nativeDibIcon = new MemoryStream(ico))
using (var dib = new Icon(nativeDibIcon))
{
    string dibIco = Path.Combine(testRoot, "native-dib.ico");
    File.WriteAllBytes(dibIco, ico);
    CreateLink(Path.Combine(qualityLinks, "Dib.lnk"), target, "", testRoot, "legacy-DIB", dibIco + ",0");
    var extractedDib = (await service.ScanAsync(qualityLinks)).Items.Single(x => x.Name == "Dib");
    Check(extractedDib.IsSupported && extractedDib.SourcePixelWidth == dib.Width && extractedDib.SourcePixelHeight == dib.Height,
        "legacy DIB extraction preserves native resource dimensions");
}
string corruptIco = Path.Combine(testRoot, "bad-icon.ico");
File.WriteAllBytes(corruptIco, [0, 0, 1, 0, 255, 255]);
CreateLink(Path.Combine(qualityLinks, "BadIcon.lnk"), target, "", testRoot, "invalid-ICO-fallback", corruptIco + ",0");
var badIconInfo = (await service.ScanAsync(qualityLinks)).Items.Single(x => x.Name == "BadIcon");
Check(badIconInfo.OriginalIconPng is null ? !badIconInfo.IsSupported : badIconInfo.QualityWarning?.Contains("无法核实原生分辨率") == true,
    "malformed icon falls back with quality caveat or a visible unsupported reason");
var a = await service.ApplyAsync([new(shortcut, initial.Items[0].FileHash, ico)]);
Check(a.Items[0].State == "applied", "apply succeeds: " + a.Items[0].Error);
Check(File.Exists(a.Items[0].BackupPath) && Hash(a.Items[0].BackupPath) == baseline, "original backup hash");
Check(File.Exists(a.Items[0].GeneratedIconPath), "persistent icon exists");
var launchAfter = ReadLink(shortcut);
Check(launchAfter.Target == initialLaunch.Target && launchAfter.Arguments == initialLaunch.Arguments && launchAfter.WorkingDirectory == initialLaunch.WorkingDirectory && launchAfter.Description == initialLaunch.Description, "launch properties preserved");
Check(launchAfter.Icon == a.Items[0].GeneratedIconPath + ",0", "new icon reference");
var adapted = await service.ScanAsync(links);
Check(adapted.Items[0].OriginalIconPng!.AsSpan().SequenceEqual(originalPng), "rescan previews original icon, avoids repeated framing");
var b = await service.ApplyAsync([new(shortcut, adapted.Items[0].FileHash, ico)]);
Check(b.Items[0].State == "applied", "repeat application creates second layer");
var premature = await service.RestoreAsync(a.Id);
Check(premature.Items[0].State == "conflict", "out-of-order restore detects later batch");
var restoredB = await service.RestoreAsync(b.Id);
Check(restoredB.Items[0].State == "restored", "restore second batch");
Check(ReadLink(shortcut).Icon == a.Items[0].GeneratedIconPath + ",0", "second restore restores first icon");
var restoredA = await service.RestoreAsync(a.Id);
Check(restoredA.Items[0].State == "restored", "restore first batch after second: " + restoredA.Items[0].Error);
Check(ReadLink(shortcut).Icon == initialLaunch.Icon, "original icon restored");
Check((await service.RestoreAsync(a.Id)).Items[0].State == "restored", "restore is idempotent");
var fresh = (await service.ScanAsync(links)).Items[0];
var c = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
EditArguments(shortcut, "/c echo external");
string externalHash = Hash(shortcut);
var conflict = await service.RestoreAsync(c.Id);
Check(conflict.Items[0].State == "conflict" && Hash(shortcut) == externalHash, "external launch modification preserved");
Check(conflict.Items[0].Error!.Contains("参数") && !conflict.Items[0].Error!.Contains("/c echo"), "conflict names changed field without leaking argument values");
var stale = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
Check(stale.Items[0].State == "failed" && Hash(shortcut) == externalHash, "stale scan hash rejected");
fresh = (await service.ScanAsync(links)).Items[0];
var bad = await service.ApplyAsync([new(shortcut, fresh.FileHash, [1, 2, 3])]);
Check(bad.Items[0].State == "failed" && Hash(shortcut) == externalHash, "invalid ICO rejected before mutation");
string missing = Path.Combine(links, "missing.lnk");
var partial = await service.ApplyAsync([new(missing, fresh.FileHash, ico), new(shortcut, fresh.FileHash, ico)]);
Check(partial.Items[0].State == "failed" && partial.Items[1].State == "applied", "partial failure keeps successful item");
Check((await service.RestoreAsync(partial.Id)).Items[1].State == "restored", "partial batch can restore");

// Simulate a crash after desktop write and before journal completion by removing AppliedHash.
fresh = (await service.ScanAsync(links)).Items[0];
var pending = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
pending.Items[0].State = "pending";
pending.Items[0].AppliedHash = null;
SaveJournal(pending);
Check((await service.RestoreAsync(pending.Id)).Items[0].State == "restored", "pending write recovery");

// Simulate a crash after intent was persisted, before any desktop write.
fresh = (await service.ScanAsync(links)).Items[0];
var pendingBefore = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
File.Copy(pendingBefore.Items[0].BackupPath, shortcut, true);
pendingBefore.Items[0].State = "pending";
pendingBefore.Items[0].AppliedHash = null;
SaveJournal(pendingBefore);
using (var locked = new FileStream(shortcut, FileMode.Open, FileAccess.Read, FileShare.None))
    Check((await service.RestoreAsync(pendingBefore.Id)).Items[0].State == "conflict", "locked pending-before-write reports recoverable conflict");
Check((await service.RestoreAsync(pendingBefore.Id)).Items[0].State == "restored", "pending intent recovers after transient lock is released");

// Simulate a crash after a restore's filesystem write but before updating its parent/journal.
fresh = (await service.ScanAsync(links)).Items[0];
var crashParent = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
fresh = (await service.ScanAsync(links)).Items[0];
var crashChild = await service.ApplyAsync([new(shortcut, fresh.FileHash, ico)]);
Check((await service.RestoreAsync(crashChild.Id)).Items[0].State == "restored", "prepare interrupted restore fixture");
SaveJournal(crashParent); // Put back its pre-restore expected hash.
crashChild.Items[0].State = "restoring";
SaveJournal(crashChild);
Check((await service.RestoreAsync(crashChild.Id)).Items[0].State == "restored", "restoring crash window reconciled");
Check((await service.RestoreAsync(crashParent.Id)).Items[0].State == "restored", "parent hash repaired after interrupted child restore");

string readOnly = Path.Combine(links, "Read only.lnk");
CreateLink(readOnly, target, "", testRoot, "readonly", "");
var readOnlyInfo = (await service.ScanAsync(links)).Items.Single(x => x.Path == readOnly);
File.SetAttributes(readOnly, File.GetAttributes(readOnly) | FileAttributes.ReadOnly);
try
{
    var refused = await service.ApplyAsync([new(readOnly, readOnlyInfo.FileHash, ico)]);
    Check(refused.Items[0].State == "failed" && Hash(readOnly) == readOnlyInfo.FileHash, "read-only file fails without mutation");
    Check(refused.Items[0].Error!.Contains("只读") && refused.Items[0].Error!.Contains("ACL") && refused.Items[0].Error!.Contains("原始错误"), "access failure offers actionable permission advice and original detail");
}
finally { File.SetAttributes(readOnly, File.GetAttributes(readOnly) & ~FileAttributes.ReadOnly); }

// The Windows Shell drops HasRelativePath when a link saved on a different volume targets this
// executable. It must pass the icon-only guard while genuine RunAsUser changes must still fail.
var shellType = typeof(ShortcutService).Assembly.GetType("IconAdapt.Windows.ShellInterop")!;
object ReadSnapshot(string path)
{
    object? result = null;
    Sta(() => result = shellType.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [path]));
    return result!;
}
bool SameSnapshot(object before, object after) => (bool)before.GetType().GetMethod("SameLaunch", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(before, [after])!;
object ChangeFlags(object snapshot, uint flag)
{
    var type = snapshot.GetType();
    var constructor = type.GetConstructors().Single(x => x.GetParameters().Length == 10);
    var args = constructor.GetParameters().Select(x => type.GetProperty(x.Name!)!.GetValue(snapshot)).ToArray();
    args[9] = (uint)args[9]! ^ flag;
    return constructor.Invoke(args);
}
var snapshot = ReadSnapshot(shortcut);
Check(SameSnapshot(snapshot, ChangeFlags(snapshot, 0x8)), "HasRelativePath structure-bit normalization is accepted");
Check(!SameSnapshot(snapshot, ChangeFlags(snapshot, 0x2000)), "RunAsUser behavior change remains rejected");
Check(!SameSnapshot(snapshot, ChangeFlags(snapshot, 0x400)), "RunInSeparateProcess behavior change remains rejected");

string sameDriveTarget = Path.Combine(testRoot, "cross-drive-target.exe");
File.Copy(target, sameDriveTarget);
string relativeSeed = Path.Combine(testRoot, "relative-seed.lnk");
CreateLink(relativeSeed, sameDriveTarget, "/c echo cross-volume", testRoot, "Cross-volume regression", "");
Check((BitConverter.ToUInt32(File.ReadAllBytes(relativeSeed), 0x14) & 0x8) != 0, "self-created fixture contains relative-path structure");
if (!string.Equals(Path.GetPathRoot(testRoot), Path.GetPathRoot(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
{
    string crossRoot = Path.Combine(Path.GetTempPath(), "IconAdapt-diagnostics-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(crossRoot);
    string crossShortcut = Path.Combine(crossRoot, "Cross-volume.lnk");
    File.Copy(relativeSeed, crossShortcut);
    object before = ReadSnapshot(crossShortcut);
    using var crossService = new ShortcutService(Path.Combine(testRoot, "cross-volume-storage"));
    var info = (await crossService.ScanAsync(crossRoot)).Items.Single();
    var changed = await crossService.ApplyAsync([new(crossShortcut, info.FileHash, ico)]);
    Check(changed.Items[0].State == "applied", "physical cross-volume shortcut applies despite relative-path normalization: " + changed.Items[0].Error);
    Check((BitConverter.ToUInt32(File.ReadAllBytes(crossShortcut), 0x14) & 0x8) == 0 && SameSnapshot(before, ReadSnapshot(crossShortcut)), "cross-volume Save removes only harmless relative-path metadata");
    Check((await crossService.RestoreAsync(changed.Id)).Items[0].State == "restored", "cross-volume shortcut restores");
    Console.WriteLine("Cross-volume fixture: " + crossRoot);
}
else Console.WriteLine("SKIP physical cross-volume fixture: workspace and TEMP share a volume; structure/behavior comparisons still tested.");

var errorFormatter = typeof(ShortcutService).GetMethod("FormatItemError", BindingFlags.Static | BindingFlags.NonPublic)!;
string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
string ExplainAccess(string path) => (string)errorFormatter.Invoke(null, [new UnauthorizedAccessException("synthetic access detail"), path])!;
if (!string.IsNullOrEmpty(commonDesktop))
{
    Check(ExplainAccess(Path.Combine(commonDesktop, "fixture.lnk")).Contains("公共桌面"), "permission advice recognizes actual common desktop path");
    Check(!ExplainAccess(Path.Combine(commonDesktop + "-sibling", "fixture.lnk")).Contains("公共桌面"), "common desktop boundary excludes similarly named sibling directory");
}
Check(!ExplainAccess(Path.Combine(testRoot, "Public", "Desktop", "fixture.lnk")).Contains("公共桌面"), "permission advice does not rely on directory names");

string document = Path.Combine(testRoot, "text.txt");
File.WriteAllText(document, "sample");
CreateLink(Path.Combine(links, "Document.lnk"), document, "", testRoot, "document", "");
File.WriteAllText(Path.Combine(links, "Corrupt.lnk"), "not a shell link");
string missingTarget = Path.Combine(links, "Missing target.lnk");
CreateLink(missingTarget, Path.Combine(testRoot, "no.exe"), "", testRoot, "missing", "");
var unsupported = await service.ScanAsync(links);
Check(unsupported.Items.Count(x => !x.IsSupported) == 3, "documents, malformed links and missing executables are unsupported");
var unavailable = await service.ScanAsync(Path.Combine(testRoot, "no-directory"));
Check(unavailable.Items.Count == 0 && unavailable.Warnings.Count == 1, "missing directory has visible warning");

var history = await service.GetHistoryAsync();
Check(history.Count >= 7 && history.SequenceEqual(history.OrderByDescending(x => x.CreatedAt)), "persistent history newest first");
using (var cts = new CancellationTokenSource())
{
    cts.Cancel();
    bool cancelled = false;
    try { await service.ScanAsync(links, cts.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled, "cancellation before operation");
}
bool traversalRejected = false;
try { await service.RestoreAsync("..\\outside"); } catch (ArgumentException) { traversalRejected = true; }
Check(traversalRejected, "batch path traversal rejected");

bool backupWriteRejected = false;
try { await service.ApplyAsync([new(a.Items[0].BackupPath, baseline, ico)]); } catch (ArgumentException) { backupWriteRejected = true; }
Check(backupWriteRejected, "internal backup cannot be selected as a shortcut target");

string cancellationLinks = Path.Combine(testRoot, "cancellation links");
string cancellationStorage = Path.Combine(testRoot, "cancellation storage");
Directory.CreateDirectory(cancellationLinks);
var cancellationChanges = new List<IconChange>();
for (int i = 0; i < 64; i++)
{
    string path = Path.Combine(cancellationLinks, i + ".lnk");
    File.Copy(shortcut, path);
    cancellationChanges.Add(new(path, Hash(path), ico));
}
using (var cancellableService = new ShortcutService(cancellationStorage))
using (var cts = new CancellationTokenSource())
{
    var applying = cancellableService.ApplyAsync(cancellationChanges, cts.Token);
    // Cancel only after observing a durably applied entry; exercise the actual partial-change path.
    while (!applying.IsCompleted)
    {
        string operations = Path.Combine(cancellationStorage, "operations");
        if (Directory.Exists(operations))
        {
            foreach (string file in Directory.GetFiles(operations, "*.json"))
            {
                try
                {
                    var observed = JsonSerializer.Deserialize<OperationBatch>(File.ReadAllText(file));
                    if (observed?.Items.Any(x => x.State == "applied") == true) cts.Cancel();
                }
                catch (IOException) { }
            }
        }
        if (cts.IsCancellationRequested) break;
        await Task.Delay(1);
    }
    var cancelledBatch = await applying;
    Check(cancelledBatch.Items.Any(x => x.State == "applied") && cancelledBatch.Items.Any(x => x.State == "cancelled"), "mid-batch cancellation returns applied and cancelled entries");
    var journal = (await cancellableService.GetHistoryAsync()).Single();
    Check(journal.Items.Select(x => x.State).SequenceEqual(cancelledBatch.Items.Select(x => x.State)), "cancellation retains all applied outcomes in durable journal");
    var recoveredCancellation = await cancellableService.RestoreAsync(cancelledBatch.Id);
    Check(recoveredCancellation.Items.All(x => x.State is "restored" or "cancelled"), "cancelled batch remains recoverable");
}

string blockedRoot = Path.Combine(testRoot, "not-a-directory");
File.WriteAllText(blockedRoot, "occupied");
using (var blocked = new ShortcutService(blockedRoot))
{
    bool blockedStorage = false;
    fresh = (await service.ScanAsync(links)).Items.Single(x => x.Path == shortcut);
    string hashBefore = Hash(shortcut);
    try { await blocked.ApplyAsync([new(shortcut, fresh.FileHash, ico)]); } catch (IOException) { blockedStorage = true; }
    Check(blockedStorage && Hash(shortcut) == hashBefore, "unavailable journal storage prevents shortcut writes");
}

// A journal cannot redirect backup reads or generated-icon writes outside its batch storage.
string unsafeJournal = Path.Combine(storage, "operations", pendingBefore.Id + ".json");
string validJournal = File.ReadAllText(unsafeJournal);
pendingBefore.Items[0].BackupPath = shortcut;
SaveJournal(pendingBefore);
bool invalidRecord = false;
try { await service.GetHistoryAsync(); } catch (InvalidDataException) { invalidRecord = true; }
Check(invalidRecord, "malformed journal boundary rejected");
File.WriteAllText(unsafeJournal, validJournal);
using (var parsed = JsonDocument.Parse(validJournal))
{
    var fields = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(validJournal)!;
    fields.Remove("SchemaVersion");
    File.WriteAllText(unsafeJournal, JsonSerializer.Serialize(fields));
    bool missingSchema = false;
    try { await service.GetHistoryAsync(); } catch (InvalidDataException) { missingSchema = true; }
    Check(missingSchema, "missing schema is rejected rather than defaulted");
    File.WriteAllText(unsafeJournal, validJournal);
}
Console.WriteLine($"Windows smoke passed: {assertions} assertions. Test artifacts: {testRoot}");

void SaveJournal(OperationBatch batch) => File.WriteAllText(Path.Combine(storage, "operations", batch.Id + ".json"), JsonSerializer.Serialize(batch));
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

static byte[] CreatePng(int size)
{
    using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
    for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            bitmap.SetPixel(x, y, Color.FromArgb((x + y) % 255 + 1, x % 256, y % 256, (x ^ y) % 256));
    using var stream = new MemoryStream();
    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
    return stream.ToArray();
}

static byte[] PngIco(params (int Size, byte[] Png)[] frames)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream);
    writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)frames.Length);
    int offset = 6 + frames.Length * 16;
    foreach (var frame in frames)
    {
        writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size)); writer.Write((byte)(frame.Size >= 256 ? 0 : frame.Size));
        writer.Write((byte)0); writer.Write((byte)0); writer.Write((ushort)1); writer.Write((ushort)32);
        writer.Write(frame.Png.Length); writer.Write(offset); offset += frame.Png.Length;
    }
    foreach (var frame in frames) writer.Write(frame.Png);
    return stream.ToArray();
}

// WScript.Shell is only the test fixture author/reader. Production uses IShellLink on a dedicated STA.
static void CreateLink(string path, string target, string args, string working, string description, string icon)
{
    Sta(() =>
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? instance = null;
        try
        {
            dynamic script = shell;
            instance = script.CreateShortcut(path);
            dynamic link = instance;
            link.TargetPath = target;
            link.Arguments = args;
            link.WorkingDirectory = working;
            link.Description = description;
            if (icon.Length > 0) link.IconLocation = icon;
            link.Save();
        }
        finally
        {
            if (instance is not null) Marshal.FinalReleaseComObject(instance);
            Marshal.FinalReleaseComObject(shell);
        }
    });
}
static (string Target, string Arguments, string WorkingDirectory, string Description, string Icon) ReadLink(string path)
{
    (string, string, string, string, string) result = default;
    Sta(() =>
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? instance = null;
        try
        {
            dynamic script = shell;
            instance = script.CreateShortcut(path);
            dynamic link = instance;
            result = ((string)link.TargetPath, (string)link.Arguments, (string)link.WorkingDirectory, (string)link.Description, (string)link.IconLocation);
        }
        finally
        {
            if (instance is not null) Marshal.FinalReleaseComObject(instance);
            Marshal.FinalReleaseComObject(shell);
        }
    });
    return result;
}
static void EditArguments(string path, string arguments)
{
    Sta(() =>
    {
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        object? instance = null;
        try
        {
            dynamic script = shell;
            instance = script.CreateShortcut(path);
            dynamic link = instance;
            link.Arguments = arguments;
            link.Save();
        }
        finally
        {
            if (instance is not null) Marshal.FinalReleaseComObject(instance);
            Marshal.FinalReleaseComObject(shell);
        }
    });
}
static void Sta(Action action)
{
    Exception? error = null;
    var thread = new Thread(() => { try { action(); } catch (Exception e) { error = e; } });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (error is not null) throw error;
}
