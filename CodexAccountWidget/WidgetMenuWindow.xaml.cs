using System.Windows;

namespace CodexAccountWidget;

public partial class WidgetMenuWindow : Window
{
    private readonly Action _hideWidget;
    private readonly Action _exitApplication;
    private readonly Func<Task> _toggleAutoContrast;
    private readonly Func<bool> _isAutoContrastEnabled;

    public WidgetMenuWindow(
        Action hideWidget,
        Action exitApplication,
        Func<Task> toggleAutoContrast,
        Func<bool> isAutoContrastEnabled)
    {
        InitializeComponent();
        _hideWidget = hideWidget;
        _exitApplication = exitApplication;
        _toggleAutoContrast = toggleAutoContrast;
        _isAutoContrastEnabled = isAutoContrastEnabled;
    }

    public void ShowMenu(OverlayWindow overlay)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Owner ??= overlay;
        AutoContrastCheckBox.IsChecked = _isAutoContrastEnabled();
        Left = overlay.Left;
        Top = overlay.Top;
        Opacity = 0;
        Show();
        UpdateLayout();
        Top = Math.Max(0, overlay.Top - ActualHeight - 3);
        Activate();
        EntranceAnimation.Play(this);
    }

    private async void OnAutoContrastClicked(object sender, RoutedEventArgs e)
    {
        await _toggleAutoContrast();
        AutoContrastCheckBox.IsChecked = _isAutoContrastEnabled();
    }

    private void OnHideClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        _hideWidget();
    }

    private void OnExitClicked(object sender, RoutedEventArgs e)
    {
        Hide();
        _exitApplication();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (IsVisible) Hide();
    }
}
