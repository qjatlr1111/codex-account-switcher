using System.Runtime.InteropServices;
using System.ComponentModel;
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
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MainViewModel _viewModel;
    private readonly CodexDesktopRestartService _codexDesktop;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly bool _diagnosticMode;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _refreshTimer;
    private AccountPanelWindow? _panel;
    private WidgetMenuWindow? _widgetMenu;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _autoVisibilityMenuItem;
    private readonly Forms.ToolStripMenuItem _startupMenuItem;
    private bool _isUserHidden;
    private bool _isManuallyShown;
    private bool _codexWindowPresent;
    private DateTime _restartGraceUntil;

    public OverlayWindow(
        MainViewModel viewModel,
        CodexDesktopRestartService codexDesktop,
        StartupRegistrationService startupRegistration)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _codexDesktop = codexDesktop;
        _startupRegistration = startupRegistration;
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

        (_trayIcon, _autoVisibilityMenuItem, _startupMenuItem) = CreateTrayIcon();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _trayIcon.Dispose();
        };
    }

    public async Task InitializeAsync()
    {
        if (!_diagnosticMode && !_startupRegistration.TrySetEnabled(_viewModel.StartWithWindows))
            await _viewModel.SetStartWithWindowsAsync(_startupRegistration.IsEnabled());

        _startupMenuItem.Checked = _startupRegistration.IsEnabled();
        _autoVisibilityMenuItem.Checked = _viewModel.ShowOnlyWhileCodexIsRunning;
        _visibilityTimer.Start();
        _refreshTimer.Start();

        if (_diagnosticMode)
        {
            ShowWidgetCore();
            return;
        }

        await MonitorCodexAsync();
    }

    private (Forms.NotifyIcon TrayIcon, Forms.ToolStripMenuItem AutoVisibility, Forms.ToolStripMenuItem Startup) CreateTrayIcon()
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

        var startup = new Forms.ToolStripMenuItem("Windows 시작 시 자동 실행")
        {
            Checked = _startupRegistration.IsEnabled()
        };
        startup.Click += (_, _) => Dispatcher.InvokeAsync(ToggleStartupAsync);
        menu.Items.Add(startup);
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
        return (trayIcon, autoVisibility, startup);
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
        if (_diagnosticMode)
        {
            if (IsVisible) PositionOverTaskbar();
            return;
        }

        var wasPresent = _codexWindowPresent;
        _codexWindowPresent = _codexDesktop.HasRunningWindow();

        if (wasPresent && !_codexWindowPresent)
        {
            _isUserHidden = false;
            _isManuallyShown = false;
        }

        var inRestartGrace = _viewModel.IsSwitching || DateTime.UtcNow < _restartGraceUntil;
        var shouldShow = _viewModel.ShowOnlyWhileCodexIsRunning
            ? _isManuallyShown || inRestartGrace || (_codexWindowPresent && !_isUserHidden)
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

    private void PositionOverTaskbar()
    {
        if (!IsVisible) return;

        if (!TaskbarLocator.TryGetPrimaryTaskbar(out var taskbarHandle, out var taskbar))
        {
            HideWidgetCore();
            return;
        }

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
        SetWindowPos(handle, HwndTopmost, (int)Left, (int)Top, (int)Width, (int)Height,
            SwpNoActivate | SwpShowWindow);

        _panel?.Reposition(this);
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
        _panel.Show();
        _panel.Activate();
        _panel.Reposition(this);
    }

    private void OnOverlayRightClicked(object sender, MouseButtonEventArgs e)
    {
        _panel?.Hide();
        _widgetMenu ??= new WidgetMenuWindow(HideFromMenu, ExitApplication);
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
        PositionOverTaskbar();
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
        if (e.PropertyName != nameof(MainViewModel.IsSwitching)) return;

        if (_viewModel.IsSwitching)
        {
            _restartGraceUntil = DateTime.UtcNow.AddSeconds(15);
            ShowWidgetCore();
        }
        else
        {
            _restartGraceUntil = DateTime.UtcNow.AddSeconds(10);
        }
    }

    private void ExitApplication()
    {
        _visibilityTimer.Stop();
        _refreshTimer.Stop();
        _trayIcon.Visible = false;
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
