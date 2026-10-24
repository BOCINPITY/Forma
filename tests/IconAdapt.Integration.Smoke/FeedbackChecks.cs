using IconAdapt.App.Diagnostics;
using IconAdapt.Core;

internal static class FeedbackChecks
{
    public static void Run()
    {
        string commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        Check(!string.IsNullOrEmpty(commonDesktop), "Feedback checks require a Windows public desktop path.");
        string publicLink = Path.Combine(commonDesktop, "Diagnostic example.lnk");
        string privateLink = Path.Combine(Path.GetTempPath(), "IconAdapt-feedback-example.lnk");
        const string denied = "Access to the path is denied.";
        const string protection = "Shell 未能保留快捷方式属性，已中止修改。";

        var accident = new OperationBatch();
        accident.Items.AddRange(Enumerable.Range(0, 14).Select(_ => Item("applied", privateLink)));
        accident.Items.AddRange(Enumerable.Range(0, 13).Select(_ => Item("failed", publicLink, denied)));
        accident.Items.AddRange(Enumerable.Range(0, 4).Select(_ => Item("pending", privateLink, protection)));
        string status = OperationFeedback.ApplyStatus(accident);
        Check(status.Contains("成功 14") && status.Contains("失败 13") && status.Contains("待核对 4"), "Mixed outcome must report the actual 14/13/4 split.");
        string summary = OperationFeedback.BatchSummary(accident);
        Check(summary.IndexOf("失败 13", StringComparison.Ordinal) < summary.IndexOf("已应用 14", StringComparison.Ordinal) && summary.Contains("待核对 4"), "History must put failed and unresolved counts before successful outcomes.");
        string notice = OperationFeedback.ApplyNotice(accident);
        Check(notice.Contains("公共桌面") && notice.Contains("管理员") && notice.Contains("先关闭程序") && notice.Contains("取消勾选") && notice.Contains("仅勾选失败项"), "Permission errors require actionable normal and elevated-mode advice.");
        Check(notice.Contains("4 项属性校验") && notice.Contains("原快捷方式保留") && notice.Contains("仅 14 项"), "Pre-write protection and confirmed successful items must remain distinct.");

        var allFailed = new OperationBatch { Items = [Item("failed", publicLink, denied)] };
        notice = OperationFeedback.ApplyNotice(allFailed);
        Check(notice.Contains("没有图标被确认应用成功") && !notice.Contains("通知 Windows") && !notice.Contains("F5"), "All-failed batches must not suggest a successful update or refreshing the desktop.");

        var cancelled = new OperationBatch { Items = [Item("cancelled", privateLink), Item("cancelled", privateLink)] };
        notice = OperationFeedback.ApplyNotice(cancelled);
        Check(notice.Contains("2 项已取消") && !notice.Contains("通知 Windows") && !notice.Contains("已确认成功并"), "Cancellation must not be presented as a successful application.");

        var unresolved = new OperationBatch
        {
            Items = [Item("failed", privateLink, protection), Item("pending", privateLink, "Disk write failed after shortcut replacement")]
        };
        notice = OperationFeedback.ApplyNotice(unresolved);
        Check(notice.Contains("1 项状态待核对"), "A different protected failure must not hide an uncertain write in the same batch.");
        string unknownPending = OperationFeedback.ItemAdvice("Disk write failed after shortcut replacement", privateLink, "pending");
        Check(!unknownPending.Contains("原快捷方式保留") && !unknownPending.Contains("尚未修改"), "Unknown pending errors cannot promise that the shortcut was unchanged.");
        Check(OperationFeedback.ItemAdvice(null, privateLink, "restoring").Contains("待核对"), "Interrupted restoration also needs reconciliation advice.");

        Check(OperationFeedback.ItemAdvice(denied, publicLink).Contains("公共桌面"), "Old English access-denied errors must be translated.");
        Check(OperationFeedback.ItemAdvice("权限不足，无法修改此快捷方式。", publicLink).Contains("公共桌面"), "New Chinese permission errors must keep the same actionable advice.");
        Check(!OperationFeedback.ItemAdvice(denied, privateLink).Contains("公共桌面"), "A private permission error must not be diagnosed as the public desktop.");
        string similarDirectory = commonDesktop.TrimEnd('\\') + "-copy\\Example.lnk";
        Check(!OperationFeedback.ItemAdvice(denied, similarDirectory).Contains("公共桌面"), "Public desktop matching must respect the complete directory boundary.");
        string fakeNestedDirectory = Path.Combine(Path.GetTempPath(), "fixture", "Users", "Public", "Desktop", "Example.lnk");
        Check(!OperationFeedback.ItemAdvice(denied, fakeNestedDirectory).Contains("公共桌面"), "A fixture containing Users/Public/Desktop must not be treated as the system public desktop.");
        Check(accident.Items[14].Error == denied, "Formatting must preserve the original error used by detailed UI tooltips.");

        var success = new OperationBatch { Items = [Item("applied", privateLink)] };
        notice = OperationFeedback.ApplyNotice(success);
        Check(notice.Contains("1 项已确认成功并通知 Windows") && !notice.Contains("应用失败"), "Successful batches should retain an accurate desktop-update explanation.");
        Check(!notice.Contains("恢复记录") && !OperationFeedback.ApplyNotice(accident).Contains("恢复记录"),
            "Current feedback must not refer to the removed history page.");
        string restoreDenied = OperationFeedback.RestoreAdvice(denied);
        Check(restoreDenied.Contains("恢复默认图标") && restoreDenied.Contains(denied) && !restoreDenied.Contains("勾选"),
            "Restore permissions must offer restore-specific retry guidance and retain the original error.");
        Console.WriteLine("PASS: operation feedback regression branches (mixed, failed, cancelled, uncertain, permissions and raw errors)");
    }

    private static OperationItem Item(string state, string path, string? error = null) => new() { State = state, ShortcutPath = path, Error = error };
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
