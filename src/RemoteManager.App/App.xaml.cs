using System.Windows;
using RemoteManager.Core.Logging;

namespace RemoteManager.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        var logger = LogEngine.Instance;
        logger.Info("App", $"=== RemoteManager starting at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        logger.Info("App", $"OS: {Environment.OSVersion}, 64-bit: {Environment.Is64BitOperatingSystem}, .NET: {Environment.Version}");
        logger.Info("App", $"Logs directory: {logger.LogsDirectory}");

        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            if (args.ExceptionObject is Exception ex)
            {
                logger.Fatal("App", "Unhandled AppDomain exception occurred", ex);
            }
            else
            {
                logger.Fatal("App", $"Unhandled AppDomain exception object: {args.ExceptionObject}");
            }
        };

        DispatcherUnhandledException += (s, args) =>
        {
            logger.Error("App", "Unhandled UI Dispatcher exception", args.Exception);
        };

        TaskScheduler.UnobservedTaskException += (s, args) =>
        {
            logger.Error("App", "Unobserved background task exception", args.Exception);
            args.SetObserved();
        };

        try
        {
            logger.Debug("App", "Applying system theme...");
            Wpf.Ui.Appearance.ApplicationThemeManager.ApplySystemTheme();
            logger.Info("App", "System theme applied successfully.");
        }
        catch (Exception ex)
        {
            logger.Warn("App", "Failed to apply system theme", ex);
        }

        try
        {
            logger.Debug("App", "Creating MainWindow instance...");
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;
            mainWindow.Show();
            logger.Info("App", "MainWindow displayed successfully.");
        }
        catch (Exception ex)
        {
            logger.Fatal("App", "Fatal error initializing MainWindow", ex);
            throw;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        LogEngine.Instance.Info("App", $"RemoteManager exiting with code {e.ApplicationExitCode}.");
        LogEngine.Instance.Dispose();
        base.OnExit(e);
    }
}
