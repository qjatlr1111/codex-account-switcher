using System.Windows;
using CodexAccountWidget.Models;

namespace CodexAccountWidget;

public partial class SwitchConfirmationWindow : Window
{
    public SwitchConfirmationWindow(AccountProfile profile)
    {
        InitializeComponent();
        DataContext = profile;
    }

    private void OnCancelClicked(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnConfirmClicked(object sender, RoutedEventArgs e) => DialogResult = true;
}
