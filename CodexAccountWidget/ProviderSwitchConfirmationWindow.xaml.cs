using System.Windows;
using CodexAccountWidget.Models;

namespace CodexAccountWidget;

public partial class ProviderSwitchConfirmationWindow : Window
{
    public ProviderSwitchConfirmationWindow(ModelProviderOption provider)
    {
        InitializeComponent();
        DataContext = provider;
        ContentRendered += (_, _) => EntranceAnimation.Play(this);
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
