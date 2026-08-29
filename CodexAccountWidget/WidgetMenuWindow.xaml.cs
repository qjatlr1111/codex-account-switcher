using System.Windows;

namespace CodexAccountWidget;

public partial class WidgetMenuWindow : Window
{
    private readonly Action _hideWidget;
    private readonly Action _exitApplication;

    public WidgetMenuWindow(Action hideWidget, Action exitApplication)
    {
        InitializeComponent();
        _hideWidget = hideWidget;
        _exitApplication = exitApplication;
    }

    public void ShowMenu(OverlayWindow overlay)
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        Owner ??= overlay;
        Left = overlay.Left;
        Top = overlay.Top;
        Show();
        UpdateLayout();
        Top = Math.Max(0, overlay.Top - ActualHeight - 3);
        Activate();
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
