using System.IO;
using System.Windows;
using ClaudeAccountWidget.Services;
using ClaudeAccountWidget.ViewModels;

namespace ClaudeAccountWidget;

public partial class App : System.Windows.Application
{
    private OverlayWindow? _overlay;
    private Mutex? _singleInstanceMutex;

    public App()
    {
        DispatcherUnhandledException += (_, eventArgs) =>
        {
            WriteErrorLog("dispatcher-error.log", eventArgs.Exception);
            eventArgs.Handled = true;
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _singleInstanceMutex = new Mutex(
                initiallyOwned: true,
                name: "Local\\ClaudeAccountSwitcher.SingleInstance",
                createdNew: out var createdNew);
            if (!createdNew)
            {
                Shutdown();
                return;
            }

            var processes = new ClaudeProcessMonitor();
            var accounts = new ClaudeAccountService(new ClaudeApiClient(), processes);
            var viewModel = new MainViewModel(new ProfileStore(), accounts, processes);

            await viewModel.InitializeAsync(refreshUsage: false);
            _overlay = new OverlayWindow(
                viewModel,
                processes,
                new StartupRegistrationService(),
                new UpdateCheckService(),
                new UpdateInstallerService());
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
        try
        {
            var logRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClaudeAccountWidget");
            Directory.CreateDirectory(logRoot);
            File.WriteAllText(Path.Combine(logRoot, fileName), exception.ToString());
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstanceMutex?.ReleaseMutex(); }
        catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
