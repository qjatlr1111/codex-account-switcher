using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using ClaudeAccountWidget.Services;
using ClaudeAccountWidget.ViewModels;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace ClaudeAccountWidget;

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
    private const string CodexWidgetTitle = "Codex 계정 위젯";
    private static readonly IntPtr HwndTopmost = new(-1);

    private readonly MainViewModel _viewModel;
    private readonly ClaudeProcessMonitor _processes;
    private readonly StartupRegistrationService _startupRegistration;
    private readonly UpdateCheckService _updateCheckService;
    private readonly UpdateInstallerService _updateInstallerService;
    private readonly DispatcherTimer _updateCheckTimer;
    private readonly SemaphoreSlim _updateCheckGate = new(1, 1);
    private Forms.ToolStripMenuItem _updateMenuItem = null!;
    private UpdateCheckResult? _availableUpdate;
    private bool _isInstallingUpdate;
    private readonly bool _diagnosticMode;
    private readonly DispatcherTimer _visibilityTimer;
    private readonly DispatcherTimer _refreshAllTimer;
    private readonly DispatcherTimer _refreshActiveTimer;
    private readonly DispatcherTimer _clockTimer;
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private AccountPanelWindow? _panel;
    private WidgetMenuWindow? _widgetMenu;
    private readonly Forms.NotifyIcon _trayIcon;
    private readonly Forms.ToolStripMenuItem _autoVisibilityMenuItem;
    private readonly Forms.ToolStripMenuItem _startupMenuItem;
    private readonly Forms.ToolStripMenuItem _autoContrastMenuItem;
    private bool _isUserHidden;
    private bool _isManuallyShown;
    private bool _claudePresent;
    private DateTime _graceUntil;
    private bool? _usingDarkWidgetText;
    private string _lastPlacementKey = string.Empty;

    public OverlayWindow(
        MainViewModel viewModel,
        ClaudeProcessMonitor processes,
        StartupRegistrationService startupRegistration,
        UpdateCheckService updateCheckService,
        UpdateInstallerService updateInstallerService)
    {
        InitializeComponent();
        DataContext = _viewModel = viewModel;
        _processes = processes;
        _startupRegistration = startupRegistration;
        _updateCheckService = updateCheckService;
        _updateInstallerService = updateInstallerService;
        _diagnosticMode = Environment.GetCommandLineArgs()
            .Any(argument => argument.Equals("--diagnostic", StringComparison.OrdinalIgnoreCase));
        if (_diagnosticMode) ShowInTaskbar = true;

        SourceInitialized += (_, _) => ConfigureWindow();

        _visibilityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _visibilityTimer.Tick += async (_, _) => await MonitorClaudeAsync();

        // 사용량 API는 호출 빈도 제한이 있으므로 활성 계정 1분, 전체 5분 주기로만 조회합니다.
        _refreshActiveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
        _refreshActiveTimer.Tick += async (_, _) =>
        {
            if (IsVisible && !_viewModel.IsSwitching) await _viewModel.RefreshActiveAsync();
        };

        _refreshAllTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _refreshAllTimer.Tick += async (_, _) =>
        {
            if (IsVisible && !_viewModel.IsSwitching) await _viewModel.RefreshAllAsync();
        };

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _clockTimer.Tick += (_, _) => _viewModel.NotifyClockTick();

        _updateCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _updateCheckTimer.Tick += async (_, _) => await CheckForUpdatesAsync(manual: false);

        (_trayIcon, _autoVisibilityMenuItem, _startupMenuItem, _autoContrastMenuItem) = CreateTrayIcon();
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
        _autoVisibilityMenuItem.Checked = _viewModel.ShowOnlyWhileClaudeIsRunning;
        _autoContrastMenuItem.Checked = _viewModel.AutoAdjustWidgetTextColor;
        _visibilityTimer.Start();
        _refreshActiveTimer.Start();
        _refreshAllTimer.Start();
        _clockTimer.Start();
        _updateCheckTimer.Start();
        _ = CheckForUpdatesAsync(manual: false);

        if (_diagnosticMode)
        {
            ShowWidgetCore();
            _ = _viewModel.RefreshAllAsync();
            return;
        }

        await MonitorClaudeAsync();
    }

    private (Forms.NotifyIcon, Forms.ToolStripMenuItem, Forms.ToolStripMenuItem, Forms.ToolStripMenuItem) CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("위젯 표시", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        menu.Items.Add("사용량 새로고침", null, (_, _) => Dispatcher.InvokeAsync(_viewModel.RefreshAllAsync));
        menu.Items.Add(new Forms.ToolStripSeparator());

        var autoVisibility = new Forms.ToolStripMenuItem("Claude 실행 중에만 표시")
        {
            Checked = _viewModel.ShowOnlyWhileClaudeIsRunning
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
        _updateMenuItem = new Forms.ToolStripMenuItem("업데이트 확인");
        _updateMenuItem.Click += (_, _) => Dispatcher.InvokeAsync(OnUpdateMenuClickedAsync);
        menu.Items.Add(_updateMenuItem);
        menu.Items.Add("정보", null, (_, _) => Dispatcher.Invoke(ShowAboutWindow));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("끝내기", null, (_, _) => Dispatcher.Invoke(ExitApplication));

        var trayIcon = new Forms.NotifyIcon
        {
            Text = "Claude 계정 위젯",
            Icon = TrayIconFactory.Create(),
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
        trayIcon.BalloonTipClicked += (_, _) => Dispatcher.InvokeAsync(InstallAvailableUpdateAsync);
        return (trayIcon, autoVisibility, startup, autoContrast);
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
                    _trayIcon.ShowBalloonTip(4000, "업데이트 확인", "현재 최신 버전을 사용하고 있습니다.", Forms.ToolTipIcon.Info);
                return;
            }

            _availableUpdate = result;
            _updateMenuItem.Text = $"{result.LatestTagName} 업데이트 설치";
            _trayIcon.ShowBalloonTip(7000, "새 업데이트가 있습니다",
                $"Claude Account Switcher {result.LatestTagName}을 사용할 수 있습니다.", Forms.ToolTipIcon.Info);
        }
        catch
        {
            if (manual)
                _trayIcon.ShowBalloonTip(4000, "업데이트 확인 실패",
                    "GitHub에 연결하지 못했거나 아직 Claude 위젯 릴리스가 없습니다.", Forms.ToolTipIcon.Warning);
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
            "Claude Account Switcher 업데이트",
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
            System.Windows.MessageBox.Show($"업데이트를 설치하지 못했습니다.\n{exception.Message}",
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
        var enabled = !_viewModel.ShowOnlyWhileClaudeIsRunning;
        await _viewModel.SetShowOnlyWhileClaudeIsRunningAsync(enabled);
        _autoVisibilityMenuItem.Checked = enabled;
        _isUserHidden = false;
        _isManuallyShown = false;
        await MonitorClaudeAsync();
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

    private async Task MonitorClaudeAsync()
    {
        if (!await _visibilityGate.WaitAsync(0)) return;

        try
        {
            // 터미널에서 /login으로 계정을 바꾼 경우도 즉시 반영합니다.
            _viewModel.SyncActiveWithClaudeCode();

            if (_diagnosticMode)
            {
                if (IsVisible) PositionOverTaskbar();
                return;
            }

            var wasPresent = _claudePresent;
            _claudePresent = await Task.Run(_processes.IsClaudeRunning);

            if (wasPresent && !_claudePresent)
            {
                _isUserHidden = false;
                _isManuallyShown = false;
            }

            var inGrace = _viewModel.IsSwitching || _viewModel.IsLoginPending || DateTime.UtcNow < _graceUntil;
            var shouldShow = _viewModel.ShowOnlyWhileClaudeIsRunning
                ? _viewModel.HasSwitchError || _isManuallyShown || inGrace || (_claudePresent && !_isUserHidden)
                : !_isUserHidden;

            if (!shouldShow)
            {
                HideWidgetCore();
                return;
            }

            var becameVisible = ShowWidgetCore();
            if (becameVisible) await _viewModel.RefreshAllAsync();
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

        // Codex 계정 위젯이 같은 자리에 떠 있으면 그 오른쪽에 나란히 붙습니다.
        var left = taskbar.Left;
        if (TryGetCodexWidgetRight(taskbar, out var codexRight)) left = codexRight + 2;

        var placementKey = $"{taskbar.Left},{taskbar.Top},{taskbar.Right},{taskbar.Bottom},{left}";
        if (!force && placementKey == _lastPlacementKey) return;
        _lastPlacementKey = placementKey;

        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null) return;

        var fromDevice = source.CompositionTarget.TransformFromDevice;
        var topLeft = fromDevice.Transform(new System.Windows.Point(left, taskbar.Top));
        var bottomRight = fromDevice.Transform(new System.Windows.Point(taskbar.Right, taskbar.Bottom));

        Height = Math.Max(34, bottomRight.Y - topLeft.Y);
        Left = topLeft.X;
        Top = topLeft.Y;

        var handle = new WindowInteropHelper(this).Handle;
        AttachToTaskbar(handle, taskbarHandle);
        // Left/Top/Width/Height는 WPF DIP 단위이므로 SetWindowPos는 z-order만 조정합니다.
        SetWindowPos(handle, HwndTopmost, 0, 0, 0, 0, SwpNoSize | SwpNoMove | SwpNoActivate | SwpShowWindow);
        UpdateWidgetTextContrast(taskbar, left, source.CompositionTarget.TransformToDevice.M11);

        _panel?.Reposition(this);
    }

    private static bool TryGetCodexWidgetRight(TaskbarLocator.NativeRect taskbar, out int right)
    {
        right = 0;
        var codex = FindWindow(null, CodexWidgetTitle);
        if (codex == IntPtr.Zero || !IsWindowVisible(codex) || !GetWindowRect(codex, out var rect)) return false;

        var insideTaskbar = rect.Top >= taskbar.Top - 2 && rect.Bottom <= taskbar.Bottom + 2 &&
                            rect.Left <= taskbar.Left + 4;
        if (!insideTaskbar) return false;
        right = rect.Right;
        return true;
    }

    private void UpdateWidgetTextContrast(TaskbarLocator.NativeRect taskbar, int left, double scaleX)
    {
        var useDarkText = false;
        if (_viewModel.AutoAdjustWidgetTextColor)
        {
            var widgetRight = left + (int)Math.Ceiling(ActualWidth * scaleX);
            if (!TaskbarContrastDetector.TryShouldUseDarkText(taskbar, widgetRight, out useDarkText))
                return;
        }

        if (_usingDarkWidgetText == useDarkText) return;
        _usingDarkWidgetText = useDarkText;
        Resources["WidgetPrimaryTextBrush"] = new SolidColorBrush(useDarkText
            ? System.Windows.Media.Color.FromRgb(36, 28, 24)
            : System.Windows.Media.Color.FromRgb(247, 241, 236));
        Resources["WidgetSecondaryTextBrush"] = new SolidColorBrush(useDarkText
            ? System.Windows.Media.Color.FromRgb(96, 80, 70)
            : System.Windows.Media.Color.FromRgb(177, 167, 158));
    }

    private void AttachToTaskbar(IntPtr windowHandle, IntPtr taskbarHandle = default)
    {
        if (_diagnosticMode || windowHandle == IntPtr.Zero) return;
        if (taskbarHandle == IntPtr.Zero && !TaskbarLocator.TryGetPrimaryTaskbar(out taskbarHandle, out _)) return;

        if (GetWindowLongPtr(windowHandle, GwlHwndParent) != taskbarHandle)
            SetWindowLongPtr(windowHandle, GwlHwndParent, taskbarHandle);
    }

    private void OnOverlayClicked(object sender, MouseButtonEventArgs e)
    {
        _widgetMenu?.Hide();
        _viewModel.ClearSwitchError();
        if (_panel is { IsVisible: true })
        {
            _panel.HideAnimated();
            return;
        }

        _panel ??= new AccountPanelWindow(_viewModel, _diagnosticMode) { Owner = this };
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
        _isUserHidden = !_viewModel.ShowOnlyWhileClaudeIsRunning || _claudePresent;
        _isManuallyShown = false;
        HideWidgetCore();
    }

    private void ShowWidget()
    {
        _isUserHidden = false;
        _isManuallyShown = true;
        if (ShowWidgetCore()) _ = _viewModel.RefreshAllAsync();
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
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.AutoAdjustWidgetTextColor):
                _autoContrastMenuItem.Checked = _viewModel.AutoAdjustWidgetTextColor;
                _usingDarkWidgetText = null;
                PositionOverTaskbar(force: true);
                break;
            case nameof(MainViewModel.HasSwitchError) when _viewModel.HasSwitchError:
                _isUserHidden = false;
                ShowWidgetCore();
                break;
            case nameof(MainViewModel.IsSwitching):
                _graceUntil = DateTime.UtcNow.AddSeconds(_viewModel.IsSwitching ? 30 : 10);
                if (_viewModel.IsSwitching) ShowWidgetCore();
                break;
        }
    }

    private void ExitApplication()
    {
        _visibilityTimer.Stop();
        _refreshAllTimer.Stop();
        _refreshActiveTimer.Stop();
        _clockTimer.Stop();
        _updateCheckTimer.Stop();
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

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? className, string? windowName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out TaskbarLocator.NativeRect rect);
}

/// <summary>트레이 아이콘을 코드로 그립니다(코랄 원 + 흰 스파크).</summary>
internal static class TrayIconFactory
{
    public static Drawing.Icon Create()
    {
        try
        {
            using var bitmap = new Drawing.Bitmap(32, 32);
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = Drawing.Drawing2D.SmoothingMode.AntiAlias;
                graphics.Clear(Drawing.Color.Transparent);
                using var background = new Drawing.Drawing2D.LinearGradientBrush(
                    new Drawing.Rectangle(0, 0, 32, 32),
                    Drawing.Color.FromArgb(246, 193, 119),
                    Drawing.Color.FromArgb(224, 122, 85),
                    45f);
                graphics.FillEllipse(background, 1, 1, 30, 30);

                using var pen = new Drawing.Pen(Drawing.Color.White, 2.6f)
                {
                    StartCap = Drawing.Drawing2D.LineCap.Round,
                    EndCap = Drawing.Drawing2D.LineCap.Round
                };
                for (var index = 0; index < 10; index++)
                {
                    var angle = (index * 36 - 90) * Math.PI / 180;
                    var length = index % 2 == 0 ? 10.5 : 7.0;
                    graphics.DrawLine(pen,
                        (float)(16 + 2 * Math.Cos(angle)), (float)(16 + 2 * Math.Sin(angle)),
                        (float)(16 + length * Math.Cos(angle)), (float)(16 + length * Math.Sin(angle)));
                }
            }

            var handle = bitmap.GetHicon();
            // Icon.FromHandle은 핸들을 소유하지 않으므로 복제본을 만든 뒤 원래 핸들을 해제합니다.
            using var temporary = Drawing.Icon.FromHandle(handle);
            var icon = (Drawing.Icon)temporary.Clone();
            DestroyIcon(handle);
            return icon;
        }
        catch
        {
            return Drawing.SystemIcons.Application;
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
