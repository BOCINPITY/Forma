using IconAdapt.Core;

namespace IconAdapt.App.Diagnostics;

/// <summary>Turns durable operation outcomes into user-facing advice without hiding uncertain writes.</summary>
public static class OperationFeedback
{
    public static string BatchSummary(OperationBatch batch)
    {
        var count = Count(batch);
        var parts = new List<string> { $"{batch.Items.Count} 项", $"失败 {count.Failed}", $"待核对 {count.Pending}" };
        if (count.Conflicts > 0) parts.Add($"冲突 {count.Conflicts}");
        parts.Add($"已应用 {count.Applied}");
        if (count.Restored > 0) parts.Add($"已恢复 {count.Restored}");
        if (count.Cancelled > 0) parts.Add($"已取消 {count.Cancelled}");
        if (count.Queued > 0) parts.Add($"未开始 {count.Queued}");
        return string.Join(" · ", parts);
    }

    public static string ApplyStatus(OperationBatch batch)
    {
        var count = Count(batch);
        var text = $"应用结果 · 成功 {count.Applied} · 失败 {count.Failed} · 待核对 {count.Pending} · 已取消 {count.Cancelled}";
        if (count.Queued > 0) text += $" · 未开始 {count.Queued}";
        if (count.Conflicts > 0) text += $" · 冲突 {count.Conflicts}";
        return text;
    }

    public static string ApplyNotice(OperationBatch batch)
    {
        var count = Count(batch);
        var parts = new List<string>();
        if (count.Applied == 0) parts.Add("本批没有图标被确认应用成功。");
        if (count.Failed > 0) parts.Add($"{count.Failed} 项应用失败，请在恢复记录中查看原因；不要重复应用已成功项。");
        var permissionItems = batch.Items.Where(x => x.State != "applied" && IsAccessDenied(x.Error)).ToArray();
        if (permissionItems.Any(x => IsPublicDesktop(x.ShortcutPath))) parts.Add(PublicDesktopAdvice);
        if (permissionItems.Any(x => !IsPublicDesktop(x.ShortcutPath))) parts.Add(ProtectedItemAdvice);
        var preserved = batch.Items.Count(x => x.State is "pending" or "failed" && IsProtectionAbort(x.Error));
        if (preserved > 0) parts.Add($"{preserved} 项属性校验未通过，已中止，原快捷方式保留；可在恢复记录中核对或恢复。");
        var uncertain = batch.Items.Count(x => x.State is "pending" or "restoring" && !IsProtectionAbort(x.Error));
        if (uncertain > 0) parts.Add($"{uncertain} 项状态待核对，不能视为成功。请先查看恢复记录，必要时恢复后再扫描。");
        if (count.Cancelled > 0) parts.Add($"{count.Cancelled} 项已取消，尚未修改。");
        if (count.Queued > 0) parts.Add($"{count.Queued} 项尚未开始，请核对操作记录后重新扫描。");
        if (count.Conflicts > 0) parts.Add($"{count.Conflicts} 项有冲突，已保留当前文件，请先在恢复记录中核对。");
        if (count.Applied > 0) parts.Add($"仅 {count.Applied} 项已确认成功并通知 Windows 更新。若这些项目尚未显示，可在桌面按 F5 刷新。");
        return string.Join("\n", parts);
    }

    public static string ItemAdvice(string? error, string shortcutPath, string? state = null)
    {
        if (IsAccessDenied(error)) return IsPublicDesktop(shortcutPath) ? PublicDesktopAdvice : ProtectedItemAdvice;
        if (IsProtectionAbort(error)) return "属性校验未通过，已中止，原快捷方式保留；可在恢复记录中核对或恢复。请使用新版程序重新扫描后，仅重试这些项目。";
        if (!string.IsNullOrWhiteSpace(error)) return error;
        return state switch
        {
            "pending" or "restoring" => "操作状态待核对，不能视为成功。请核对本批次或尝试恢复；原始备份仍保留。",
            "cancelled" => "已取消，尚未修改。",
            "queued" => "尚未开始，未确认修改。",
            "applied" => "已应用，原始快捷方式已备份，可在恢复记录中还原。",
            "restored" => "原始图标已恢复。",
            _ => shortcutPath
        };
    }

    public const string PublicDesktopAdvice = "公共桌面快捷方式需要管理员权限。普通模式下请先取消勾选这些受保护项；若要修改，请先关闭程序，右键以管理员身份运行，再重新扫描，仅勾选失败项重试。";
    public const string ProtectedItemAdvice = "没有权限修改此快捷方式。普通模式下请先取消勾选受保护项；如需修改，请确认文件权限，或先关闭程序，右键以管理员身份运行，再重新扫描，仅勾选失败项重试。";

    public static bool IsAccessDenied(string? error)
    {
        if (string.IsNullOrWhiteSpace(error)) return false;
        return Has(error, "access") && Has(error, "denied") || Has(error, "80070005") || Has(error, "拒绝访问") ||
            Has(error, "访问被拒绝") || Has(error, "权限不足") || Has(error, "没有权限") || Has(error, "需要管理员权限");
    }

    public static bool IsProtectionAbort(string? error) => !string.IsNullOrWhiteSpace(error) &&
        (Has(error, "未能保留快捷方式属性") || Has(error, "属性校验未通过") || Has(error, "未能保留原有启动属性"));

    private static bool IsPublicDesktop(string path)
    {
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        return !string.IsNullOrEmpty(common) && path.StartsWith(common.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Has(string value, string match) => value.Contains(match, StringComparison.OrdinalIgnoreCase);
    private static Counts Count(OperationBatch batch) => new(
        batch.Items.Count(x => x.State == "applied"), batch.Items.Count(x => x.State is "failed" or "restore_failed"),
        batch.Items.Count(x => x.State is "pending" or "restoring"), batch.Items.Count(x => x.State == "cancelled"),
        batch.Items.Count(x => x.State == "queued"), batch.Items.Count(x => x.State == "restored"), batch.Items.Count(x => x.State == "conflict"));
    private sealed record Counts(int Applied, int Failed, int Pending, int Cancelled, int Queued, int Restored, int Conflicts);
}
