namespace UrlCleaner;

static class Program
{
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

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApplicationContext(configPath));
    }    
}