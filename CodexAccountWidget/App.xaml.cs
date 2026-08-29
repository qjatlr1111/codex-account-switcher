using System.IO;
using System.Windows;
using CodexAccountWidget.Services;
using CodexAccountWidget.ViewModels;

namespace CodexAccountWidget;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            WriteErrorLog("dispatcher-error.log", eventArgs.Exception);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "Local\\CodexAccountSwitcher.SingleInstance",
                createdNew: out var createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            var store = new ProfileStore();
            var accounts = new CodexAccountService();
            var restarter = new CodexDesktopRestartService();
            var viewModel = new MainViewModel(store, accounts, restarter);

            await viewModel.InitializeAsync(refreshUsage: false);
            _overlay = new OverlayWindow(viewModel, restarter, new StartupRegistrationService());
            await _overlay.InitializeAsync();
        }
        catch (Exception exception)
        {
            WriteErrorLog("startup-error.log", exception);
            Shutdown(-1);
        }
    }

    private static void WriteErrorLog(string fileName, Exception exception)
    {
        var logRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexAccountWidget");
        Directory.CreateDirectory(logRoot);
        File.WriteAllText(Path.Combine(logRoot, fileName), exception.ToString());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); }
        catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
