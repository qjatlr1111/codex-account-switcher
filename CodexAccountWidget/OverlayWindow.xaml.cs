using System.Diagnostics;
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CodexAccountWidget.Services;
using CodexAccountWidget.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace CodexAccountWidget;

public partial class OverlayWindow : Window
{
    private const int GwlExStyle = -20;
    private const int GwlHwndParent = -8;
    private const long WsExToolWindow = 0x00000080L;
    private const long WsExNoActivate = 0x08000000L;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MainViewModel _viewModel;
    private readonly CodexDesktopRestartService _codexDesktop;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly UpdateCheckService _updateCheckService;
    private readonly UpdateInstallerService _updateInstallerService;
    private readonly bool _diagnosticMode;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _liveUsageTimer;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private readonly SemaphoreSlim _liveUsageGate = new(1, 1);
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private DateTime _nextLiveUsageReconnectUtc;
    private TimeSpan _liveUsageReconnectDelay = TimeSpan.FromSeconds(10);
    private CodexAppServerClient? _liveUsageClient;
    private string? _liveUsageProfileId;
    private AccountPanelWindow? _panel;
    private WidgetMenuWindow? _widgetMenu;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _autoVisibilityMenuItem;
    private readonly Forms.ToolStripMenuItem _startupMenuItem;
    private readonly Forms.ToolStripMenuItem _autoContrastMenuItem;
    private readonly Forms.ToolStripMenuItem _updateMenuItem;
    private UpdateCheckResult? _availableUpdate;
    private bool _isInstallingUpdate;
    private bool _isUserHidden;
    private bool _isManuallyShown;
    private bool _codexWindowPresent;
    private DateTime _restartGraceUntil;
    private bool? _usingDarkWidgetText;
    private bool _hasTaskbarBounds;
    private TaskbarLocator.NativeRect _lastTaskbarBounds;

    public OverlayWindow(
        MainViewModel viewModel,
        CodexDesktopRestartService codexDesktop,
        StartupRegistrationService startupRegistration,
        UpdateCheckService updateCheckService,
        UpdateInstallerService updateInstallerService)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _codexDesktop = codexDesktop;
        _startupRegistration = startupRegistration;
        _updateCheckService = updateCheckService;
        _updateInstallerService = updateInstallerService;
        _diagnosticMode = Environment.GetCommandLineArgs()
            .Any(argument => argument.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase));
        if (_diagnosticMode) ShowInTaskbar = true;

        SourceInitialized += (_, _) => ConfigureWindow();

        _visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _visibilityTimer.Tick += async (_, _) => await MonitorCodexAsync();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(2) };
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_codexWindowPresent && IsVisible && !_viewModel.IsSwitching)
                await _viewModel.RefreshAllAsync();
        };

        _liveUsageTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _liveUsageTimer.Tick += async (_, _) => await SyncActiveUsageAsync();
        _viewModel.PrepareForConnectionSwitchAsync = PauseLiveUsageForSwitchAsync;
        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);

        (_trayIcon, _autoVisibilityMenuItem, _startupMenuItem, _autoContrastMenuItem,
            _updateMenuItem) = CreateTrayIcon();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            if (_viewModel.PrepareForConnectionSwitchAsync == PauseLiveUsageForSwitchAsync)
                _viewModel.PrepareForConnectionSwitchAsync = null;
            _updateCheckTimer.Stop();
            _trayIcon.Dispose();
        };
    }

    public async Task InitializeAsync()
    {
        if (!_diagnosticMode && !_startupRegistration.TrySetEnabled(_viewModel.StartWithWindows))
            await _viewModel.SetStartWithWindowsAsync(_startupRegistration.IsEnabled());

        _startupMenuItem.Checked = _startupRegistration.IsEnabled();
        _autoVisibilityMenuItem.Checked = _viewModel.ShowOnlyWhileCodexIsRunning;
        _autoContrastMenuItem.Checked = _viewModel.AutoAdjustWidgetTextColor;
        _visibilityTimer.Start();
        _refreshTimer.Start();
        _liveUsageTimer.Start();
        _updateCheckTimer.Start();
        _ = CheckForUpdatesAsync(manual: false);

        if (_diagnosticMode)
        {
            ShowWidgetCore();
            return;
        }

        await MonitorCodexAsync();
        await SyncActiveUsageAsync();
    }

    private async Task SyncActiveUsageAsync()
    {
        var profile = _viewModel.ActiveProfile;
        if (!_codexWindowPresent || !IsVisible || _viewModel.IsSwitching || profile is null ||
            (_liveUsageClient is null && DateTime.UtcNow < _nextLiveUsageReconnectUtc) ||
            !await _liveUsageGate.WaitAsync(0)) return;

        try
        {
            if (_liveUsageClient is null || _liveUsageProfileId != profile.Id)
            {
                await DisposeLiveUsageClientAsync();
                var defaultHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".codex");
                _liveUsageClient = new CodexAppServerClient(defaultHome);
                await _liveUsageClient.StartAsync();
                _liveUsageProfileId = profile.Id;
            }

            await _viewModel.RefreshActiveFromCurrentAccountAsync(_liveUsageClient);
            _nextLiveUsageReconnectUtc = default;
            _liveUsageReconnectDelay = TimeSpan.FromSeconds(10);
        }
        catch
        {
            // 일시적인 연결 오류에서는 기존 사용량을 유지하고 다음 주기에 재연결합니다.
            await DisposeLiveUsageClientAsync();
            _nextLiveUsageReconnectUtc = DateTime.UtcNow + _liveUsageReconnectDelay;
            _liveUsageReconnectDelay = TimeSpan.FromSeconds(
                Math.Min(_liveUsageReconnectDelay.TotalSeconds * 2, 120));
        }
        finally
        {
            _liveUsageGate.Release();
        }
    }

    private async Task DisposeLiveUsageClientAsync()
    {
        if (_liveUsageClient is not null) await _liveUsageClient.DisposeAsync();
        _liveUsageClient = null;
        _liveUsageProfileId = null;
    }

    private async Task PauseLiveUsageForSwitchAsync()
    {
        _liveUsageTimer.Stop();
        await _liveUsageGate.WaitAsync();
        try
        {
            await DisposeLiveUsageClientAsync();
            _nextLiveUsageReconnectUtc = default;
            _liveUsageReconnectDelay = TimeSpan.FromSeconds(10);
        }
        finally
        {
            _liveUsageGate.Release();
        }
    }

    private (Forms.NotifyIcon TrayIcon, Forms.ToolStripMenuItem AutoVisibility,
        Forms.ToolStripMenuItem Startup, Forms.ToolStripMenuItem AutoContrast,
        Forms.ToolStripMenuItem Update) CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("위젯 표시", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autoVisibility = new Forms.ToolStripMenuItem("Codex 실행 중에만 표시")
        {
            Checked = _viewModel.ShowOnlyWhileCodexIsRunning
        };
        autoVisibility.Click += (_, _) => Dispatcher.InvokeAsync(ToggleAutoVisibilityAsync);
        menu.Items.Add(autoVisibility);

        var autoContrast = new Forms.ToolStripMenuItem("배경에 맞춰 글자색 자동 조절")
        {
            Checked = _viewModel.AutoAdjustWidgetTextColor
        };
        autoContrast.Click += (_, _) => Dispatcher.InvokeAsync(ToggleAutoContrastAsync);
        menu.Items.Add(autoContrast);

        var startup = new Forms.ToolStripMenuItem("Windows 시작 시 자동 실행")
        {
            Checked = _startupRegistration.IsEnabled()
        };
        startup.Click += (_, _) => Dispatcher.InvokeAsync(ToggleStartupAsync);
        menu.Items.Add(startup);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var update = new Forms.ToolStripMenuItem("업데이트 확인");
        update.Click += (_, _) => Dispatcher.InvokeAsync(OnUpdateMenuClickedAsync);
        menu.Items.Add(update);
        menu.Items.Add("정보", null, (_, _) => Dispatcher.Invoke(ShowAboutWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("끝내기", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var trayIcon = new Forms.NotifyIcon
        {
            Text = "Codex 계정 위젯",
            Icon = Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
        trayIcon.BalloonTipClicked += (_, _) =>
            Dispatcher.InvokeAsync(InstallAvailableUpdateAsync);
        return (trayIcon, autoVisibility, startup, autoContrast, update);
    }

    private async Task OnUpdateMenuClickedAsync()
    {
        if (_availableUpdate is not null)
        {
            await InstallAvailableUpdateAsync();
            return;
        }

        await CheckForUpdatesAsync(manual: true);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (!await _updateCheckGate.WaitAsync(0)) return;

        try
        {
            var result = await _updateCheckService.CheckAsync();
            if (!result.IsUpdateAvailable)
            {
                if (manual)
                    _trayIcon.ShowBalloonTip(4000, "업데이트 확인",
                        "현재 최신 버전을 사용하고 있습니다.", Forms.ToolTipIcon.Info);
                return;
            }

            _availableUpdate = result;
            _updateMenuItem.Text = $"{result.LatestTagName} 업데이트 설치";
            _trayIcon.ShowBalloonTip(7000, "새 업데이트가 있습니다",
                $"Codex Account Switcher {result.LatestTagName}을 사용할 수 있습니다.",
                Forms.ToolTipIcon.Info);
        }
        catch
        {
            if (manual)
                _trayIcon.ShowBalloonTip(4000, "업데이트 확인 실패",
                    "GitHub에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.",
                    Forms.ToolTipIcon.Warning);
        }
        finally
        {
            _updateCheckGate.Release();
        }
    }

    private async Task InstallAvailableUpdateAsync()
    {
        if (_availableUpdate is null || _isInstallingUpdate) return;
        var answer = System.Windows.MessageBox.Show(
            $"{_availableUpdate.LatestTagName}을 다운로드하고 설치할까요?\n" +
            "설치 파일의 SHA-256을 확인한 후 앱이 자동으로 다시 시작됩니다.",
            "Codex Account Switcher 업데이트",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;

        _isInstallingUpdate = true;
        _updateMenuItem.Enabled = false;
        _updateMenuItem.Text = "업데이트 다운로드 중...";
        try
        {
            var installerPath = await _updateInstallerService.DownloadVerifiedAsync(_availableUpdate);
            _updateInstallerService.Launch(installerPath);
        }
        catch (Exception exception)
        {
            _isInstallingUpdate = false;
            _updateMenuItem.Enabled = true;
            _updateMenuItem.Text = $"{_availableUpdate.LatestTagName} 업데이트 설치";
            System.Windows.MessageBox.Show(
                $"업데이트를 설치하지 못했습니다.\n{exception.Message}",
                "업데이트 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowAboutWindow()
    {
        var about = new AboutWindow { Owner = this };
        about.ShowDialog();
    }

    private async Task ToggleAutoVisibilityAsync()
    {
        var enabled = !_viewModel.ShowOnlyWhileCodexIsRunning;
        await _viewModel.SetShowOnlyWhileCodexIsRunningAsync(enabled);
        _autoVisibilityMenuItem.Checked = enabled;
        _isUserHidden = false;
        _isManuallyShown = false;
        await MonitorCodexAsync();
    }

    private async Task ToggleStartupAsync()
    {
        var enabled = !_startupRegistration.IsEnabled();
        if (!_startupRegistration.TrySetEnabled(enabled))
        {
            _startupMenuItem.Checked = _startupRegistration.IsEnabled();
            return;
        }

        _startupMenuItem.Checked = enabled;
        await _viewModel.SetStartWithWindowsAsync(enabled);
    }

    private async Task ToggleAutoContrastAsync()
    {
        var enabled = !_viewModel.AutoAdjustWidgetTextColor;
        await _viewModel.SetAutoAdjustWidgetTextColorAsync(enabled);
        _autoContrastMenuItem.Checked = enabled;
    }

    private void ConfigureWindow()
    {
        if (_diagnosticMode) return;
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExToolWindow | WsExNoActivate));
        AttachToTaskbar(handle);
    }

    private async Task MonitorCodexAsync()
    {
        if (!await _visibilityGate.WaitAsync(0)) return;

        try
        {
            if (_diagnosticMode)
            {
                if (IsVisible) PositionOverTaskbar();
                return;
            }

            var wasPresent = _codexWindowPresent;
            _codexWindowPresent = await Task.Run(_codexDesktop.HasRunningWindow);

            if (wasPresent && !_codexWindowPresent)
            {
                _isUserHidden = false;
                _isManuallyShown = false;
            }

            var inRestartGrace = _viewModel.IsSwitching || DateTime.UtcNow < _restartGraceUntil;
            var shouldShow = _viewModel.ShowOnlyWhileCodexIsRunning
                ? _viewModel.HasSwitchError || _isManuallyShown || inRestartGrace ||
                  (_codexWindowPresent && !_isUserHidden)
                : !_isUserHidden;

            if (!shouldShow)
            {
                HideWidgetCore();
                return;
            }

            var becameVisible = ShowWidgetCore();
            if (becameVisible && _codexWindowPresent)
                await _viewModel.RefreshAllAsync();
        }
        finally
        {
            _visibilityGate.Release();
        }
    }

    private void PositionOverTaskbar(bool force = false)
    {
        if (!IsVisible) return;

        if (!TaskbarLocator.TryGetPrimaryTaskbar(out var taskbarHandle, out var taskbar))
        {
            HideWidgetCore();
            return;
        }

        var boundsChanged = !_hasTaskbarBounds ||
                            taskbar.Left != _lastTaskbarBounds.Left ||
                            taskbar.Top != _lastTaskbarBounds.Top ||
                            taskbar.Right != _lastTaskbarBounds.Right ||
                            taskbar.Bottom != _lastTaskbarBounds.Bottom;
        if (!force && !boundsChanged) return;
        _lastTaskbarBounds = taskbar;
        _hasTaskbarBounds = true;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(taskbar.Left, taskbar.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(taskbar.Right, taskbar.Bottom));
        var taskbarHeight = bottomRight.Y - topLeft.Y;

        Height = Math.Max(34, taskbarHeight);
        Left = topLeft.X;
        Top = topLeft.Y;

        if (!IsVisible) Show();

        var handle = new WindowInteropHelper(this).Handle;
        AttachToTaskbar(handle, taskbarHandle);
        // Left, Top, Width, and Height are WPF device-independent units. Passing
        // them back to SetWindowPos as physical pixels makes the window shrink
        // on every timer tick when display scaling is enabled. WPF already
        // applies the correct DPI transform, so SetWindowPos only controls the
        // z-order here.
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
        UpdateWidgetTextContrast(taskbar, source.CompositionTarget.TransformToDevice.M11);

        _panel?.Reposition(this);
    }

    private void UpdateWidgetTextContrast(TaskbarLocator.NativeRect taskbar, double scaleX)
    {
        var useDarkText = false;
        if (_viewModel.AutoAdjustWidgetTextColor)
        {
            var widgetRight = taskbar.Left + (int)Math.Ceiling(ActualWidth * scaleX);
            if (!TaskbarContrastDetector.TryShouldUseDarkText(taskbar, widgetRight, out useDarkText))
                return;
        }

        if (_usingDarkWidgetText == useDarkText) return;
        _usingDarkWidgetText = useDarkText;
        Resources["WidgetPrimaryTextBrush"] = new SolidColorBrush(useDarkText
            ? System.Windows.Media.Color.FromRgb(28, 31, 36)
            : System.Windows.Media.Color.FromRgb(245, 247, 255));
        Resources["WidgetSecondaryTextBrush"] = new SolidColorBrush(useDarkText
            ? System.Windows.Media.Color.FromRgb(74, 82, 92)
            : System.Windows.Media.Color.FromRgb(167, 167, 167));
        Resources["WidgetAccentTextBrush"] = new SolidColorBrush(useDarkText
            ? System.Windows.Media.Color.FromRgb(35, 54, 84)
            : System.Windows.Media.Color.FromRgb(216, 228, 255));
    }

    private void AttachToTaskbar(IntPtr windowHandle, IntPtr taskbarHandle = default)
    {
        if (_diagnosticMode || windowHandle == IntPtr.Zero) return;
        if (taskbarHandle == IntPtr.Zero &&
            !TaskbarLocator.TryGetPrimaryTaskbar(out taskbarHandle, out _)) return;

        // 작업 표시줄의 소유 창으로 연결하면 작업 표시줄을 클릭해도
        // 위젯이 그 아래로 내려가지 않아 타이머 기반 복구가 필요하지 않습니다.
        if (GetWindowLongPtr(windowHandle, GwlHwndParent) != taskbarHandle)
            SetWindowLongPtr(windowHandle, GwlHwndParent, taskbarHandle);
    }

    private void OnOverlayClicked(object sender, MouseButtonEventArgs e)
    {
        _widgetMenu?.Hide();
        if (_panel is { IsVisible: true })
        {
            _panel.Hide();
            return;
        }

        if (_panel is null)
        {
            _panel = new AccountPanelWindow(_viewModel, _diagnosticMode)
            {
                Owner = this
            };
        }
        _panel.ShowAnimated(this);
    }

    private void OnOverlayRightClicked(object sender, MouseButtonEventArgs e)
    {
        _panel?.Hide();
        _widgetMenu ??= new WidgetMenuWindow(
            HideFromMenu,
            ExitApplication,
            ToggleAutoContrastAsync,
            () => _viewModel.AutoAdjustWidgetTextColor);
        _widgetMenu.ShowMenu(this);
        e.Handled = true;
    }

    private void HideFromMenu()
    {
        _isUserHidden = !_viewModel.ShowOnlyWhileCodexIsRunning || _codexWindowPresent;
        _isManuallyShown = false;
        HideWidgetCore();
    }

    private void ShowWidget()
    {
        _isUserHidden = false;
        _isManuallyShown = true;
        var becameVisible = ShowWidgetCore();
        if (becameVisible)
            _ = _viewModel.RefreshAllAsync();
    }

    private bool ShowWidgetCore()
    {
        var becameVisible = !IsVisible;
        if (becameVisible) Show();
        PositionOverTaskbar(force: becameVisible);
        return becameVisible;
    }

    private void HideWidgetCore()
    {
        _widgetMenu?.Hide();
        _panel?.Hide();
        if (IsVisible) Hide();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.AutoAdjustWidgetTextColor))
        {
            _autoContrastMenuItem.Checked = _viewModel.AutoAdjustWidgetTextColor;
            _usingDarkWidgetText = null;
            PositionOverTaskbar(force: true);
            return;
        }

        if (e.PropertyName == nameof(MainViewModel.HasSwitchError))
        {
            if (_viewModel.HasSwitchError)
            {
                _isUserHidden = false;
                ShowWidgetCore();
            }
            return;
        }

        if (e.PropertyName != nameof(MainViewModel.IsSwitching)) return;

        if (_viewModel.IsSwitching)
        {
            _restartGraceUntil = DateTime.UtcNow.AddSeconds(15);
            ShowWidgetCore();
        }
        else
        {
            _restartGraceUntil = DateTime.UtcNow.AddSeconds(10);
            _liveUsageTimer.Start();
            _ = SyncActiveUsageAsync();
        }
    }

    private async void ExitApplication()
    {
        _visibilityTimer.Stop();
        _refreshTimer.Stop();
        _liveUsageTimer.Stop();
        _updateCheckTimer.Stop();
        _trayIcon.Visible = false;
        await _liveUsageGate.WaitAsync();
        try { await DisposeLiveUsageClientAsync(); }
        finally { _liveUsageGate.Release(); }
        System.Windows.Application.Current.Shutdown();
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr windowHandle, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr windowHandle, int index, IntPtr newValue);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(IntPtr windowHandle, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
}
