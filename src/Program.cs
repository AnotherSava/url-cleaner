namespace UrlCleaner;

static class Program
{
    // Local\ scopes the mutex to this Windows session. The name is the app's own, since mutex names are shared by
    // every program in the session.
    private const string InstanceMutexName = @"Local\AnotherUrlCleaner.SingleInstance";

    /// <summary>
    ///  The main entry point for the application.
    /// </summary>
    [STAThread]
    static void Main(string[] args)
    {
        string? configPath = null;
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] is "--config" or "-c")
            {
                configPath = args[i + 1];
                break;
            }
        }

        // One instance only: a second one would handle every copy twice. It exits before starting the logger,
        // which would otherwise rotate the running instance's log.
        using var instance = new Mutex(initiallyOwned: true, InstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
            return;

        var configFilePath = AppConfig.ResolvePath(configPath);
        Logger.Start(Path.GetDirectoryName(configFilePath)!);
        Logger.Info($"Started with {configFilePath}");

        // A tray app has no window to show WinForms' unhandled-exception dialog over, so exceptions go to the log.
        TrayApplicationContext? context = null;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            Logger.Error("Unhandled exception", e.Exception);
            context?.ShowNotice(Notices.UnexpectedError(e.Exception));
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Logger.Error("Unhandled exception, exiting", e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        context = new TrayApplicationContext(configFilePath);
        Application.Run(context);
    }
}
