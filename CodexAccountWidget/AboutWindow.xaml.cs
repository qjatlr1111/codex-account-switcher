using System.Diagnostics;
using System.Reflection;
using System.Windows;

namespace CodexAccountWidget;

public partial class AboutWindow : Window
{
    private const string RepositoryUrl = "https://github.com/qjatlr1111/codex-account-switcher";

    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "알 수 없음";
        VersionText.Text = $"버전 {version}";
        ContentRendered += (_, _) => EntranceAnimation.Play(this);
    }

    private void OnGitHubClicked(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepositoryUrl) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"GitHub를 열지 못했습니다.\n{exception.Message}",
                "링크 열기 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e) => Close();
}
