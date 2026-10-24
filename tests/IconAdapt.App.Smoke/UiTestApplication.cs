using System.Windows;

// Application's constructor queues a virtual OnStartup even when tests never call
// Run(). Intercept it before pumping a dispatcher so production startup cannot
// parse test arguments, create ShortcutService or scan a user's real desktop.
internal static class UiTestApplication
{
    private static IconAdapt.App.App? host;

    public static IconAdapt.App.App EnsureResources()
    {
        if (host != null && ReferenceEquals(Application.Current, host)) return host;
        if (Application.Current != null) throw new InvalidOperationException("UI tests require an isolated application host.");
        var app = new IconAdapt.App.App(launchMainWindow: false);
        app.InitializeComponent();
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        host = app;
        return app;
    }

    public static void VerifyIsolation(Window expected)
    {
        if (Application.Current is not IconAdapt.App.App { UiTestStartupIntercepted: true } app)
            throw new InvalidOperationException("The UI test host did not intercept application startup.");
        if (app.Windows.Count != 1 || !ReferenceEquals(app.Windows[0], expected))
            throw new InvalidOperationException("A UI fixture opened an unexpected application window.");
    }
}
